using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SaveFormatTests
{
    private static readonly SaveJsonLimits DefaultLimits =
        new(
            maximumDocumentBytes: 16_384,
            maximumDepth: 16,
            maximumStringLength: 1_024,
            maximumContainerEntries: 128);

    [Fact]
    public void ReaderAcceptsExternallyEditedWhitespaceAndPropertyOrder()
    {
        const string edited = """
            {
              "checkpoint": { "engine": { "currentTime": "500" } },
              "schemaVersion": 1,
              "format": "galaxy-command-save"
            }
            """;

        SaveReadResult result = Read(edited);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Document!.SchemaVersion);
        Assert.Equal(
            "500",
            result.Document.Root
                .GetProperty("checkpoint")
                .GetProperty("engine")
                .GetProperty("currentTime")
                .GetString());
    }

    [Fact]
    public void WriterProducesReadableIndentedJsonWithTrailingNewline()
    {
        InertSaveDocument document = Read(CurrentDocument()).Document!;
        using var destination = new MemoryStream();

        SaveWriteResult result = SaveJsonCodec.Write(
            destination,
            document,
            DefaultLimits);

        Assert.True(result.IsSuccess);
        Assert.Equal(destination.Length, result.BytesWritten);
        string encoded = Encoding.UTF8.GetString(destination.ToArray());
        Assert.Contains("\n  \"schemaVersion\": 1,", encoded, StringComparison.Ordinal);
        Assert.EndsWith("\n", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("\uFEFF", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void WriterRejectsNonCurrentSchema()
    {
        using JsonDocument parsed = JsonDocument.Parse("""
            {
              "format": "galaxy-command-save",
              "schemaVersion": 2
            }
            """);
        var document = new InertSaveDocument(2, parsed.RootElement);
        using var destination = new MemoryStream();

        SaveWriteResult result = SaveJsonCodec.Write(
            destination,
            document,
            DefaultLimits);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            SaveFormatFailureKind.UnsupportedSchemaVersion,
            result.Failure!.Kind);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public void ReaderRejectsDuplicateProperties()
    {
        const string duplicate = """
            {
              "format": "galaxy-command-save",
              "schemaVersion": 1,
              "schemaVersion": 1
            }
            """;

        SaveFormatFailure failure = AssertFailure(Read(duplicate));

        Assert.Equal(SaveFormatFailureKind.InvalidJson, failure.Kind);
        Assert.Equal("$", failure.Path);
    }

    [Fact]
    public void ReaderRejectsInvalidUtf8SeparatelyFromInvalidJson()
    {
        byte[] prefix = Encoding.UTF8.GetBytes(
            "{\"format\":\"galaxy-command-save\",\"schemaVersion\":1,\"value\":\"");
        byte[] suffix = Encoding.UTF8.GetBytes("\"}");
        byte[] bytes = [.. prefix, 0xff, .. suffix];

        SaveFormatFailure failure = AssertFailure(Read(bytes));

        Assert.Equal(SaveFormatFailureKind.InvalidUtf8, failure.Kind);
    }

    [Fact]
    public void ReaderRejectsWrongFormatDiscriminator()
    {
        const string wrongFormat = """
            {
              "format": "some-other-save",
              "schemaVersion": 1
            }
            """;

        SaveFormatFailure failure = AssertFailure(Read(wrongFormat));

        Assert.Equal(SaveFormatFailureKind.WrongFormat, failure.Kind);
        Assert.Equal("$.format", failure.Path);
    }

    [Fact]
    public void ReaderRejectsUnsupportedFutureSchema()
    {
        const string future = """
            {
              "format": "galaxy-command-save",
              "schemaVersion": 2
            }
            """;

        SaveFormatFailure failure = AssertFailure(Read(future));

        Assert.Equal(
            SaveFormatFailureKind.UnsupportedSchemaVersion,
            failure.Kind);
        Assert.Equal("$.schemaVersion", failure.Path);
    }

    [Fact]
    public void ReaderRejectsDocumentBeyondByteLimitWithoutReadingRemainder()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(CurrentDocument());
        var limits = new SaveJsonLimits(
            maximumDocumentBytes: bytes.Length - 1,
            maximumDepth: 16,
            maximumStringLength: 1_024,
            maximumContainerEntries: 128);

        SaveFormatFailure failure = AssertFailure(Read(bytes, limits));

        Assert.Equal(SaveFormatFailureKind.DocumentTooLarge, failure.Kind);
    }

    [Fact]
    public void ReaderRejectsStringBeyondConfiguredLimit()
    {
        const string document = """
            {
              "format": "galaxy-command-save",
              "schemaVersion": 1,
              "value": "12345"
            }
            """;
        var limits = new SaveJsonLimits(
            maximumDocumentBytes: 1_024,
            maximumDepth: 16,
            maximumStringLength: 4,
            maximumContainerEntries: 128);

        SaveFormatFailure failure = AssertFailure(Read(document, limits));

        Assert.Equal(SaveFormatFailureKind.InvalidSchema, failure.Kind);
    }

    [Fact]
    public void ReaderRejectsContainerBeyondConfiguredEntryLimit()
    {
        const string document = """
            {
              "format": "galaxy-command-save",
              "schemaVersion": 1,
              "first": true
            }
            """;
        var limits = new SaveJsonLimits(
            maximumDocumentBytes: 1_024,
            maximumDepth: 16,
            maximumStringLength: 1_024,
            maximumContainerEntries: 2);

        SaveFormatFailure failure = AssertFailure(Read(document, limits));

        Assert.Equal(SaveFormatFailureKind.InvalidSchema, failure.Kind);
    }

    [Fact]
    public void RegistryAppliesEveryMigrationInVersionOrder()
    {
        var registry = new SaveMigrationRegistry(
            currentSchemaVersion: 3,
            [new VersionMigration(1, 2), new VersionMigration(2, 3)]);
        const string versionOne = """
            {
              "format": "galaxy-command-save",
              "schemaVersion": 1,
              "migrationHistory": "1"
            }
            """;

        SaveReadResult result = Read(versionOne, DefaultLimits, registry);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Document!.SchemaVersion);
        Assert.Equal(
            "1,2,3",
            result.Document.Root.GetProperty("migrationHistory").GetString());
    }

    [Fact]
    public void RegistryRejectsMigrationGapAtConstruction()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new SaveMigrationRegistry(
                currentSchemaVersion: 3,
                [new VersionMigration(1, 2)]));

        Assert.Contains("Missing migration", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryReturnsTypedMigrationFailure()
    {
        var registry = new SaveMigrationRegistry(
            currentSchemaVersion: 2,
            [new RejectingMigration()]);

        SaveFormatFailure failure = AssertFailure(
            Read(CurrentDocument(), DefaultLimits, registry));

        Assert.Equal(SaveFormatFailureKind.MigrationFailed, failure.Kind);
        Assert.Equal("$.checkpoint", failure.Path);
    }

    [Fact]
    public void RegistryRejectsInconsistentMigrationTarget()
    {
        var registry = new SaveMigrationRegistry(
            currentSchemaVersion: 2,
            [new InconsistentMigration()]);

        SaveFormatFailure failure = AssertFailure(
            Read(CurrentDocument(), DefaultLimits, registry));

        Assert.Equal(SaveFormatFailureKind.MigrationFailed, failure.Kind);
        Assert.Equal("$.schemaVersion", failure.Path);
    }

    [Fact]
    public void RegistryRejectsSourceVersionThatDisagreesWithEnvelope()
    {
        using JsonDocument parsed = JsonDocument.Parse(CurrentDocument());
        var source = new InertSaveDocument(2, parsed.RootElement);
        var registry = new SaveMigrationRegistry(
            currentSchemaVersion: 2,
            Array.Empty<ISaveSchemaMigration>());

        SaveMigrationResult result = registry.MigrateToCurrent(
            source,
            DefaultLimits);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            SaveFormatFailureKind.MigrationFailed,
            result.Failure!.Kind);
        Assert.Equal("$.schemaVersion", result.Failure.Path);
    }

    private static string CurrentDocument() => """
        {
          "format": "galaxy-command-save",
          "schemaVersion": 1,
          "checkpoint": {}
        }
        """;

    private static SaveReadResult Read(
        string document,
        SaveJsonLimits? limits = null,
        SaveMigrationRegistry? migrations = null) =>
        Read(Encoding.UTF8.GetBytes(document), limits, migrations);

    private static SaveReadResult Read(
        byte[] bytes,
        SaveJsonLimits? limits = null,
        SaveMigrationRegistry? migrations = null)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return SaveJsonCodec.Read(
            stream,
            limits ?? DefaultLimits,
            migrations ?? new SaveMigrationRegistry(
                SaveFormat.CurrentSchemaVersion,
                Array.Empty<ISaveSchemaMigration>()));
    }

    private static SaveFormatFailure AssertFailure(SaveReadResult result)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Document);
        return Assert.IsType<SaveFormatFailure>(result.Failure);
    }

    private sealed class VersionMigration : ISaveSchemaMigration
    {
        internal VersionMigration(int sourceVersion, int targetVersion)
        {
            SourceVersion = sourceVersion;
            TargetVersion = targetVersion;
        }

        public int SourceVersion { get; }

        public int TargetVersion { get; }

        public SaveMigrationResult Migrate(InertSaveDocument source)
        {
            JsonObject root = JsonNode.Parse(source.Root.GetRawText())!.AsObject();
            root["schemaVersion"] = TargetVersion;
            string history = root["migrationHistory"]!.GetValue<string>();
            root["migrationHistory"] = $"{history},{TargetVersion}";
            JsonElement migrated = JsonSerializer.SerializeToElement(root);
            return SaveMigrationResult.Success(
                new InertSaveDocument(TargetVersion, migrated));
        }
    }

    private sealed class RejectingMigration : ISaveSchemaMigration
    {
        public int SourceVersion => 1;

        public int TargetVersion => 2;

        public SaveMigrationResult Migrate(InertSaveDocument source) =>
            SaveMigrationResult.Rejected(
                new SaveFormatFailure(
                    SaveFormatFailureKind.MigrationFailed,
                    "$.checkpoint",
                    "The historical checkpoint cannot be migrated."));
    }

    private sealed class InconsistentMigration : ISaveSchemaMigration
    {
        public int SourceVersion => 1;

        public int TargetVersion => 2;

        public SaveMigrationResult Migrate(InertSaveDocument source) =>
            SaveMigrationResult.Success(source);
    }
}
