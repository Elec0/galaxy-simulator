using System.Text;
using System.Text.Json;

namespace GalaxyCommand.Content;

/// <summary>
/// Reads and writes the initial strict JSON physical format without exposing
/// JSON nodes through the format-neutral content model.
/// </summary>
public static partial class ContentJsonAdapter
{
    private const string PackageFormat = "galaxy-command-content-package";
    private const int CurrentSchemaVersion = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Reads one complete package manifest under the supplied untrusted-input
    /// limits. A failure returns no partial package model.
    /// </summary>
    public static ContentReadResult<ContentPackageSource> ReadPackage(
        ReadOnlyMemory<byte> bytes,
        ContentJsonLimits limits,
        string source)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (bytes.Length > limits.MaximumDocumentBytes)
        {
            return Reject(ContentDiagnosticKind.DocumentTooLarge, source, "$", "The document exceeds the configured byte limit.");
        }

        try
        {
            _ = StrictUtf8.GetString(bytes.Span);
        }
        catch (DecoderFallbackException)
        {
            return Reject(ContentDiagnosticKind.InvalidUtf8, source, "$", "The document is not valid UTF-8.");
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
            return Reject(ContentDiagnosticKind.InvalidJson, source, "$", "The document is not valid strict JSON.");
        }

        using (document)
        {
            ContentDiagnostic? shapeFailure = ValidateJsonShape(document.RootElement, limits, source, "$", 0);
            if (shapeFailure is not null)
            {
                return ContentReadResult<ContentPackageSource>.Rejected(shapeFailure);
            }

            return DecodePackage(document.RootElement, limits, source);
        }
    }

    /// <summary>
    /// Writes one package manifest with stable property and collection order.
    /// The emitted bytes must remain within the supplied document limit.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the encoded manifest exceeds the configured byte limit.
    /// </exception>
    public static byte[] WritePackage(ContentPackageSource package, ContentJsonLimits limits)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(limits);

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(
            stream,
            new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", PackageFormat);
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("packageId", package.PackageId.Value);
            writer.WriteStartArray("dependencies");
            foreach (PackageId dependency in package.Dependencies)
            {
                writer.WriteStringValue(dependency.Value);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("documents");
            foreach (ContentDocumentDeclaration declaration in package.Documents)
            {
                writer.WriteStartObject();
                writer.WriteString("path", declaration.Path);
                writer.WriteString("kind", FormatDocumentKind(declaration.Kind));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        if (stream.Length > limits.MaximumDocumentBytes)
        {
            throw new InvalidOperationException("The encoded package manifest exceeds the configured byte limit.");
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Validates duplicate properties and configured collection and string
    /// limits before schema decoding can allocate neutral-model collections.
    /// </summary>
    private static ContentDiagnostic? ValidateJsonShape(
        JsonElement element,
        ContentJsonLimits limits,
        string source,
        string path,
        int depth)
    {
        if (depth > limits.MaximumDepth)
        {
            return Diagnostic(ContentDiagnosticKind.LimitExceeded, source, path, "The document exceeds the configured depth limit.");
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            int count = 0;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                count++;
                if (count > limits.MaximumContainerEntries)
                {
                    return Diagnostic(ContentDiagnosticKind.LimitExceeded, source, path, "An object exceeds the configured entry limit.");
                }

                string propertyPath = $"{path}.{property.Name}";
                if (property.Name.Length > limits.MaximumStringLength)
                {
                    return Diagnostic(ContentDiagnosticKind.LimitExceeded, source, propertyPath, "A property name exceeds the configured string limit.");
                }

                if (!names.Add(property.Name))
                {
                    return Diagnostic(ContentDiagnosticKind.DuplicateProperty, source, propertyPath, "A property is declared more than once.");
                }

                ContentDiagnostic? childFailure = ValidateJsonShape(property.Value, limits, source, propertyPath, depth + 1);
                if (childFailure is not null)
                {
                    return childFailure;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (index >= limits.MaximumContainerEntries)
                {
                    return Diagnostic(ContentDiagnosticKind.LimitExceeded, source, path, "An array exceeds the configured entry limit.");
                }

                ContentDiagnostic? childFailure = ValidateJsonShape(item, limits, source, $"{path}[{index}]", depth + 1);
                if (childFailure is not null)
                {
                    return childFailure;
                }

                index++;
            }
        }
        else if (element.ValueKind == JsonValueKind.String &&
                 element.GetString()!.Length > limits.MaximumStringLength)
        {
            return Diagnostic(ContentDiagnosticKind.LimitExceeded, source, path, "A string exceeds the configured length limit.");
        }

        return null;
    }

    /// <summary>
    /// Decodes only the approved current manifest schema and returns the first
    /// deterministic schema diagnostic on failure.
    /// </summary>
    private static ContentReadResult<ContentPackageSource> DecodePackage(
        JsonElement root,
        ContentJsonLimits limits,
        string source)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Reject(ContentDiagnosticKind.InvalidValue, source, "$", "The package manifest must be an object.");
        }

        string[] approvedProperties = ["format", "schemaVersion", "packageId", "dependencies", "documents"];
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!approvedProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                return Reject(ContentDiagnosticKind.UnknownProperty, source, $"$.{property.Name}", "The property is not part of the current package schema.");
            }
        }

        if (!TryGetRequired(root, "format", JsonValueKind.String, source, out JsonElement format, out ContentDiagnostic? failure) ||
            !TryGetRequired(root, "schemaVersion", JsonValueKind.Number, source, out JsonElement schemaVersion, out failure) ||
            !TryGetRequired(root, "packageId", JsonValueKind.String, source, out JsonElement packageIdElement, out failure) ||
            !TryGetRequired(root, "dependencies", JsonValueKind.Array, source, out JsonElement dependenciesElement, out failure) ||
            !TryGetRequired(root, "documents", JsonValueKind.Array, source, out JsonElement documentsElement, out failure))
        {
            return ContentReadResult<ContentPackageSource>.Rejected(failure!);
        }

        if (!string.Equals(format.GetString(), PackageFormat, StringComparison.Ordinal))
        {
            return Reject(ContentDiagnosticKind.WrongFormat, source, "$.format", "The document format discriminator is not a content package.");
        }

        if (!schemaVersion.TryGetInt32(out int version) || version != CurrentSchemaVersion)
        {
            return Reject(ContentDiagnosticKind.UnsupportedSchemaVersion, source, "$.schemaVersion", "The package schema version is unsupported.");
        }

        PackageId packageId;
        try
        {
            packageId = PackageId.Create(packageIdElement.GetString()!);
        }
        catch (ArgumentException)
        {
            return Reject(ContentDiagnosticKind.InvalidValue, source, "$.packageId", "The package identifier is invalid.");
        }

        ContentReadResult<IReadOnlyList<PackageId>> dependencies =
            DecodeDependencies(dependenciesElement, limits, source);
        if (!dependencies.IsSuccess)
        {
            return ContentReadResult<ContentPackageSource>.Rejected(dependencies.Diagnostics[0]);
        }

        ContentReadResult<IReadOnlyList<ContentDocumentDeclaration>> documents =
            DecodeDocuments(documentsElement, limits, source);
        if (!documents.IsSuccess)
        {
            return ContentReadResult<ContentPackageSource>.Rejected(documents.Diagnostics[0]);
        }

        return ContentReadResult<ContentPackageSource>.Accepted(
            new ContentPackageSource(packageId, dependencies.Value!, documents.Value!));
    }

    /// <summary>
    /// Decodes dependency identities while enforcing the package-level limit.
    /// </summary>
    private static ContentReadResult<IReadOnlyList<PackageId>> DecodeDependencies(
        JsonElement array,
        ContentJsonLimits limits,
        string source)
    {
        if (array.GetArrayLength() > limits.MaximumDependenciesPerPackage)
        {
            return RejectList<PackageId>(ContentDiagnosticKind.LimitExceeded, source, "$.dependencies", "The dependency list exceeds the configured limit.");
        }

        List<PackageId> dependencies = [];
        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return RejectList<PackageId>(ContentDiagnosticKind.InvalidValue, source, $"$.dependencies[{index}]", "A dependency must be a package identifier string.");
            }

            try
            {
                dependencies.Add(PackageId.Create(item.GetString()!));
            }
            catch (ArgumentException)
            {
                return RejectList<PackageId>(ContentDiagnosticKind.InvalidValue, source, $"$.dependencies[{index}]", "A dependency package identifier is invalid.");
            }

            index++;
        }

        return ContentReadResult<IReadOnlyList<PackageId>>.Accepted(dependencies.AsReadOnly());
    }

    /// <summary>
    /// Decodes explicit document declarations without filesystem discovery.
    /// </summary>
    private static ContentReadResult<IReadOnlyList<ContentDocumentDeclaration>> DecodeDocuments(
        JsonElement array,
        ContentJsonLimits limits,
        string source)
    {
        if (array.GetArrayLength() > limits.MaximumDocumentsPerPackage)
        {
            return RejectList<ContentDocumentDeclaration>(ContentDiagnosticKind.LimitExceeded, source, "$.documents", "The document list exceeds the configured limit.");
        }

        List<ContentDocumentDeclaration> documents = [];
        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            string itemPath = $"$.documents[{index}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                return RejectList<ContentDocumentDeclaration>(ContentDiagnosticKind.InvalidValue, source, itemPath, "A document declaration must be an object.");
            }

            foreach (JsonProperty property in item.EnumerateObject())
            {
                if (property.Name is not ("path" or "kind"))
                {
                    return RejectList<ContentDocumentDeclaration>(ContentDiagnosticKind.UnknownProperty, source, $"{itemPath}.{property.Name}", "The property is not part of a document declaration.");
                }
            }

            if (!TryGetRequired(item, "path", JsonValueKind.String, source, out JsonElement path, out ContentDiagnostic? failure, itemPath) ||
                !TryGetRequired(item, "kind", JsonValueKind.String, source, out JsonElement kind, out failure, itemPath))
            {
                return ContentReadResult<IReadOnlyList<ContentDocumentDeclaration>>.Rejected(failure!);
            }

            if (!TryParseDocumentKind(kind.GetString()!, out ContentDocumentKind documentKind))
            {
                return RejectList<ContentDocumentDeclaration>(ContentDiagnosticKind.InvalidValue, source, $"{itemPath}.kind", "The document kind is unsupported.");
            }

            string documentPath = path.GetString()!;
            if (string.IsNullOrWhiteSpace(documentPath))
            {
                return RejectList<ContentDocumentDeclaration>(ContentDiagnosticKind.InvalidValue, source, $"{itemPath}.path", "The document path is empty.");
            }

            documents.Add(new ContentDocumentDeclaration(documentPath, documentKind));
            index++;
        }

        return ContentReadResult<IReadOnlyList<ContentDocumentDeclaration>>.Accepted(documents.AsReadOnly());
    }

    /// <summary>
    /// Reads one required property with an exact current-schema JSON kind.
    /// </summary>
    private static bool TryGetRequired(
        JsonElement parent,
        string propertyName,
        JsonValueKind expectedKind,
        string source,
        out JsonElement value,
        out ContentDiagnostic? failure,
        string parentPath = "$")
    {
        if (!parent.TryGetProperty(propertyName, out value))
        {
            failure = Diagnostic(ContentDiagnosticKind.MissingProperty, source, $"{parentPath}.{propertyName}", "A required property is missing.");
            return false;
        }

        if (value.ValueKind != expectedKind)
        {
            failure = Diagnostic(ContentDiagnosticKind.InvalidValue, source, $"{parentPath}.{propertyName}", "A property has the wrong JSON value kind.");
            return false;
        }

        failure = null;
        return true;
    }

    private static string FormatDocumentKind(ContentDocumentKind kind) => kind switch
    {
        ContentDocumentKind.Definitions => "definitions",
        ContentDocumentKind.Scenario => "scenario",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown content document kind."),
    };

    private static bool TryParseDocumentKind(string value, out ContentDocumentKind kind)
    {
        switch (value)
        {
            case "definitions":
                kind = ContentDocumentKind.Definitions;
                return true;
            case "scenario":
                kind = ContentDocumentKind.Scenario;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static ContentDiagnostic Diagnostic(
        ContentDiagnosticKind kind,
        string source,
        string path,
        string message) => new(kind, source, path, message);

    private static ContentReadResult<ContentPackageSource> Reject(
        ContentDiagnosticKind kind,
        string source,
        string path,
        string message) => ContentReadResult<ContentPackageSource>.Rejected(Diagnostic(kind, source, path, message));

    private static ContentReadResult<IReadOnlyList<T>> RejectList<T>(
        ContentDiagnosticKind kind,
        string source,
        string path,
        string message) => ContentReadResult<IReadOnlyList<T>>.Rejected(Diagnostic(kind, source, path, message));
}
