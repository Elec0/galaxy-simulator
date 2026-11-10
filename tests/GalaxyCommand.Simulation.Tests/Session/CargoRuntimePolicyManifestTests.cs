using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoRuntimePolicyManifestTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 999)]
    public void SessionRestoreRejectsCargoWithoutPolicyOrKnownSystem(bool selectedPolicy, ulong systemId)
    {
        using GameSession session = GameSessionTestFixture.Create(navigation:
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        GameSessionCheckpoint saved = session.CaptureCheckpoint().Value!;
        var ore = new PhysicalDefinition(QualifiedContentKey.Create("test.cargo", "commodity", "ore"),
            PhysicalHoldingKind.Fungible, new(2));
        Inventory inventory = Inventory.CreateCargoObjectInventory(new(2), new(2), ore,
            new PhysicalTransferSubject.Fungible(ore.Key, new(1)));
        EntityLifecycleCheckpoint original = saved.Lifecycle;
        var lifecycle = new EntityLifecycleCheckpoint(new(3), original.ShipIds, new(3),
            new(original.Inventories.Inventories.Append(inventory.CaptureCheckpoint())), original.LiveShips,
            original.MaterializationReceipts, original.RemovalReceipts, original.SensorDeployables,
            [new CargoObjectSnapshot(new(2), new(2), new(new(systemId), GameSessionTestFixture.Position(0, 0).Position))]);
        var changed = saved with
        {
            Lifecycle = lifecycle,
            RuntimePolicies = saved.RuntimePolicies with { CargoPolicy = selectedPolicy ? Capture(Policy()).CargoPolicy : null },
        };
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(changed, new PhysicalDefinitionCatalog([ore]));
        result.Value?.Dispose();
        Assert.False(result.IsSuccess);
        Assert.StartsWith("$.checkpoint.lifecycle.cargoObjects", result.Failure!.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionCheckpointContinuationRetainsTheExplicitlySelectedCargoPolicy()
    {
        CargoDispositionPolicy policy = Policy();
        var setup = new GameSessionSetup([], [], new ConnectorTopology([], []), [], null, GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed, factRetentionCapacity: 64,
            playerCommandSource: GameSessionTestFixture.Player, cargoPolicy: policy);
        using var session = new GameSession(setup,
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        GameSessionCheckpoint saved = Assert.IsType<GameSessionCheckpoint>(session.CaptureCheckpoint().Value);
        CargoDispositionPolicyCheckpoint expected = Assert.IsType<CargoDispositionPolicyCheckpoint>(saved.RuntimePolicies.CargoPolicy);

        using GameSession restored = Assert.IsType<GameSession>(GameSession.RestoreCheckpoint(saved).Value);
        GameSessionCheckpoint continued = Assert.IsType<GameSessionCheckpoint>(restored.CaptureCheckpoint().Value);
        CargoDispositionPolicyCheckpoint actual = Assert.IsType<CargoDispositionPolicyCheckpoint>(continued.RuntimePolicies.CargoPolicy);
        Assert.Equal(expected.RecoveryByCause, actual.RecoveryByCause);
        AssertPolicyEqual(policy, RuntimePolicyManifest.Resolve(continued.RuntimePolicies, Topology(), []).Value!.CargoPolicy!);
    }

    [Fact]
    public void ManifestPreservesEveryExactCargoParameterAndCauseFraction()
    {
        CargoDispositionPolicy original = Policy();
        RuntimePolicyManifestCheckpoint manifest = Capture(original);
        CargoDispositionPolicyCheckpoint saved = Assert.IsType<CargoDispositionPolicyCheckpoint>(manifest.CargoPolicy);
        Assert.Equal(original.Key, saved.Key);
        Assert.Equal(original.GenerationBehaviorVersion, saved.GenerationBehaviorVersion);
        Assert.Equal(original.MergeBehaviorVersion, saved.MergeBehaviorVersion);
        Assert.Equal(original.OrderingBehaviorVersion, saved.OrderingBehaviorVersion);
        Assert.Equal(original.DurationBehaviorVersion, saved.DurationBehaviorVersion);

        CheckpointResult<ResolvedRuntimePolicies> result = RuntimePolicyManifest.Resolve(manifest, Topology(), []);
        Assert.True(result.IsSuccess);
        CargoDispositionPolicy restored = Assert.IsType<CargoDispositionPolicy>(result.Value!.CargoPolicy);
        AssertPolicyEqual(original, restored);
        Assert.NotSame(original, restored);
        Assert.Equal(saved.RecoveryByCause, Capture(restored).CargoPolicy!.RecoveryByCause);
    }

    [Fact]
    public void NoCargoManifestDoesNotAdmitAChosenDefaultPolicy()
    {
        RuntimePolicyManifestCheckpoint manifest = Capture(null);
        Assert.Null(manifest.CargoPolicy);
        CheckpointResult<ResolvedRuntimePolicies> result = RuntimePolicyManifest.Resolve(manifest, Topology(), []);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CargoPolicy);
    }

    [Theory]
    [InlineData("key")]
    [InlineData("kind")]
    [InlineData("generation")]
    [InlineData("merge")]
    [InlineData("ordering")]
    [InlineData("duration")]
    [InlineData("causes")]
    [InlineData("missing-cause")]
    [InlineData("duplicate-cause")]
    [InlineData("unknown-cause")]
    [InlineData("null-fraction")]
    [InlineData("zero-denominator")]
    [InlineData("excess-recovery")]
    [InlineData("lossy-ejection")]
    [InlineData("object-count")]
    [InlineData("radius")]
    [InlineData("lifetime")]
    [InlineData("zero-minimum")]
    [InlineData("reversed-duration")]
    [InlineData("storage-cap")]
    public void RestoreRejectsMalformedCargoPolicyWithoutPublishingPolicies(string invalidField)
    {
        RuntimePolicyManifestCheckpoint manifest = Capture(Policy());
        CargoDispositionPolicyCheckpoint saved = Assert.IsType<CargoDispositionPolicyCheckpoint>(manifest.CargoPolicy);
        CargoDispositionPolicyCheckpoint invalid = invalidField switch
        {
            "key" => saved with { Key = null },
            "kind" => saved with { Key = QualifiedContentKey.Create("test.cargo", "commodity", "other") },
            "generation" => saved with { GenerationBehaviorVersion = 2 },
            "merge" => saved with { MergeBehaviorVersion = 2 },
            "ordering" => saved with { OrderingBehaviorVersion = 2 },
            "duration" => saved with { DurationBehaviorVersion = 2 },
            "causes" => saved with { RecoveryByCause = null },
            "missing-cause" => saved with { RecoveryByCause = [new(CargoLossCause.EntityDestruction, 1, 2)] },
            "duplicate-cause" => saved with { RecoveryByCause = [new(CargoLossCause.EntityDestruction, 1, 2), new(CargoLossCause.EntityDestruction, 1, 2)] },
            "unknown-cause" => saved with { RecoveryByCause = [new((CargoLossCause)99, 1, 2)] },
            "null-fraction" => saved with { RecoveryByCause = [null] },
            "zero-denominator" => saved with { RecoveryByCause = [new(CargoLossCause.EntityDestruction, 1, 0)] },
            "excess-recovery" => saved with { RecoveryByCause = [new(CargoLossCause.EntityDestruction, 2, 1)] },
            "lossy-ejection" => saved with { RecoveryByCause = [new(CargoLossCause.EntityDestruction, 1, 2), new(CargoLossCause.StorageModuleLoss, 1, 2), new(CargoLossCause.FunctionalEjection, 1, 2)] },
            "object-count" => saved with { FungibleObjectCount = 0 },
            "radius" => saved with { ScatterRadiusMeters = (1UL << 50) + 1 },
            "lifetime" => saved with { Lifetime = SimulationDuration.Zero },
            "zero-minimum" => saved with { MinimumCollectionDuration = SimulationDuration.Zero },
            "reversed-duration" => saved with { MaximumCollectionDuration = new(1) },
            "storage-cap" => saved with { CollectionStorageCap = new(1) },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField)),
        };

        CheckpointResult<ResolvedRuntimePolicies> result = RuntimePolicyManifest.Resolve(
            manifest with { CargoPolicy = invalid }, Topology(), []);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.StartsWith("$.checkpoint.runtimePolicies.cargoPolicy", result.Failure!.Path, StringComparison.Ordinal);
    }

    private static void AssertPolicyEqual(CargoDispositionPolicy expected, CargoDispositionPolicy actual)
    {
        Assert.Equal(expected.Key, actual.Key);
        Assert.Equal(expected.GenerationBehaviorVersion, actual.GenerationBehaviorVersion);
        Assert.Equal(expected.MergeBehaviorVersion, actual.MergeBehaviorVersion);
        Assert.Equal(expected.OrderingBehaviorVersion, actual.OrderingBehaviorVersion);
        Assert.Equal(expected.DurationBehaviorVersion, actual.DurationBehaviorVersion);
        Assert.Equal(expected.RecoveryByCause, actual.RecoveryByCause);
        Assert.Equal(expected.FungibleObjectCount, actual.FungibleObjectCount);
        Assert.Equal(expected.ScatterRadiusMeters, actual.ScatterRadiusMeters);
        Assert.Equal(expected.Lifetime, actual.Lifetime);
        Assert.Equal(expected.MinimumCollectionDuration, actual.MinimumCollectionDuration);
        Assert.Equal(expected.MaximumCollectionDuration, actual.MaximumCollectionDuration);
        Assert.Equal(expected.CollectionStorageCap, actual.CollectionStorageCap);
    }

    private static RuntimePolicyManifestCheckpoint Capture(CargoDispositionPolicy? policy) =>
        Assert.IsType<RuntimePolicyManifestCheckpoint>(RuntimePolicyManifest.Capture(Topology(),
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)), [], 64,
            cargoPolicy: policy).Value);

    private static WorldTopology Topology() => new([new StarSystem(new SystemId(1), "One")], new ConnectorTopology([], []));

    private static CargoDispositionPolicy Policy() => new(
        QualifiedContentKey.Create("test.cargo", "cargo-policy", "custom"), 1, 1, 1, 1,
        [new(CargoLossCause.EntityDestruction, new(2, 7)), new(CargoLossCause.StorageModuleLoss, new(3, 8)), new(CargoLossCause.FunctionalEjection, new(5, 5))],
        7, 123, new(9999), new(12), new(45), new(73));
}
