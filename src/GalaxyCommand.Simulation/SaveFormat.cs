using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace GalaxyCommand.Simulation;

internal static class SaveFormat
{
    internal const string Discriminator = "galaxy-command-save";
    internal const int CurrentSchemaVersion = 1;
}

internal enum SaveFormatFailureKind
{
    StorageAccess,
    DocumentTooLarge,
    InvalidUtf8,
    InvalidJson,
    InvalidSchema,
    WrongFormat,
    UnsupportedSchemaVersion,
    MigrationFailed,
}

internal sealed record SaveFormatFailure(
    SaveFormatFailureKind Kind,
    string Path,
    string Message);

internal sealed class SaveJsonLimits
{
    internal SaveJsonLimits(
        int maximumDocumentBytes,
        int maximumDepth,
        int maximumStringLength,
        int maximumContainerEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDocumentBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStringLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumContainerEntries);
        MaximumDocumentBytes = maximumDocumentBytes;
        MaximumDepth = maximumDepth;
        MaximumStringLength = maximumStringLength;
        MaximumContainerEntries = maximumContainerEntries;
    }

    internal int MaximumDocumentBytes { get; }

    internal int MaximumDepth { get; }

    internal int MaximumStringLength { get; }

    internal int MaximumContainerEntries { get; }
}

internal sealed class InertSaveDocument
{
    internal InertSaveDocument(int schemaVersion, JsonElement root)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        SchemaVersion = schemaVersion;
        Root = root.Clone();
    }

    internal int SchemaVersion { get; }

    internal JsonElement Root { get; }
}

internal sealed class SaveReadResult
{
    private SaveReadResult(
        InertSaveDocument? document,
        SaveFormatFailure? failure)
    {
        Document = document;
        Failure = failure;
    }

    internal InertSaveDocument? Document { get; }

    internal SaveFormatFailure? Failure { get; }

    internal bool IsSuccess => Document is not null;

    internal static SaveReadResult Success(InertSaveDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new SaveReadResult(document, null);
    }

    internal static SaveReadResult Rejected(SaveFormatFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new SaveReadResult(null, failure);
    }
}

internal sealed class SaveWriteResult
{
    private SaveWriteResult(int bytesWritten, SaveFormatFailure? failure)
    {
        BytesWritten = bytesWritten;
        Failure = failure;
    }

    internal int BytesWritten { get; }

    internal SaveFormatFailure? Failure { get; }

    internal bool IsSuccess => Failure is null;

    internal static SaveWriteResult Success(int bytesWritten) =>
        new(bytesWritten, null);

    internal static SaveWriteResult Rejected(SaveFormatFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new SaveWriteResult(0, failure);
    }
}

internal sealed class SaveMigrationResult
{
    private SaveMigrationResult(
        InertSaveDocument? document,
        SaveFormatFailure? failure)
    {
        Document = document;
        Failure = failure;
    }

    internal InertSaveDocument? Document { get; }

    internal SaveFormatFailure? Failure { get; }

    internal bool IsSuccess => Document is not null;

    internal static SaveMigrationResult Success(InertSaveDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new SaveMigrationResult(document, null);
    }

    internal static SaveMigrationResult Rejected(SaveFormatFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new SaveMigrationResult(null, failure);
    }
}

internal interface ISaveSchemaMigration
{
    int SourceVersion { get; }

    int TargetVersion { get; }

    SaveMigrationResult Migrate(InertSaveDocument source);
}

internal sealed class SaveMigrationRegistry
{
    private readonly ReadOnlyDictionary<int, ISaveSchemaMigration> _migrations;

    /// <summary>
    /// Creates one deterministic contiguous migration chain ending at the
    /// current schema version.
    /// </summary>
    internal SaveMigrationRegistry(
        int currentSchemaVersion,
        IEnumerable<ISaveSchemaMigration> migrations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentSchemaVersion);
        ArgumentNullException.ThrowIfNull(migrations);

        ISaveSchemaMigration[] values = migrations
            .OrderBy(migration => migration.SourceVersion)
            .ToArray();
        var bySource = new Dictionary<int, ISaveSchemaMigration>();
        foreach (ISaveSchemaMigration migration in values)
        {
            ArgumentNullException.ThrowIfNull(migration);
            if (migration.SourceVersion <= 0 ||
                migration.TargetVersion != migration.SourceVersion + 1 ||
                migration.TargetVersion > currentSchemaVersion)
            {
                throw new ArgumentException(
                    "Every save migration must advance one positive schema version toward the current version.",
                    nameof(migrations));
            }

            if (!bySource.TryAdd(migration.SourceVersion, migration))
            {
                throw new ArgumentException(
                    $"Duplicate migration from schema version {migration.SourceVersion}.",
                    nameof(migrations));
            }
        }

        if (values.Length > 0)
        {
            int firstVersion = values[0].SourceVersion;
            for (int version = firstVersion; version < currentSchemaVersion; version++)
            {
                if (!bySource.ContainsKey(version))
                {
                    throw new ArgumentException(
                        $"Missing migration from schema version {version}.",
                        nameof(migrations));
                }
            }
        }

        CurrentSchemaVersion = currentSchemaVersion;
        _migrations = new ReadOnlyDictionary<int, ISaveSchemaMigration>(bySource);
    }

    internal int CurrentSchemaVersion { get; }

    /// <summary>
    /// Applies the one registered migration for every historical version and
    /// rejects gaps, future schemas, and inconsistent migration output.
    /// </summary>
    internal SaveMigrationResult MigrateToCurrent(
        InertSaveDocument source,
        SaveJsonLimits limits)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        if (source.SchemaVersion > CurrentSchemaVersion)
        {
            return Unsupported(source.SchemaVersion);
        }

        if (!SaveJsonCodec.HasEnvelopeVersion(
                source.Root,
                source.SchemaVersion))
        {
            return SaveMigrationResult.Rejected(
                new SaveFormatFailure(
                    SaveFormatFailureKind.MigrationFailed,
                    "$.schemaVersion",
                    "The migration source version does not match its envelope."));
        }

        InertSaveDocument current = source;
        while (current.SchemaVersion < CurrentSchemaVersion)
        {
            if (!_migrations.TryGetValue(
                    current.SchemaVersion,
                    out ISaveSchemaMigration? migration))
            {
                return Unsupported(current.SchemaVersion);
            }

            SaveMigrationResult result = migration.Migrate(current);
            if (!result.IsSuccess)
            {
                return result;
            }

            InertSaveDocument migrated = result.Document!;
            SaveFormatFailure? structureFailure = SaveJsonCodec.ValidateStructure(
                migrated.Root,
                limits);
            if (structureFailure is not null ||
                migrated.SchemaVersion != migration.TargetVersion ||
                !SaveJsonCodec.HasEnvelopeVersion(
                    migrated.Root,
                    migration.TargetVersion))
            {
                return SaveMigrationResult.Rejected(
                    new SaveFormatFailure(
                        SaveFormatFailureKind.MigrationFailed,
                        "$.schemaVersion",
                        $"Migration from schema version {migration.SourceVersion} produced an inconsistent target document."));
            }

            current = migrated;
        }

        return SaveMigrationResult.Success(current);
    }

    private SaveMigrationResult Unsupported(int schemaVersion) =>
        SaveMigrationResult.Rejected(
            new SaveFormatFailure(
                SaveFormatFailureKind.UnsupportedSchemaVersion,
                "$.schemaVersion",
                $"Schema version {schemaVersion} cannot be migrated to version {CurrentSchemaVersion}."));
}

internal static class SaveJsonCodec
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Reads one bounded JSON document, validates its generic save envelope,
    /// and deterministically migrates it to the current schema.
    /// </summary>
    internal static SaveReadResult Read(
        Stream source,
        SaveJsonLimits limits,
        SaveMigrationRegistry migrations)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(migrations);

        byte[] bytes;
        try
        {
            SaveFormatFailure? readFailure = ReadBounded(
                source,
                limits.MaximumDocumentBytes,
                out bytes);
            if (readFailure is not null)
            {
                return SaveReadResult.Rejected(readFailure);
            }
        }
        catch (IOException)
        {
            return SaveReadResult.Rejected(StorageFailure());
        }
        catch (UnauthorizedAccessException)
        {
            return SaveReadResult.Rejected(StorageFailure());
        }

        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
        }
        catch (DecoderFallbackException)
        {
            return SaveReadResult.Rejected(
                new SaveFormatFailure(
                    SaveFormatFailureKind.InvalidUtf8,
                    "$",
                    "The save is not valid UTF-8."));
        }

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = limits.MaximumDepth,
                });
        }
        catch (JsonException)
        {
            return SaveReadResult.Rejected(
                new SaveFormatFailure(
                    SaveFormatFailureKind.InvalidJson,
                    "$",
                    "The save is not valid strict JSON."));
        }

        using (parsed)
        {
            SaveFormatFailure? structureFailure = ValidateStructure(
                parsed.RootElement,
                limits);
            if (structureFailure is not null)
            {
                return SaveReadResult.Rejected(structureFailure);
            }

            SaveFormatFailure? envelopeFailure = ReadEnvelope(
                parsed.RootElement,
                out int schemaVersion);
            if (envelopeFailure is not null)
            {
                return SaveReadResult.Rejected(envelopeFailure);
            }

            var inert = new InertSaveDocument(
                schemaVersion,
                parsed.RootElement);
            SaveMigrationResult migration = migrations.MigrateToCurrent(
                inert,
                limits);
            return migration.IsSuccess
                ? SaveReadResult.Success(migration.Document!)
                : SaveReadResult.Rejected(migration.Failure!);
        }
    }

    /// <summary>
    /// Writes stable indented JSON after validating the in-memory document
    /// against the same generic limits and save envelope used by the reader.
    /// </summary>
    internal static SaveWriteResult Write(
        Stream destination,
        InertSaveDocument document,
        SaveJsonLimits limits)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(limits);

        SaveFormatFailure? structureFailure = ValidateStructure(
            document.Root,
            limits);
        if (structureFailure is not null)
        {
            return SaveWriteResult.Rejected(structureFailure);
        }

        SaveFormatFailure? envelopeFailure = ReadEnvelope(
            document.Root,
            out int schemaVersion);
        if (envelopeFailure is not null)
        {
            return SaveWriteResult.Rejected(envelopeFailure);
        }

        if (schemaVersion != document.SchemaVersion)
        {
            return SaveWriteResult.Rejected(
                new SaveFormatFailure(
                    SaveFormatFailureKind.InvalidSchema,
                    "$.schemaVersion",
                    "The document schema version does not match its envelope."));
        }

        if (schemaVersion != SaveFormat.CurrentSchemaVersion)
        {
            return SaveWriteResult.Rejected(
                new SaveFormatFailure(
                    SaveFormatFailureKind.UnsupportedSchemaVersion,
                    "$.schemaVersion",
                    "The writer only emits the current save schema version."));
        }

        using var encoded = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   encoded,
                   new JsonWriterOptions { Indented = true }))
        {
            document.Root.WriteTo(writer);
        }

        encoded.WriteByte((byte)'\n');
        if (encoded.Length > limits.MaximumDocumentBytes)
        {
            return SaveWriteResult.Rejected(TooLarge(limits.MaximumDocumentBytes));
        }

        try
        {
            encoded.Position = 0;
            encoded.CopyTo(destination);
        }
        catch (IOException)
        {
            return SaveWriteResult.Rejected(StorageFailure());
        }
        catch (UnauthorizedAccessException)
        {
            return SaveWriteResult.Rejected(StorageFailure());
        }

        return SaveWriteResult.Success(checked((int)encoded.Length));
    }

    /// <summary>
    /// Confirms that a migration result retains the format discriminator and
    /// declares its expected target schema version.
    /// </summary>
    internal static bool HasEnvelopeVersion(JsonElement root, int expectedVersion) =>
        ReadEnvelope(root, out int version) is null &&
        version == expectedVersion;

    /// <summary>
    /// Reads no more than the configured byte limit plus one sentinel byte so
    /// oversized streams are rejected without buffering their remaining data.
    /// </summary>
    private static SaveFormatFailure? ReadBounded(
        Stream source,
        int maximumBytes,
        out byte[] bytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[Math.Min(81920, maximumBytes)];
        while (buffer.Length <= maximumBytes)
        {
            long remainingWithSentinel =
                (long)maximumBytes + 1 - buffer.Length;
            int read = source.Read(
                chunk,
                0,
                (int)Math.Min(chunk.Length, remainingWithSentinel));
            if (read == 0)
            {
                bytes = buffer.ToArray();
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        bytes = Array.Empty<byte>();
        return TooLarge(maximumBytes);
    }

    /// <summary>
    /// Recursively enforces duplicate-property, string-length, and
    /// per-container entry limits over one already parsed JSON value.
    /// </summary>
    internal static SaveFormatFailure? ValidateStructure(
        JsonElement root,
        SaveJsonLimits limits) =>
        ValidateElement(root, "$", limits);

    /// <summary>
    /// Recursively enforces duplicate-property, string-length, and
    /// per-container entry limits without exposing arbitrary property names in
    /// a returned diagnostic path.
    /// </summary>
    private static SaveFormatFailure? ValidateElement(
        JsonElement element,
        string path,
        SaveJsonLimits limits)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            string value = element.GetString()!;
            return value.Length > limits.MaximumStringLength
                ? StringTooLong(path, limits.MaximumStringLength)
                : null;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (index >= limits.MaximumContainerEntries)
                {
                    return ContainerTooLarge(path, limits.MaximumContainerEntries);
                }

                SaveFormatFailure? failure = ValidateElement(
                    item,
                    $"{path}[{index}]",
                    limits);
                if (failure is not null)
                {
                    return failure;
                }

                index++;
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        int propertyIndex = 0;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (propertyIndex >= limits.MaximumContainerEntries)
            {
                return ContainerTooLarge(path, limits.MaximumContainerEntries);
            }

            if (property.Name.Length > limits.MaximumStringLength)
            {
                return StringTooLong(path, limits.MaximumStringLength);
            }

            if (!names.Add(property.Name))
            {
                return new SaveFormatFailure(
                    SaveFormatFailureKind.InvalidJson,
                    path,
                    "A JSON object contains a duplicate property name.");
            }

            SaveFormatFailure? failure = ValidateElement(
                property.Value,
                $"{path}[{propertyIndex}]",
                limits);
            if (failure is not null)
            {
                return failure;
            }

            propertyIndex++;
        }

        return null;
    }

    /// <summary>
    /// Validates the format discriminator and positive integer schema version
    /// shared by every historical and current save document.
    /// </summary>
    private static SaveFormatFailure? ReadEnvelope(
        JsonElement root,
        out int schemaVersion)
    {
        schemaVersion = 0;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new SaveFormatFailure(
                SaveFormatFailureKind.InvalidSchema,
                "$",
                "The save root must be a JSON object.");
        }

        if (!root.TryGetProperty("format", out JsonElement format) ||
            format.ValueKind != JsonValueKind.String ||
            !string.Equals(
                format.GetString(),
                SaveFormat.Discriminator,
                StringComparison.Ordinal))
        {
            return new SaveFormatFailure(
                SaveFormatFailureKind.WrongFormat,
                "$.format",
                "The document is not a Galaxy Command save.");
        }

        if (!root.TryGetProperty(
                "schemaVersion",
                out JsonElement schema) ||
            schema.ValueKind != JsonValueKind.Number ||
            !schema.TryGetInt32(out schemaVersion) ||
            schemaVersion <= 0)
        {
            return new SaveFormatFailure(
                SaveFormatFailureKind.InvalidSchema,
                "$.schemaVersion",
                "The save schema version must be a positive integer.");
        }

        return null;
    }

    private static SaveFormatFailure TooLarge(int maximumBytes) =>
        new(
            SaveFormatFailureKind.DocumentTooLarge,
            "$",
            $"The save exceeds the configured {maximumBytes}-byte limit.");

    private static SaveFormatFailure StringTooLong(
        string path,
        int maximumLength) =>
        new(
            SaveFormatFailureKind.InvalidSchema,
            path,
            $"A JSON string exceeds the configured {maximumLength}-character limit.");

    private static SaveFormatFailure ContainerTooLarge(
        string path,
        int maximumEntries) =>
        new(
            SaveFormatFailureKind.InvalidSchema,
            path,
            $"A JSON container exceeds the configured {maximumEntries}-entry limit.");

    private static SaveFormatFailure StorageFailure() =>
        new(
            SaveFormatFailureKind.StorageAccess,
            "$",
            "The save stream could not be accessed.");
}
