using System.Text;
using GalaxyCommand.Content;

namespace GalaxyCommand.Content.Tests;

public sealed class ContentPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"galaxy-command-content-{Guid.NewGuid():N}");

    [Fact]
    public void ProductionPathIsInvariantAcrossInputOrderAndWorkerCount()
    {
        string shared = WritePackage(
            "shared",
            """{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"shared","dependencies":[],"documents":[{"path":"materials.json","kind":"definitions"}]}""",
            ("materials.json", Definitions(("material", "iron", "Iron", "[]"))));
        string core = WritePackage(
            "core",
            """{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"core","dependencies":["shared"],"documents":[{"path":"ships.json","kind":"definitions"},{"path":"minimal.json","kind":"scenario"}]}""",
            ("ships.json", Definitions(("ship-design", "scout", "Scout", "[\"shared/material/iron\"]"))),
            ("minimal.json", Scenario("minimal", "core/ship-design/scout")));
        ContentKindRegistry registry = new([ContentKind.Create("material"), ContentKind.Create("ship-design")]);

        ContentValidationResult singleWorker = ContentPipeline.Validate(
            [core, shared],
            new ContentValidationOptions(ContentJsonLimits.ProductionDefaults, registry, maximumDegreeOfParallelism: 1));
        ContentValidationResult fourWorkers = ContentPipeline.Validate(
            [shared, core],
            new ContentValidationOptions(ContentJsonLimits.ProductionDefaults, registry, maximumDegreeOfParallelism: 4));

        Assert.True(singleWorker.IsSuccess);
        Assert.True(fourWorkers.IsSuccess);
        Assert.Equal(["shared", "core"], singleWorker.Content!.PackageOrder.Select(id => id.Value));
        Assert.Equal(
            ["core/ship-design/scout", "shared/material/iron"],
            singleWorker.Content.Catalog.Definitions.Keys.Select(key => key.ToString()));
        Assert.Equal(singleWorker.Content.CatalogFingerprint, fourWorkers.Content!.CatalogFingerprint);
        Assert.Equal(singleWorker.Content.Catalog, fourWorkers.Content.Catalog);
    }

    [Fact]
    public void ProductionPathReportsStableDependencyCollisionAndReferenceFailures()
    {
        string first = WritePackage(
            "first",
            """{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"core","dependencies":["missing"],"documents":[{"path":"one.json","kind":"definitions"}]}""",
            ("one.json", Definitions(("material", "iron", "Iron", "[\"core/material/copper\"]"))));
        string second = WritePackage(
            "second",
            """{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"core","dependencies":[],"documents":[]}""");
        ContentKindRegistry registry = new([ContentKind.Create("material")]);

        ContentValidationResult result = ContentPipeline.Validate(
            [second, first],
            new ContentValidationOptions(ContentJsonLimits.ProductionDefaults, registry, 4));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Content);
        Assert.Equal(
            [ContentDiagnosticKind.MissingDependency, ContentDiagnosticKind.IdentityCollision],
            result.Diagnostics.Select(diagnostic => diagnostic.Kind).Order());
    }

    [Fact]
    public void ProductionPathRejectsUnsupportedKindsAndEscapingDocumentPaths()
    {
        string package = WritePackage(
            "core",
            """{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"core","dependencies":[],"documents":[{"path":"definitions.json","kind":"definitions"},{"path":"../outside.json","kind":"definitions"}]}""",
            ("definitions.json", Definitions(("executable", "plugin", "Plugin", "[]"))));
        ContentKindRegistry registry = new([]);

        ContentValidationResult result = ContentPipeline.Validate(
            [package],
            new ContentValidationOptions(ContentJsonLimits.ProductionDefaults, registry, 2));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Kind == ContentDiagnosticKind.UnsupportedContentKind);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Kind == ContentDiagnosticKind.InvalidValue);
    }

    [Fact]
    public void ProductionPathRejectsCyclesDuplicateDefinitionsAndUnresolvedReferences()
    {
        string first = WritePackage(
            "first",
            """{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"first","dependencies":["second"],"documents":[{"path":"one.json","kind":"definitions"},{"path":"two.json","kind":"definitions"}]}""",
            ("one.json", Definitions(("material", "iron", "Iron", "[\"first/material/missing\"]"))),
            ("two.json", Definitions(("material", "iron", "Iron again", "[]"))));
        string second = WritePackage(
            "second",
            """{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"second","dependencies":["first"],"documents":[]}""");
        ContentKindRegistry registry = new([ContentKind.Create("material")]);

        ContentValidationResult cycle = ContentPipeline.Validate(
            [first, second],
            new ContentValidationOptions(ContentJsonLimits.ProductionDefaults, registry, 2));

        Assert.Equal(ContentDiagnosticKind.DependencyCycle, Assert.Single(cycle.Diagnostics).Kind);

        File.WriteAllText(
            Path.Combine(second, "package.json"),
            """{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"second","dependencies":[],"documents":[]}""",
            new UTF8Encoding(false));
        ContentValidationResult contentFailures = ContentPipeline.Validate(
            [first, second],
            new ContentValidationOptions(ContentJsonLimits.ProductionDefaults, registry, 2));

        Assert.Contains(contentFailures.Diagnostics, diagnostic => diagnostic.Kind == ContentDiagnosticKind.IdentityCollision);
        Assert.Contains(contentFailures.Diagnostics, diagnostic => diagnostic.Kind == ContentDiagnosticKind.UnresolvedReference);
    }

    [Fact]
    public void FingerprintsIgnoreDeclaredDocumentAndDefinitionOrder()
    {
        string package = WritePackage(
            "core",
            """{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"core","dependencies":[],"documents":[{"path":"one.json","kind":"definitions"},{"path":"two.json","kind":"definitions"}]}""",
            ("one.json", Definitions(("material", "iron", "Iron", "[]"), ("material", "copper", "Copper", "[]"))),
            ("two.json", Definitions(("material", "water", "Water", "[]"))));
        ContentKindRegistry registry = new([ContentKind.Create("material")]);
        ContentValidationOptions options = new(ContentJsonLimits.ProductionDefaults, registry, 3);
        ContentValidationResult first = ContentPipeline.Validate([package], options);

        File.WriteAllText(
            Path.Combine(package, "package.json"),
            """{"documents":[{"kind":"definitions","path":"two.json"},{"kind":"definitions","path":"one.json"}],"dependencies":[],"packageId":"core","schemaVersion":1,"format":"galaxy-command-content-package"}""",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(package, "one.json"),
            Definitions(("material", "copper", "Copper", "[]"), ("material", "iron", "Iron", "[]")),
            new UTF8Encoding(false));
        ContentValidationResult reordered = ContentPipeline.Validate([package], options);

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Content!.CatalogFingerprint, reordered.Content!.CatalogFingerprint);
        Assert.Equal(first.Content.PackageFingerprints, reordered.Content.PackageFingerprints);
    }

    [Fact]
    public void DuplicateScenarioDiagnosticsAreInvariantAcrossWorkerCounts()
    {
        string package = WritePackage(
            "core",
            """{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"core","dependencies":[],"documents":[{"path":"first.json","kind":"scenario"},{"path":"second.json","kind":"scenario"}]}""",
            ("first.json", Scenario("minimal", "core/material/iron")),
            ("second.json", Scenario("minimal", "core/material/iron")));
        ContentKindRegistry registry = new([]);

        ContentValidationResult singleWorker = ContentPipeline.Validate(
            [package],
            new ContentValidationOptions(ContentJsonLimits.ProductionDefaults, registry, 1));
        ContentValidationResult fourWorkers = ContentPipeline.Validate(
            [package],
            new ContentValidationOptions(ContentJsonLimits.ProductionDefaults, registry, 4));

        Assert.Contains(singleWorker.Diagnostics, diagnostic => diagnostic.Kind == ContentDiagnosticKind.IdentityCollision);
        Assert.Equal(singleWorker.Diagnostics, fourWorkers.Diagnostics);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WritePackage(
        string directoryName,
        string manifest,
        params (string Path, string Contents)[] documents)
    {
        string directory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "package.json"), manifest, new UTF8Encoding(false));
        foreach ((string path, string contents) in documents)
        {
            string file = Path.Combine(directory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, contents, new UTF8Encoding(false));
        }

        return directory;
    }

    private static string Definitions(params (string Kind, string Id, string Fallback, string References)[] definitions)
    {
        string entries = string.Join(
            ',',
            definitions.Select(definition =>
                $"{{\"kind\":\"{definition.Kind}\",\"id\":\"{definition.Id}\",\"fallback\":\"{definition.Fallback}\",\"references\":{definition.References},\"values\":{{}}}}"));
        return $"{{\"format\":\"galaxy-command-content-definitions\",\"schemaVersion\":1,\"definitions\":[{entries}]}}";
    }

    private static string Scenario(string id, string reference) =>
        $"{{\"format\":\"galaxy-command-content-scenario\",\"schemaVersion\":1,\"id\":\"{id}\",\"fallback\":\"Minimal\",\"references\":[\"{reference}\"],\"values\":{{}}}}";
}
