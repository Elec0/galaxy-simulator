using System.Text;
using System.Text.Json.Nodes;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class DialogueContentTests
{
    [Fact]
    public void LoadsEveryApprovedDeclarativeKindAndCanonicalCatalog()
    {
        JsonObject source = Definition();
        var second = (JsonObject)source.DeepClone();
        second["id"] = "a-greeting";
        DialogueContentLoadResult result = DialogueContentLoader.Load(Catalog(source, second));
        Assert.True(result.IsSuccess, string.Join("; ", result.Diagnostics));
        Assert.Equal("a-greeting", result.Catalog!.Definitions[0].Key.LocalId.Value);
        DialogueDefinition definition = result.Catalog.Get(QualifiedContentKey.Create("test.dialogue", "dialogue", "greeting"));
        Assert.Equal(4, definition.Roles.Count);
        Assert.Equal(2, definition.Memories.Count);
        DialogueChoice choice = Assert.Single(definition.Nodes.Single(node => node.Id == "entry").Choices);
        Assert.Equal(4, choice.AdmissionConditions.Count);
        Assert.IsType<DialogueCondition.Receipt>(choice.OfferConditions[0]);
        Assert.NotNull(choice.Move);
        Assert.Equal(OrderPlacement.Append, choice.Move.Placement);
        DialogueText text = definition.Nodes.Single(node => node.Id == "entry").Segments[0].Text;
        Assert.Equal(4, text.Arguments.Count);
        Assert.Equal(2, text.PluralSelector);
        Assert.Equal("Captain", definition.Nodes.Single(node => node.Id == "entry").Segments[0].Speaker!.InvariantFallback);
        Assert.True(result.Catalog.TryGet(definition.Key, out DialogueDefinition? found));
        Assert.Same(definition, found);
        Assert.False(result.Catalog.TryGet(QualifiedContentKey.Create("test.dialogue", "dialogue", "absent"), out _));
    }

    [Theory]
    [InlineData("unknown-field")]
    [InlineData("missing-repeatability")]
    [InlineData("wrong-type")]
    [InlineData("unknown-condition")]
    [InlineData("unknown-command")]
    [InlineData("unknown-argument")]
    [InlineData("fractional-order")]
    [InlineData("negative-count")]
    [InlineData("argument-overflow")]
    [InlineData("dangling-transition")]
    [InlineData("scope-mismatch")]
    [InlineData("implicit-transition")]
    [InlineData("wrong-node-behavior")]
    [InlineData("invalid-markup")]
    [InlineData("discovered-ship")]
    public void RejectsMalformedDefinitionsWithoutPublishingPartialCatalog(string invalid)
    {
        JsonObject source = Definition();
        JsonObject values = source["values"]!.AsObject();
        JsonObject entry = values["nodes"]![0]!.AsObject();
        JsonObject choice = entry["choices"]![0]!.AsObject();
        JsonObject text = entry["segments"]![0]!["text"]!.AsObject();
        switch (invalid)
        {
            case "unknown-field": values["script"] = "execute"; break;
            case "missing-repeatability": values.Remove("repeatability"); break;
            case "wrong-type": values["roles"] = "ship"; break;
            case "unknown-condition": choice["admissionConditions"]![0]!["kind"] = "callback"; break;
            case "unknown-command": choice["move"]!["kind"] = "bundle"; break;
            case "unknown-argument": text["arguments"]![0]!["kind"] = "object"; break;
            case "fractional-order": choice["order"] = 0.5; break;
            case "negative-count": text["arguments"]![2]!["value"] = -1; break;
            case "argument-overflow": text["arguments"]![2]!["value"] = decimal.MaxValue; break;
            case "dangling-transition": choice["transition"]!["nodeId"] = "absent"; break;
            case "scope-mismatch": choice["offerConditions"]![0]!["scope"] = "participant-binding"; break;
            case "implicit-transition": values["nodes"]![1]!.AsObject().Remove("automaticTransition"); break;
            case "wrong-node-behavior": entry["automaticTransition"] = JsonNode.Parse("""{"kind":"complete","outcomeId":"done"}"""); break;
            case "invalid-markup":
                text["markup"] = "restricted-rich-text";
                text["invariantFallback"] = "[url]{name} {amount} {count} {duration}[/url]";
                break;
            case "discovered-ship": values["roles"]![1]!["acceptedKinds"] = new JsonArray("ship"); break;
        }
        DialogueContentLoadResult result = DialogueContentLoader.Load(Catalog(source));
        Assert.False(result.IsSuccess);
        Assert.Null(result.Catalog);
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.Equal("test.dialogue/dialogue/greeting", diagnostic.Source);
            Assert.StartsWith("$definition.values", diagnostic.Path, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CatalogCopiesDefinitionsAndRejectsDuplicateKeys()
    {
        DialogueDefinition definition = Assert.Single(DialogueContentLoader.Load(Catalog(Definition())).Catalog!.Definitions);
        var source = new List<DialogueDefinition> { definition };
        var catalog = new DialogueDefinitionCatalog(source);
        source.Clear();
        Assert.Same(definition, Assert.Single(catalog.Definitions));
        Assert.Throws<ArgumentException>(() => new DialogueDefinitionCatalog([definition, definition]));
        Assert.Throws<KeyNotFoundException>(() => catalog.Get(QualifiedContentKey.Create("test.dialogue", "dialogue", "missing")));
    }

    [Fact]
    public void ProductionLoadCarriesCatalogAndInvalidUnreferencedDialogueRejectsSession()
    {
        using var package = new Package();
        JsonObject source = Definition();
        package.Add(source);
        StaticNewGameLoadResult loaded = package.Load(1);
        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Diagnostics));
        Assert.Single(loaded.Setup!.DialogueDefinitions.Definitions);
        StaticNewGameLoadResult concurrent = package.Load(3);
        Assert.True(concurrent.IsSuccess);
        Assert.Equal(loaded.Content!.CatalogFingerprint, concurrent.Content!.CatalogFingerprint);
        Assert.Equal(loaded.Setup.DialogueDefinitions.Definitions.Select(value => value.Key),
            concurrent.Setup!.DialogueDefinitions.Definitions.Select(value => value.Key));
        source["values"]!["initialNodeId"] = "missing";
        package.Add(source);
        StaticNewGameLoadResult rejected = package.Load(2);
        Assert.False(rejected.IsSuccess);
        Assert.Null(rejected.Setup);
        Assert.Null(rejected.Content);
    }

    private static ResolvedContentCatalog Catalog(params JsonObject[] definitions)
    {
        var document = new JsonObject { ["format"] = "galaxy-command-content-definitions", ["schemaVersion"] = 5,
            ["definitions"] = new JsonArray(definitions.Select(value => value.DeepClone()).ToArray()) };
        ContentReadResult<ContentDefinitionsSource> decoded = ContentJsonAdapter.ReadDefinitions(
            Encoding.UTF8.GetBytes(document.ToJsonString()), PackageId.Create("test.dialogue"), ContentJsonLimits.ProductionDefaults, "fixture");
        Assert.True(decoded.IsSuccess, string.Join("; ", decoded.Diagnostics));
        return new(decoded.Value!.Definitions);
    }

    private static JsonObject Definition() => JsonNode.Parse("""
        {"kind":"dialogue","id":"greeting","fallback":"Greeting","references":[],"values":{
          "initialNodeId":"entry","repeatability":"once-per-participant-binding","startConditions":[],
          "roles":[{"id":"actor","acceptedKinds":["ship"]},{"id":"respondent","acceptedKinds":["station"]},
                   {"id":"player","acceptedKinds":["principal"]},{"id":"organization","acceptedKinds":["principal"]}],
          "memories":[{"id":"heard","scope":"session"},{"id":"met","scope":"participant-binding"}],
          "nodes":[{"id":"entry","segments":[{"id":"line","order":0,"text":{
            "resourceKey":"dialogue.greeting","invariantFallback":"{name} {amount} {count} {duration}","markup":"plain-text",
            "arguments":[{"kind":"text","name":"name","value":"Pilot"},{"kind":"number","name":"amount","value":1.5},
              {"kind":"count","name":"count","value":2},{"kind":"duration","name":"duration","value":1000}],"pluralSelector":2},
            "speaker":{"resourceKey":"dialogue.captain","invariantFallback":"Captain","markup":"plain-text","arguments":[]}}],
            "choices":[{"id":"accept","order":0,"label":{"resourceKey":"dialogue.accept","invariantFallback":"Accept","markup":"plain-text","arguments":[]},
              "transition":{"kind":"node","nodeId":"notice"},"repeatability":"once-per-session",
              "offerConditions":[{"kind":"receipt","memoryId":"heard","scope":"session","exists":false}],
              "admissionConditions":[{"kind":"standing","assessingRoleId":"player","subjectRoleId":"organization","minimumValue":0},
                {"kind":"detected","roleId":"respondent"},{"kind":"discovered","roleId":"respondent"},
                {"kind":"receipt","memoryId":"met","scope":"participant-binding","exists":false}],
              "memoryIds":["heard","met"],"move":{"kind":"move-ship","actorRoleId":"actor","destinationRoleId":"respondent","placement":"append"}}]},
            {"id":"notice","segments":[],"choices":[],"automaticTransition":{"kind":"complete","outcomeId":"done"}}]}}
        """)!.AsObject();

    private sealed class Package : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"galaxy-dialogue-content-{Guid.NewGuid():N}");
        internal Package()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "GalaxyCommand.slnx"))) repository = repository.Parent;
            string source = Path.Combine(repository!.FullName, "content", "built-in", "galaxy-command.core");
            Directory.CreateDirectory(_root);
            foreach (string file in new[] { "package.json", "definitions.json", "minimal.json" })
                File.Copy(Path.Combine(source, file), Path.Combine(_root, file));
        }
        internal void Add(JsonObject definition)
        {
            string path = Path.Combine(_root, "definitions.json");
            JsonObject document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            JsonArray definitions = document["definitions"]!.AsArray();
            JsonNode? previous = definitions.FirstOrDefault(value => value!["kind"]!.GetValue<string>() == "dialogue");
            if (previous is not null) definitions.Remove(previous);
            definitions.Add(definition.DeepClone());
            File.WriteAllText(path, document.ToJsonString());
        }
        internal StaticNewGameLoadResult Load(int workers) => StaticNewGameLoader.Load([_root], PackageId.Create("galaxy-command.core"),
            LocalContentId.Create("minimal"), GameSessionTestFixture.RootSeed, 64, workers);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
