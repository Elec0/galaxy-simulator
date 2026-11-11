using System.Text.Json;
using System.Text.Json.Nodes;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionSaveSchemaTests
{
    private static readonly SaveJsonLimits Limits = new(4_194_304, 128, 16_384, 100_000);
    private static readonly GameSessionSaveMetadata Metadata = new(
        Guid.Parse("2b080570-673a-4f85-a0ac-18730f43c612"),
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), "schema-test");

    [Theory]
    [InlineData("checkpoint", "$.checkpoint")]
    [InlineData("compatibility", "$.compatibility")]
    [InlineData("saveId", "$.saveId")]
    [InlineData("producer", "$.producer")]
    public void MissingEnvelopeMemberRejectsBeforeSessionPublication(string member, string path)
    {
        using GameSession original = Create();
        JsonObject root = Root(original);
        root.Remove(member);
        CheckpointResult<GameSession> result = Restore(root);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(path, result.Failure!.Path);
    }

    [Theory]
    [InlineData("engine", "$.checkpoint.engine")]
    [InlineData("cargo", "$.checkpoint.cargo")]
    [InlineData("stations", "$.checkpoint.stations")]
    [InlineData("random", "$.checkpoint.random")]
    public void MissingOwnerNeverUsesConstructorDefaults(string member, string path)
    {
        using GameSession original = Create();
        JsonObject root = Root(original);
        root["checkpoint"]!.AsObject().Remove(member);
        Assert.Equal(path, Restore(root).Failure!.Path);
    }

    [Theory]
    [InlineData("checkpoint")]
    [InlineData("compatibility")]
    public void NullRequiredRootSectionsReturnTypedFailure(string member)
    {
        using GameSession original = Create();
        JsonObject root = Root(original);
        root[member] = null;
        CheckpointResult<GameSession> result = Restore(root);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("$." + member, result.Failure!.Path);
    }

    [Fact]
    public void InconsistentOwnerAdmissionLeavesOriginalSessionUntouched()
    {
        using GameSession original = Create();
        JsonObject root = Root(original);
        string before = root.ToJsonString();
        root["checkpoint"]!["control"]!["actors"] = new JsonArray();
        CheckpointResult<GameSession> result = Restore(root);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("$.checkpoint.control.actors", result.Failure!.Path);
        Assert.Equal(CheckpointFailureKind.InvalidCheckpoint, result.Failure.Kind);
        Assert.Equal(before, Root(original).ToJsonString());
    }

    [Fact]
    public void CompatibilityManifestMustMatchCheckpointPolicies()
    {
        using GameSession original = Create();
        JsonObject root = Root(original);
        root["compatibility"]!["runtimePolicies"]!["factRetentionCapacity"] = 99;
        Assert.Equal("$.compatibility.runtimePolicies", Restore(root).Failure!.Path);
    }

    [Fact]
    public void ExternalPropertyReorderingDoesNotChangeCanonicalAuthority()
    {
        using GameSession original = Create();
        JsonObject root = Root(original);
        string expected = root.ToJsonString();
        JsonObject checkpoint = root["checkpoint"]!.AsObject();
        JsonObject reordered = new();
        foreach (var property in checkpoint.Reverse()) reordered.Add(property.Key, property.Value?.DeepClone());
        root["checkpoint"] = reordered;
        using GameSession restored = Restore(root).Value!;
        Assert.NotNull(restored);
        Assert.Equal(expected, Root(restored).ToJsonString());
    }

    [Fact]
    public void RuntimePolicyMismatchRejectsBeforeRuntimeConstruction()
    {
        using GameSession original = Create();
        JsonObject root = Root(original);
        root["checkpoint"]!["runtimePolicies"]!["navigation"]!["behaviorVersion"] = 999;
        root["compatibility"]!["runtimePolicies"]!["navigation"]!["behaviorVersion"] = 999;
        CheckpointResult<GameSession> result = Restore(root);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.StartsWith("$.checkpoint.runtimePolicies", result.Failure!.Path, StringComparison.Ordinal);
        Assert.Equal(CheckpointFailureKind.IncompatibleRuntimePolicy, result.Failure.Kind);
    }

    [Fact]
    public void TypedReaderHonorsGenericResourceLimits()
    {
        using GameSession original = Create();
        InertSaveDocument document = GameSessionSaveCodec.Encode(original.CaptureCheckpoint().Value!, Metadata, Limits).Value!;
        var small = new SaveJsonLimits(128, 128, 16_384, 100_000);
        Assert.False(GameSessionSaveCodec.Restore(document, small).IsSuccess);
    }

    [Fact]
    public void ContentKeysUseValidatedExternalIdentity()
    {
        var codec = new CheckpointValueCodec();
        QualifiedContentKey key = QualifiedContentKey.Create("test.save", "cargo", "ore");
        Assert.Equal("test.save/cargo/ore", codec.Encode(key).GetString());
        Assert.Equal(key, codec.Decode<QualifiedContentKey>(codec.Encode(key)));
        using JsonDocument invalid = JsonDocument.Parse("\"Invalid/key\"");
        Assert.Throws<CheckpointJsonException>(() => codec.Decode<QualifiedContentKey>(invalid.RootElement));
    }

    [Fact]
    public void DictionaryEncodingUsesNumericDomainOrderAndRejectsDuplicateKeys()
    {
        var codec = new CheckpointValueCodec();
        codec.Scalar<MaterialId>("Value");
        codec.Scalar<Quantity>("Units");
        var source = new Dictionary<MaterialId, Quantity>
        {
            [new(10)] = new(2), [new(2)] = new(3),
        };
        JsonElement encoded = codec.Encode(source);
        Assert.Equal("2", encoded[0].GetProperty("key").GetString());
        Assert.Equal(source[new(10)], codec.Decode<IReadOnlyDictionary<MaterialId, Quantity>>(encoded)[new(10)]);
        using JsonDocument duplicate = JsonDocument.Parse("[{\"key\":\"2\",\"value\":\"1\"},{\"key\":\"2\",\"value\":\"4\"}]");
        Assert.Equal("$[1].key", Assert.Throws<CheckpointJsonException>(() =>
            codec.Decode<IReadOnlyDictionary<MaterialId, Quantity>>(duplicate.RootElement)).Path);
    }

    [Fact]
    public void FailureKindsDistinguishWireSchemaFromUnsupportedVersions()
    {
        using GameSession original = Create();
        JsonObject root = Root(original);
        root["checkpoint"]!["engine"]!["agenda"]!["currentTime"] = 0;
        Assert.Equal(CheckpointFailureKind.InvalidSchema, Restore(root).Failure!.Kind);
        root["schemaVersion"] = SaveFormat.CurrentSchemaVersion + 1;
        var future = new InertSaveDocument(SaveFormat.CurrentSchemaVersion + 1,
            JsonSerializer.SerializeToElement(root));
        Assert.Equal(CheckpointFailureKind.UnsupportedSchemaVersion,
            GameSessionSaveCodec.Restore(future, Limits).Failure!.Kind);
    }

    [Theory]
    [InlineData("2020-01-01T00:00:00+00:00")]
    [InlineData("2020-01-01t00:00:00z")]
    public void Rfc3339UtcMetadataAcceptsEquivalentExternalSpellings(string timestamp)
    {
        using GameSession original = Create();
        JsonObject root = Root(original);
        root["capturedAtUtc"] = timestamp;
        CheckpointResult<GameSession> result = Restore(root);
        Assert.True(result.IsSuccess, result.Failure?.Path);
        result.Value!.Dispose();
    }

    [Fact]
    public void UnknownMemberDiagnosticsDoNotEchoUnboundedOrNonSchemaKeys()
    {
        using GameSession original = Create();
        JsonObject root = Root(original);
        string arbitraryKey = new('x', 512);
        root[arbitraryKey] = true;
        CheckpointResult<GameSession> result = Restore(root);
        Assert.False(result.IsSuccess);
        Assert.Equal("$", result.Failure!.Path);
        Assert.DoesNotContain(arbitraryKey, result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidShapesAnywhereInPopulatedSaveReturnTypedRejections()
    {
        using GameSession original = Create();
        original.SubmitCommand(GameSessionTestFixture.Player, new MoveShipCommand(GameSessionTestFixture.Ship,
            GameSessionTestFixture.Destination(100, 0), OrderPlacement.ReplaceAll));
        JsonObject root = Root(original);
        foreach (string path in ShapePaths(root))
        {
            JsonObject altered = root.DeepClone().AsObject();
            JsonNode node = altered;
            foreach (string part in path.Split('/').Skip(1))
                node = node is JsonArray array ? array[int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)]! : node[part]!;
            node.ReplaceWith<JsonNode>(node is JsonObject ? new JsonArray() : new JsonObject());
            CheckpointResult<GameSession> result = Restore(altered);
            Assert.False(result.IsSuccess, path);
            Assert.NotNull(result.Failure);
            Assert.Null(result.Value);
        }
    }

    /// <summary>Enumerates populated structural positions without assuming any specific owner data shape.</summary>
    private static IEnumerable<string> ShapePaths(JsonNode node, string prefix = "")
    {
        if (prefix.Length > 0) yield return prefix;
        if (node is JsonObject obj)
        {
            foreach (var child in obj)
                if (child.Value is not null)
                    foreach (string path in ShapePaths(child.Value, prefix + "/" + child.Key)) yield return path;
        }
        else if (node is JsonArray array)
        {
            for (int index = 0; index < array.Count; index++)
                if (array[index] is { } child)
                    foreach (string path in ShapePaths(child, prefix + "/" + index)) yield return path;
        }
    }

    [Fact]
    public void CallerDepthLimitAppliesToEncodingAndDirectInertAdmission()
    {
        using GameSession original = Create();
        GameSessionCheckpoint checkpoint = original.CaptureCheckpoint().Value!;
        InertSaveDocument valid = GameSessionSaveCodec.Encode(checkpoint, Metadata, Limits).Value!;
        var shallow = new SaveJsonLimits(4_194_304, 2, 16_384, 100_000);
        Assert.False(GameSessionSaveCodec.Encode(checkpoint, Metadata, shallow).IsSuccess);
        Assert.False(GameSessionSaveCodec.Restore(valid, shallow).IsSuccess);
    }

    private static GameSession Create() => GameSessionTestFixture.Create(navigation:
        new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(100)));

    private static JsonObject Root(GameSession session)
    {
        CheckpointResult<InertSaveDocument> encoded = GameSessionSaveCodec.Encode(session.CaptureCheckpoint().Value!, Metadata, Limits);
        Assert.True(encoded.IsSuccess, encoded.Failure?.Path + ": " + encoded.Failure?.Message);
        return JsonNode.Parse(encoded.Value!.Root.GetRawText())!.AsObject();
    }

    private static CheckpointResult<GameSession> Restore(JsonObject root) => GameSessionSaveCodec.Restore(
        new InertSaveDocument(SaveFormat.CurrentSchemaVersion, JsonSerializer.SerializeToElement(root)), Limits);
}
