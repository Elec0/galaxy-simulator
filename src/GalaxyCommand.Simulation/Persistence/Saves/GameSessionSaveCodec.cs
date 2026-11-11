using System.Globalization;
using System.Text.Json;

namespace GalaxyCommand.Simulation;

internal sealed record GameSessionSaveMetadata(Guid SaveId, DateTimeOffset CapturedAtUtc, string Producer);

/// <summary>
/// Connects the bounded inert format to current authoritative checkpoints.
/// Content-reference migration remains a separate compatibility boundary.
/// </summary>
internal static class GameSessionSaveCodec
{
    private static readonly CheckpointValueCodec Values = CreateValues();

    /// <summary>
    /// Encodes every current owner with explicit envelope metadata. This captures
    /// inert authority only; runtime admission still requires isolated restore.
    /// </summary>
    internal static CheckpointResult<InertSaveDocument> Encode(GameSessionCheckpoint checkpoint,
        GameSessionSaveMetadata metadata, SaveJsonLimits limits)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(limits);
        if (metadata.CapturedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(metadata.Producer))
            return Reject<InertSaveDocument>("$", "UTC capture time and producer identity are required.");
        try
        {
            JsonElement state = Values.Encode(checkpoint);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { MaxDepth = 256 }))
            {
                writer.WriteStartObject();
                writer.WriteString("format", SaveFormat.Discriminator);
                writer.WriteNumber("schemaVersion", SaveFormat.CurrentSchemaVersion);
                writer.WriteString("saveId", metadata.SaveId.ToString("D", CultureInfo.InvariantCulture));
                writer.WriteString("capturedAtUtc", metadata.CapturedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
                writer.WriteString("producer", metadata.Producer);
                writer.WriteStartObject("compatibility");
                writer.WritePropertyName("runtimePolicies");
                state.GetProperty("runtimePolicies").WriteTo(writer);
                writer.WriteEndObject();
                writer.WritePropertyName("checkpoint"); state.WriteTo(writer);
                writer.WriteEndObject();
            }
            using JsonDocument root = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = 256 });
            var document = new InertSaveDocument(SaveFormat.CurrentSchemaVersion, root.RootElement);
            using var bounded = new MemoryStream();
            SaveWriteResult validation = SaveJsonCodec.Write(bounded, document, TypedLimits(limits));
            return validation.IsSuccess ? CheckpointResult<InertSaveDocument>.Success(document)
                : Reject<InertSaveDocument>(validation.Failure!.Path, validation.Failure.Message, FormatKind(validation.Failure.Kind));
        }
        catch (CheckpointJsonException error)
        {
            return Reject<InertSaveDocument>("$.checkpoint" + error.Path[1..], error.Message);
        }
    }

    /// <summary>
    /// Validates the current envelope and inert owner sections without publishing a session.
    /// Every optional owner must be explicitly present, using null only where its contract permits.
    /// </summary>
    internal static CheckpointResult<GameSessionCheckpoint> Decode(InertSaveDocument document, SaveJsonLimits limits)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(limits);
        // Reuse strict format checks even when callers constructed the inert document directly.
        using var bytes = new MemoryStream();
        SaveWriteResult validation = SaveJsonCodec.Write(bytes, document, TypedLimits(limits));
        if (!validation.IsSuccess) return Reject<GameSessionCheckpoint>(validation.Failure!.Path, validation.Failure.Message, FormatKind(validation.Failure.Kind));
        try
        {
            JsonElement root = document.Root;
            CheckpointValueCodec.RequireMembers(root,
                ["format", "schemaVersion", "saveId", "capturedAtUtc", "producer", "compatibility", "checkpoint"], "$");
            string? saveId = root.GetProperty("saveId").GetString();
            if (!Guid.TryParseExact(saveId, "D", out Guid id) || id.ToString("D", CultureInfo.InvariantCulture) != saveId)
                return Reject<GameSessionCheckpoint>("$.saveId", "A lowercase canonical save UUID is required.");
            string? timestamp = root.GetProperty("capturedAtUtc").GetString();
            if (!IsUtcTimestamp(timestamp))
                return Reject<GameSessionCheckpoint>("$.capturedAtUtc", "An RFC 3339 UTC capture timestamp is required.");
            if (string.IsNullOrWhiteSpace(root.GetProperty("producer").GetString()))
                return Reject<GameSessionCheckpoint>("$.producer", "A producer identity is required.");
            JsonElement compatibility = root.GetProperty("compatibility");
            CheckpointValueCodec.RequireMembers(compatibility, ["runtimePolicies"], "$.compatibility");
            GameSessionCheckpoint checkpoint;
            try { checkpoint = Values.Decode<GameSessionCheckpoint>(root.GetProperty("checkpoint")); }
            catch (CheckpointJsonException error)
            { return Reject<GameSessionCheckpoint>("$.checkpoint" + error.Path[1..], error.Message); }
            RuntimePolicyManifestCheckpoint policies;
            try { policies = Values.Decode<RuntimePolicyManifestCheckpoint>(compatibility.GetProperty("runtimePolicies")); }
            catch (CheckpointJsonException error)
            { return Reject<GameSessionCheckpoint>("$.compatibility.runtimePolicies" + error.Path[1..], error.Message); }
            if (Values.Encode(policies).GetRawText() != Values.Encode(checkpoint.RuntimePolicies).GetRawText())
                return Reject<GameSessionCheckpoint>("$.compatibility.runtimePolicies", "Runtime compatibility policies disagree with checkpoint authority.");
            return CheckpointResult<GameSessionCheckpoint>.Success(checkpoint);
        }
        catch (CheckpointJsonException error) { return Reject<GameSessionCheckpoint>(error.Path, error.Message); }
        catch (InvalidOperationException) { return Reject<GameSessionCheckpoint>("$", "Invalid typed save envelope."); }
    }

    /// <summary>
    /// Admits all decoded owners privately using current explicit content dependencies.
    /// Failure never returns a partial session or mutates an existing session.
    /// Host worker tuning is supplied outside the document and cannot alter authority.
    /// </summary>
    internal static CheckpointResult<GameSession> Restore(InertSaveDocument document, SaveJsonLimits limits,
        PhysicalDefinitionCatalog? definitions = null, MaterialInventoryCompatibilityMap? materialCompatibility = null,
        InteractionBatchExecution? interactionExecution = null)
    {
        CheckpointResult<GameSessionCheckpoint> decoded = Decode(document, limits);
        if (!decoded.IsSuccess) return CheckpointResult<GameSession>.Rejected(decoded.Failure!);
        if (definitions is null && materialCompatibility is not null)
            return Reject<GameSession>("$.compatibility", "Material compatibility requires current physical definitions.", CheckpointFailureKind.IncompatibleContent);
        GameSessionCheckpoint checkpoint = decoded.Value!;
        try
        {
            CheckpointResult<GameSession> admitted = definitions is null
                ? interactionExecution is null ? GameSession.RestoreCheckpoint(checkpoint)
                    : GameSession.RestoreCheckpoint(checkpoint, interactionExecution)
                : materialCompatibility is null
                    ? interactionExecution is null ? GameSession.RestoreCheckpoint(checkpoint, definitions)
                        : GameSession.RestoreCheckpoint(checkpoint, definitions, interactionExecution)
                    : interactionExecution is null ? GameSession.RestoreCheckpoint(checkpoint, definitions, materialCompatibility)
                        : GameSession.RestoreCheckpoint(checkpoint, definitions, materialCompatibility, interactionExecution);
            if (admitted.Failure is { } failure && failure.Path.StartsWith("$.checkpoint.runtimePolicies", StringComparison.Ordinal))
                return CheckpointResult<GameSession>.Rejected(failure with { Kind = CheckpointFailureKind.IncompatibleRuntimePolicy });
            return admitted;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        {
            // Owner constructors remain isolated; never echo values or internal exception details.
            return Reject<GameSession>("$.checkpoint", "Authoritative checkpoint restoration failed.", CheckpointFailureKind.InvalidCheckpoint);
        }
        catch (Exception error) when (error is NullReferenceException or IndexOutOfRangeException or NotSupportedException or InvalidCastException)
        {
            return Reject<GameSession>("$.checkpoint", "Unexpected checkpoint restoration failure.", CheckpointFailureKind.UnexpectedFailure);
        }
    }

    /// <summary>Accepts RFC 3339 UTC spellings without letting display metadata select local time.</summary>
    private static bool IsUtcTimestamp(string? value)
    {
        if (value is null) return false;
        string utc = value.EndsWith("+00:00", StringComparison.Ordinal) ? value[..^6] + "Z" : value;
        utc = utc.Replace('t', 'T').Replace('z', 'Z');
        return DateTimeOffset.TryParseExact(utc,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _);
    }

    /// <summary>Applies the typed schema's recursion ceiling before traversing even caller-created inert documents.</summary>
    private static SaveJsonLimits TypedLimits(SaveJsonLimits limits) => new(limits.MaximumDocumentBytes,
        Math.Min(limits.MaximumDepth, 128), limits.MaximumStringLength, limits.MaximumContainerEntries);

    private static CheckpointValueCodec CreateValues()
    {
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);
        return codec;
    }

    private static CheckpointResult<T> Reject<T>(string path, string message,
        CheckpointFailureKind kind = CheckpointFailureKind.InvalidSchema) where T : class =>
        CheckpointResult<T>.Rejected(new CheckpointValidationFailure(path, message, kind));

    private static CheckpointFailureKind FormatKind(SaveFormatFailureKind kind) =>
        kind == SaveFormatFailureKind.UnsupportedSchemaVersion
            ? CheckpointFailureKind.UnsupportedSchemaVersion : CheckpointFailureKind.InvalidSchema;
}
