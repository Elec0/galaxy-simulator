using System.Text;
using GalaxyCommand.Content;

namespace GalaxyCommand.Content.Tests;

public sealed class ContentPackageJsonTests
{
    private static readonly ContentJsonLimits DefaultLimits = ContentJsonLimits.ProductionDefaults;

    [Fact]
    public void ReaderAcceptsEditedWhitespaceAndPropertyOrder()
    {
        byte[] json = Encoding.UTF8.GetBytes(
            """
            {
              "documents": [
                { "kind": "definitions", "path": "definitions/materials.json" },
                { "path": "scenarios/minimal.json", "kind": "scenario" }
              ],
              "dependencies": ["galaxy-command.shared"],
              "packageId": "galaxy-command.core",
              "schemaVersion": 1,
              "format": "galaxy-command-content-package"
            }
            """);

        ContentReadResult<ContentPackageSource> result =
            ContentJsonAdapter.ReadPackage(json, DefaultLimits, "package.json");

        Assert.True(result.IsSuccess);
        ContentPackageSource package = Assert.IsType<ContentPackageSource>(result.Value);
        Assert.Equal("galaxy-command.core", package.PackageId.Value);
        Assert.Equal("galaxy-command.shared", Assert.Single(package.Dependencies).Value);
        Assert.Collection(
            package.Documents,
            document =>
            {
                Assert.Equal("definitions/materials.json", document.Path);
                Assert.Equal(ContentDocumentKind.Definitions, document.Kind);
            },
            document =>
            {
                Assert.Equal("scenarios/minimal.json", document.Path);
                Assert.Equal(ContentDocumentKind.Scenario, document.Kind);
            });
    }

    [Theory]
    [InlineData("""{"format":"galaxy-command-content-package","format":"galaxy-command-content-package","schemaVersion":1,"packageId":"core","dependencies":[],"documents":[]}""", ContentDiagnosticKind.DuplicateProperty)]
    [InlineData("""{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"core","dependencies":[],"documents":[],"extra":true}""", ContentDiagnosticKind.UnknownProperty)]
    [InlineData("""{"format":"wrong","schemaVersion":1,"packageId":"core","dependencies":[],"documents":[]}""", ContentDiagnosticKind.WrongFormat)]
    [InlineData("""{"format":"galaxy-command-content-package","schemaVersion":2,"packageId":"core","dependencies":[],"documents":[]}""", ContentDiagnosticKind.UnsupportedSchemaVersion)]
    public void ReaderRejectsInvalidCurrentSchema(string json, ContentDiagnosticKind expectedKind)
    {
        ContentReadResult<ContentPackageSource> result =
            ContentJsonAdapter.ReadPackage(Encoding.UTF8.GetBytes(json), DefaultLimits, "package.json");

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedKind, Assert.Single(result.Diagnostics).Kind);
    }

    [Fact]
    public void ReaderRejectsInvalidUtf8()
    {
        byte[] invalidUtf8 = [0x7b, 0x22, 0x66, 0x6f, 0x72, 0x6d, 0x61, 0x74, 0x22, 0x3a, 0x22, 0xc3, 0x28];

        ContentReadResult<ContentPackageSource> result =
            ContentJsonAdapter.ReadPackage(invalidUtf8, DefaultLimits, "package.json");

        Assert.Equal(ContentDiagnosticKind.InvalidUtf8, Assert.Single(result.Diagnostics).Kind);
    }

    [Fact]
    public void ReaderAppliesConfiguredDocumentLimitBeforeParsing()
    {
        ContentJsonLimits limits = new(
            maximumDocumentBytes: 1,
            maximumDepth: 32,
            maximumStringLength: 4_096,
            maximumContainerEntries: 4_096,
            maximumDocumentsPerPackage: 256,
            maximumDependenciesPerPackage: 128,
            maximumDiagnostics: 1_024);

        ContentReadResult<ContentPackageSource> result =
            ContentJsonAdapter.ReadPackage("{}"u8.ToArray(), limits, "package.json");

        Assert.Equal(ContentDiagnosticKind.DocumentTooLarge, Assert.Single(result.Diagnostics).Kind);
    }

    [Fact]
    public void WriterEmitsStableReadableJsonThatRoundTrips()
    {
        ContentPackageSource package = new(
            PackageId.Create("galaxy-command.core"),
            [PackageId.Create("galaxy-command.shared")],
            [new ContentDocumentDeclaration("definitions/materials.json", ContentDocumentKind.Definitions)]);

        byte[] first = ContentJsonAdapter.WritePackage(package, DefaultLimits);
        byte[] second = ContentJsonAdapter.WritePackage(package, DefaultLimits);
        ContentReadResult<ContentPackageSource> read =
            ContentJsonAdapter.ReadPackage(first, DefaultLimits, "package.json");

        Assert.Equal(first, second);
        Assert.EndsWith("\n", Encoding.UTF8.GetString(first), StringComparison.Ordinal);
        Assert.True(read.IsSuccess);
        Assert.Equal(package, read.Value);
    }
}
