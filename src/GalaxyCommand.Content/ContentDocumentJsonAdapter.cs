using System.Text;
using System.Text.Json;

namespace GalaxyCommand.Content;

public static partial class ContentJsonAdapter
{
    private const string DefinitionsFormat = "galaxy-command-content-definitions";
    private const string ScenarioFormat = "galaxy-command-content-scenario";

    /// <summary>
    /// Reads one definitions document and qualifies its local definitions with
    /// the manifest package identity supplied by the production loader.
    /// </summary>
    public static ContentReadResult<ContentDefinitionsSource> ReadDefinitions(
        ReadOnlyMemory<byte> bytes,
        PackageId packageId,
        ContentJsonLimits limits,
        string source)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        return ReadStrictDocument(
            bytes,
            limits,
            source,
            root => DecodeDefinitions(root, packageId, source));
    }

    /// <summary>
    /// Writes definitions through the stable JSON adapter without exposing JSON
    /// values in the neutral model.
    /// </summary>
    public static byte[] WriteDefinitions(ContentDefinitionsSource source, ContentJsonLimits limits)
    {
        ArgumentNullException.ThrowIfNull(source);
        return WriteDocument(
            limits,
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("format", DefinitionsFormat);
                writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
                writer.WriteStartArray("definitions");
                foreach (ContentDefinitionSource definition in source.Definitions)
                {
                    writer.WriteStartObject();
                    writer.WriteString("kind", definition.Key.ContentKind.Value);
                    writer.WriteString("id", definition.Key.LocalId.Value);
                    writer.WriteString("fallback", definition.InvariantFallback);
                    WriteReferences(writer, definition.References);
                    writer.WritePropertyName("values");
                    WriteValue(writer, definition.Values);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            });
    }

    /// <summary>
    /// Reads one static scenario without creating or mutating a game session.
    /// </summary>
    public static ContentReadResult<StaticScenarioSource> ReadScenario(
        ReadOnlyMemory<byte> bytes,
        PackageId packageId,
        ContentJsonLimits limits,
        string source)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        return ReadStrictDocument(
            bytes,
            limits,
            source,
            root => DecodeScenario(root, packageId, source));
    }

    /// <summary>
    /// Writes one static scenario with stable envelope and neutral-object
    /// property ordering.
    /// </summary>
    public static byte[] WriteScenario(StaticScenarioSource source, ContentJsonLimits limits)
    {
        ArgumentNullException.ThrowIfNull(source);
        return WriteDocument(
            limits,
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("format", ScenarioFormat);
                writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
                writer.WriteString("id", source.Id.Value);
                writer.WriteString("fallback", source.InvariantFallback);
                WriteReferences(writer, source.References);
                writer.WritePropertyName("values");
                WriteValue(writer, source.Values);
                writer.WriteEndObject();
            });
    }

    /// <summary>
    /// Applies the same strict UTF-8, JSON, duplicate-property, and resource
    /// checks to every physical document before invoking its schema decoder.
    /// </summary>
    private static ContentReadResult<T> ReadStrictDocument<T>(
        ReadOnlyMemory<byte> bytes,
        ContentJsonLimits limits,
        string source,
        Func<JsonElement, ContentReadResult<T>> decode)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(decode);

        if (bytes.Length > limits.MaximumDocumentBytes)
        {
            return RejectValue<T>(ContentDiagnosticKind.DocumentTooLarge, source, "$", "The document exceeds the configured byte limit.");
        }

        try
        {
            _ = StrictUtf8.GetString(bytes.Span);
        }
        catch (DecoderFallbackException)
        {
            return RejectValue<T>(ContentDiagnosticKind.InvalidUtf8, source, "$", "The document is not valid UTF-8.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
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
            return RejectValue<T>(ContentDiagnosticKind.InvalidJson, source, "$", "The document is not valid strict JSON.");
        }

        using (document)
        {
            ContentDiagnostic? shapeFailure = ValidateJsonShape(document.RootElement, limits, source, "$", 0);
            return shapeFailure is null
                ? decode(document.RootElement)
                : ContentReadResult<T>.Rejected(shapeFailure);
        }
    }

    /// <summary>
    /// Decodes the current definitions envelope and each definition through the
    /// same neutral-value conversion.
    /// </summary>
    private static ContentReadResult<ContentDefinitionsSource> DecodeDefinitions(
        JsonElement root,
        PackageId packageId,
        string source)
    {
        ContentDiagnostic? envelopeFailure = ValidateEnvelope(
            root,
            DefinitionsFormat,
            ["format", "schemaVersion", "definitions"],
            source);
        if (envelopeFailure is not null)
        {
            return ContentReadResult<ContentDefinitionsSource>.Rejected(envelopeFailure);
        }

        if (!TryGetRequired(root, "definitions", JsonValueKind.Array, source, out JsonElement definitions, out ContentDiagnostic? failure))
        {
            return ContentReadResult<ContentDefinitionsSource>.Rejected(failure!);
        }

        List<ContentDefinitionSource> decoded = [];
        int index = 0;
        foreach (JsonElement definition in definitions.EnumerateArray())
        {
            string path = $"$.definitions[{index}]";
            ContentReadResult<ContentDefinitionSource> result = DecodeDefinition(definition, packageId, source, path);
            if (!result.IsSuccess)
            {
                return ContentReadResult<ContentDefinitionsSource>.Rejected(result.Diagnostics[0]);
            }

            decoded.Add(result.Value!);
            index++;
        }

        return ContentReadResult<ContentDefinitionsSource>.Accepted(new ContentDefinitionsSource(decoded));
    }

    /// <summary>
    /// Decodes one definition while preserving only typed identity, references,
    /// fallback text, and format-neutral values.
    /// </summary>
    private static ContentReadResult<ContentDefinitionSource> DecodeDefinition(
        JsonElement element,
        PackageId packageId,
        string source,
        string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return RejectValue<ContentDefinitionSource>(ContentDiagnosticKind.InvalidValue, source, path, "A definition must be an object.");
        }

        ContentDiagnostic? unknown = FindUnknownProperty(element, ["kind", "id", "fallback", "references", "values"], source, path);
        if (unknown is not null)
        {
            return ContentReadResult<ContentDefinitionSource>.Rejected(unknown);
        }

        if (!TryGetRequired(element, "kind", JsonValueKind.String, source, out JsonElement kind, out ContentDiagnostic? failure, path) ||
            !TryGetRequired(element, "id", JsonValueKind.String, source, out JsonElement id, out failure, path) ||
            !TryGetRequired(element, "fallback", JsonValueKind.String, source, out JsonElement fallback, out failure, path) ||
            !TryGetRequired(element, "references", JsonValueKind.Array, source, out JsonElement references, out failure, path) ||
            !TryGetRequired(element, "values", JsonValueKind.Object, source, out JsonElement values, out failure, path))
        {
            return ContentReadResult<ContentDefinitionSource>.Rejected(failure!);
        }

        QualifiedContentKey key;
        try
        {
            key = QualifiedContentKey.Create(packageId.Value, kind.GetString()!, id.GetString()!);
        }
        catch (ArgumentException)
        {
            return RejectValue<ContentDefinitionSource>(ContentDiagnosticKind.InvalidValue, source, path, "The definition identity is invalid.");
        }

        string fallbackText = fallback.GetString()!;
        if (string.IsNullOrWhiteSpace(fallbackText))
        {
            return RejectValue<ContentDefinitionSource>(ContentDiagnosticKind.InvalidValue, source, $"{path}.fallback", "The invariant fallback is empty.");
        }

        ContentReadResult<IReadOnlyList<QualifiedContentKey>> decodedReferences = DecodeReferences(references, source, $"{path}.references");
        ContentReadResult<ContentValue> decodedValues = DecodeValue(values, source, $"{path}.values");
        if (!decodedReferences.IsSuccess)
        {
            return ContentReadResult<ContentDefinitionSource>.Rejected(decodedReferences.Diagnostics[0]);
        }

        if (!decodedValues.IsSuccess)
        {
            return ContentReadResult<ContentDefinitionSource>.Rejected(decodedValues.Diagnostics[0]);
        }

        return ContentReadResult<ContentDefinitionSource>.Accepted(
            new ContentDefinitionSource(
                key,
                fallbackText,
                decodedReferences.Value!,
                (ContentObjectValue)decodedValues.Value!));
    }

    /// <summary>
    /// Decodes the current scenario envelope without interpreting domain fields.
    /// </summary>
    private static ContentReadResult<StaticScenarioSource> DecodeScenario(
        JsonElement root,
        PackageId packageId,
        string source)
    {
        ContentDiagnostic? envelopeFailure = ValidateEnvelope(
            root,
            ScenarioFormat,
            ["format", "schemaVersion", "id", "fallback", "references", "values"],
            source);
        if (envelopeFailure is not null)
        {
            return ContentReadResult<StaticScenarioSource>.Rejected(envelopeFailure);
        }

        if (!TryGetRequired(root, "id", JsonValueKind.String, source, out JsonElement id, out ContentDiagnostic? failure) ||
            !TryGetRequired(root, "fallback", JsonValueKind.String, source, out JsonElement fallback, out failure) ||
            !TryGetRequired(root, "references", JsonValueKind.Array, source, out JsonElement references, out failure) ||
            !TryGetRequired(root, "values", JsonValueKind.Object, source, out JsonElement values, out failure))
        {
            return ContentReadResult<StaticScenarioSource>.Rejected(failure!);
        }

        LocalContentId scenarioId;
        try
        {
            scenarioId = LocalContentId.Create(id.GetString()!);
        }
        catch (ArgumentException)
        {
            return RejectValue<StaticScenarioSource>(ContentDiagnosticKind.InvalidValue, source, "$.id", "The scenario identifier is invalid.");
        }

        string fallbackText = fallback.GetString()!;
        if (string.IsNullOrWhiteSpace(fallbackText))
        {
            return RejectValue<StaticScenarioSource>(ContentDiagnosticKind.InvalidValue, source, "$.fallback", "The invariant fallback is empty.");
        }

        ContentReadResult<IReadOnlyList<QualifiedContentKey>> decodedReferences = DecodeReferences(references, source, "$.references");
        ContentReadResult<ContentValue> decodedValues = DecodeValue(values, source, "$.values");
        if (!decodedReferences.IsSuccess)
        {
            return ContentReadResult<StaticScenarioSource>.Rejected(decodedReferences.Diagnostics[0]);
        }

        if (!decodedValues.IsSuccess)
        {
            return ContentReadResult<StaticScenarioSource>.Rejected(decodedValues.Diagnostics[0]);
        }

        return ContentReadResult<StaticScenarioSource>.Accepted(
            new StaticScenarioSource(
                packageId,
                scenarioId,
                fallbackText,
                decodedReferences.Value!,
                (ContentObjectValue)decodedValues.Value!));
    }

    /// <summary>
    /// Validates the common discriminator and schema version before a document
    /// decoder reads its remaining properties.
    /// </summary>
    private static ContentDiagnostic? ValidateEnvelope(
        JsonElement root,
        string expectedFormat,
        IReadOnlyCollection<string> approvedProperties,
        string source)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Diagnostic(ContentDiagnosticKind.InvalidValue, source, "$", "The content document must be an object.");
        }

        ContentDiagnostic? unknown = FindUnknownProperty(root, approvedProperties, source, "$");
        if (unknown is not null)
        {
            return unknown;
        }

        if (!TryGetRequired(root, "format", JsonValueKind.String, source, out JsonElement format, out ContentDiagnostic? failure) ||
            !TryGetRequired(root, "schemaVersion", JsonValueKind.Number, source, out JsonElement schemaVersion, out failure))
        {
            return failure;
        }

        if (!string.Equals(format.GetString(), expectedFormat, StringComparison.Ordinal))
        {
            return Diagnostic(ContentDiagnosticKind.WrongFormat, source, "$.format", "The document format discriminator is incorrect.");
        }

        return !schemaVersion.TryGetInt32(out int version) || version != CurrentSchemaVersion
            ? Diagnostic(ContentDiagnosticKind.UnsupportedSchemaVersion, source, "$.schemaVersion", "The content schema version is unsupported.")
            : null;
    }

    /// <summary>Finds the first unknown property in source order.</summary>
    private static ContentDiagnostic? FindUnknownProperty(
        JsonElement element,
        IReadOnlyCollection<string> approved,
        string source,
        string path)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!approved.Contains(property.Name, StringComparer.Ordinal))
            {
                return Diagnostic(ContentDiagnosticKind.UnknownProperty, source, $"{path}.{property.Name}", "The property is not part of the current schema.");
            }
        }

        return null;
    }

    /// <summary>Decodes qualified references without resolving them yet.</summary>
    private static ContentReadResult<IReadOnlyList<QualifiedContentKey>> DecodeReferences(
        JsonElement array,
        string source,
        string path)
    {
        List<QualifiedContentKey> references = [];
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                return RejectList<QualifiedContentKey>(ContentDiagnosticKind.InvalidValue, source, $"{path}[{index}]", "A reference must be a qualified-key string.");
            }

            try
            {
                references.Add(QualifiedContentKey.Parse(element.GetString()!));
            }
            catch (ArgumentException)
            {
                return RejectList<QualifiedContentKey>(ContentDiagnosticKind.InvalidValue, source, $"{path}[{index}]", "A qualified reference is invalid.");
            }

            index++;
        }

        return ContentReadResult<IReadOnlyList<QualifiedContentKey>>.Accepted(references.AsReadOnly());
    }

    /// <summary>
    /// Converts the physical JSON tree to the approved format-neutral value
    /// algebra and rejects numbers that cannot be represented exactly.
    /// </summary>
    private static ContentReadResult<ContentValue> DecodeValue(JsonElement element, string source, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return ContentReadResult<ContentValue>.Accepted(ContentNullValue.Instance);
            case JsonValueKind.True:
            case JsonValueKind.False:
                return ContentReadResult<ContentValue>.Accepted(new ContentBooleanValue(element.GetBoolean()));
            case JsonValueKind.String:
                return ContentReadResult<ContentValue>.Accepted(new ContentStringValue(element.GetString()!));
            case JsonValueKind.Number:
                return element.TryGetDecimal(out decimal number)
                    ? ContentReadResult<ContentValue>.Accepted(new ContentNumberValue(number))
                    : RejectValue<ContentValue>(ContentDiagnosticKind.InvalidValue, source, path, "The number is outside the supported exact decimal range.");
            case JsonValueKind.Array:
                List<ContentValue> items = [];
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    ContentReadResult<ContentValue> decodedItem = DecodeValue(item, source, $"{path}[{index}]");
                    if (!decodedItem.IsSuccess)
                    {
                        return decodedItem;
                    }

                    items.Add(decodedItem.Value!);
                    index++;
                }

                return ContentReadResult<ContentValue>.Accepted(new ContentArrayValue(items));
            case JsonValueKind.Object:
                Dictionary<string, ContentValue> properties = new(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    ContentReadResult<ContentValue> decodedProperty = DecodeValue(property.Value, source, $"{path}.{property.Name}");
                    if (!decodedProperty.IsSuccess)
                    {
                        return decodedProperty;
                    }

                    properties.Add(property.Name, decodedProperty.Value!);
                }

                return ContentReadResult<ContentValue>.Accepted(new ContentObjectValue(properties));
            default:
                return RejectValue<ContentValue>(ContentDiagnosticKind.InvalidValue, source, path, "The JSON value kind is unsupported.");
        }
    }

    /// <summary>Writes an already canonical format-neutral value recursively.</summary>
    private static void WriteValue(Utf8JsonWriter writer, ContentValue value)
    {
        switch (value)
        {
            case ContentNullValue:
                writer.WriteNullValue();
                break;
            case ContentBooleanValue boolean:
                writer.WriteBooleanValue(boolean.Value);
                break;
            case ContentNumberValue number:
                writer.WriteNumberValue(number.Value);
                break;
            case ContentStringValue text:
                writer.WriteStringValue(text.Value);
                break;
            case ContentArrayValue array:
                writer.WriteStartArray();
                foreach (ContentValue item in array.Items)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            case ContentObjectValue contentObject:
                writer.WriteStartObject();
                foreach ((string name, ContentValue propertyValue) in contentObject.Properties)
                {
                    writer.WritePropertyName(name);
                    WriteValue(writer, propertyValue);
                }

                writer.WriteEndObject();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.GetType(), "Unknown content value type.");
        }
    }

    private static void WriteReferences(Utf8JsonWriter writer, IEnumerable<QualifiedContentKey> references)
    {
        writer.WriteStartArray("references");
        foreach (QualifiedContentKey reference in references)
        {
            writer.WriteStringValue(reference.ToString());
        }

        writer.WriteEndArray();
    }

    private static byte[] WriteDocument(ContentJsonLimits limits, Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(limits);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            write(writer);
        }

        stream.WriteByte((byte)'\n');
        if (stream.Length > limits.MaximumDocumentBytes)
        {
            throw new InvalidOperationException("The encoded content document exceeds the configured byte limit.");
        }

        return stream.ToArray();
    }

    private static ContentReadResult<T> RejectValue<T>(
        ContentDiagnosticKind kind,
        string source,
        string path,
        string message)
        where T : class => ContentReadResult<T>.Rejected(Diagnostic(kind, source, path, message));
}
