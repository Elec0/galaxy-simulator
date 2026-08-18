using System.Text;
using GalaxyCommand.Content;

namespace GalaxyCommand.Content.Tests;

public sealed class ContentDocumentJsonTests
{
    private static readonly ContentJsonLimits Limits = ContentJsonLimits.ProductionDefaults;

    [Fact]
    public void DefinitionReaderProducesOnlyFormatNeutralValues()
    {
        byte[] json = Encoding.UTF8.GetBytes(
            """
            {
              "format": "galaxy-command-content-definitions",
              "schemaVersion": 1,
              "definitions": [
                {
                  "kind": "ship-design",
                  "id": "freighter-mk1",
                  "fallback": "Freighter Mk 1",
                  "references": ["galaxy-command.core/material/iron"],
                  "values": {
                    "tags": ["cargo", true, null],
                    "capacity": 1200
                  }
                }
              ]
            }
            """);

        ContentReadResult<ContentDefinitionsSource> result = ContentJsonAdapter.ReadDefinitions(
            json,
            PackageId.Create("galaxy-command.core"),
            Limits,
            "definitions/ships.json");

        Assert.True(result.IsSuccess);
        ContentDefinitionSource definition = Assert.Single(result.Value!.Definitions);
        Assert.Equal("galaxy-command.core/ship-design/freighter-mk1", definition.Key.ToString());
        Assert.Equal("Freighter Mk 1", definition.InvariantFallback);
        Assert.Equal("galaxy-command.core/material/iron", Assert.Single(definition.References).ToString());
        Assert.IsType<ContentNumberValue>(definition.Values.Properties["capacity"]);
        Assert.IsType<ContentArrayValue>(definition.Values.Properties["tags"]);
    }

    [Fact]
    public void ScenarioReaderAndWriterAreStableAcrossPropertyOrder()
    {
        const string firstJson = """
            {"format":"galaxy-command-content-scenario","schemaVersion":1,"id":"minimal","fallback":"Minimal","references":["core/ship-design/scout"],"values":{"z":2,"a":1}}
            """;
        const string secondJson = """
            {"values":{"a":1,"z":2},"references":["core/ship-design/scout"],"fallback":"Minimal","id":"minimal","schemaVersion":1,"format":"galaxy-command-content-scenario"}
            """;

        ContentReadResult<StaticScenarioSource> first = ContentJsonAdapter.ReadScenario(
            Encoding.UTF8.GetBytes(firstJson), PackageId.Create("core"), Limits, "minimal.json");
        ContentReadResult<StaticScenarioSource> second = ContentJsonAdapter.ReadScenario(
            Encoding.UTF8.GetBytes(secondJson), PackageId.Create("core"), Limits, "minimal.json");

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(
            ContentJsonAdapter.WriteScenario(first.Value!, Limits),
            ContentJsonAdapter.WriteScenario(second.Value!, Limits));
        Assert.Equal(["a", "z"], first.Value!.Values.Properties.Keys);
    }

    [Fact]
    public void DefinitionReaderRejectsUnknownEnvelopeProperty()
    {
        byte[] json = Encoding.UTF8.GetBytes(
            """
            {"format":"galaxy-command-content-definitions","schemaVersion":1,"definitions":[],"executable":"plugin.dll"}
            """);

        ContentReadResult<ContentDefinitionsSource> result = ContentJsonAdapter.ReadDefinitions(
            json, PackageId.Create("core"), Limits, "definitions.json");

        ContentDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentDiagnosticKind.UnknownProperty, diagnostic.Kind);
        Assert.Equal("$.executable", diagnostic.Path);
    }

    [Fact]
    public void DefinitionWriterRoundTripsCanonicalNeutralValues()
    {
        ContentDefinitionSource definition = new(
            QualifiedContentKey.Create("core", "material", "iron"),
            "Iron",
            [],
            new ContentObjectValue(
                new Dictionary<string, ContentValue>
                {
                    ["density"] = new ContentNumberValue(7.8m),
                    ["refined"] = new ContentBooleanValue(false),
                }));
        ContentDefinitionsSource source = new([definition]);

        byte[] encoded = ContentJsonAdapter.WriteDefinitions(source, Limits);
        ContentReadResult<ContentDefinitionsSource> decoded = ContentJsonAdapter.ReadDefinitions(
            encoded, PackageId.Create("core"), Limits, "materials.json");

        Assert.True(decoded.IsSuccess);
        Assert.Equal(source, decoded.Value);
    }
}
