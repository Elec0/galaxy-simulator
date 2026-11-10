using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ShipConstructionCapabilityPolicyTests
{
    [Theory]
    [InlineData(0UL, 1UL)]
    [InlineData(123UL, 7UL)]
    [InlineData(ulong.MaxValue, ulong.MaxValue)]
    public void CaptureAndResolveRetainExactOptionalConstructionCapability(ulong range, ulong throughput)
    {
        var capability = new StationConstructionCapability(range, new(throughput));
        RuntimePolicyManifestCheckpoint manifest = Capture(Design(capability));
        StationConstructionCapabilityCheckpoint saved = Assert.IsType<StationConstructionCapabilityCheckpoint>(
            Assert.Single(manifest.ShipDefinitions!)!.StationConstructionCapability);

        Assert.Equal(range, saved.RangeUnits);
        Assert.Equal(throughput, saved.ThroughputUnitsPerSecond);
        CheckpointResult<ResolvedRuntimePolicies> resolved = RuntimePolicyManifest.Resolve(manifest, Topology(), []);

        Assert.True(resolved.IsSuccess, resolved.Failure?.Message);
        Assert.Equal(capability, Assert.Single(resolved.Value!.ShipDefinitions).StationConstructionCapability);
    }

    [Fact]
    public void RestoreDoesNotInferAbsentConstructionCapability()
    {
        RuntimePolicyManifestCheckpoint manifest = Capture(Design(null));
        CheckpointResult<ResolvedRuntimePolicies> resolved = RuntimePolicyManifest.Resolve(manifest, Topology(), []);

        Assert.True(resolved.IsSuccess, resolved.Failure?.Message);
        Assert.Null(Assert.Single(manifest.ShipDefinitions!)!.StationConstructionCapability);
        Assert.Null(Assert.Single(resolved.Value!.ShipDefinitions).StationConstructionCapability);
    }

    [Fact]
    public void RestoreRejectsZeroConstructionThroughputInsteadOfSilentlyDroppingCapability()
    {
        RuntimePolicyManifestCheckpoint manifest = Capture(Design(null));
        ShipDesignPolicyCheckpoint design = Assert.Single(manifest.ShipDefinitions!)!;
        RuntimePolicyManifestCheckpoint malformed = manifest with
        {
            ShipDefinitions = [design with { StationConstructionCapability = new(10, 0) }],
        };

        CheckpointResult<ResolvedRuntimePolicies> resolved = RuntimePolicyManifest.Resolve(malformed, Topology(), []);

        Assert.False(resolved.IsSuccess);
        Assert.Null(resolved.Value);
        Assert.Equal("$.checkpoint.runtimePolicies.shipDefinitions[0].stationConstructionCapability.throughputUnitsPerSecond",
            resolved.Failure!.Path);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(11UL, 2UL)]
    [InlineData(10UL, 3UL)]
    public void SameDesignIdentityCannotHaveDifferentConstructionCapability(ulong? range, ulong? throughput)
    {
        ShipDesign original = Design(new(10, new(2)));
        ShipDesign conflicting = Design(range is { } radius ? new(radius, new(throughput!.Value)) : null);

        Assert.Throws<ArgumentException>(() => new ShipDefinitionCatalog([original, conflicting]));
        CheckpointResult<RuntimePolicyManifestCheckpoint> captured = RuntimePolicyManifest.Capture(Topology(),
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)), [], 32, [original, conflicting]);
        Assert.False(captured.IsSuccess);
    }

    [Fact]
    public void EquivalentConstructionCapabilitiesShareOneRetainedDefinition()
    {
        var catalog = new ShipDefinitionCatalog([Design(new(10, new(2))), Design(new(10, new(2)))]);

        Assert.Single(catalog.Definitions);
    }

    [Fact]
    public void ProducerPolicyResolvesTheRetainedCapableDefinition()
    {
        ShipDesign design = Design(new(10, new(2)));
        var producer = new ShipMaterializationPolicy(new(1), new(1), new(new(1), new(new(0), new(0))),
            new(ActorControllerKind.Autonomous, new("yard")), InitialShipOrderPolicy.NoInitialOrder, [design]);
        RuntimePolicyManifestCheckpoint manifest = RuntimePolicyManifest.Capture(Topology(),
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)), [producer], 32, [design]).Value!;

        CheckpointResult<ResolvedRuntimePolicies> resolved = RuntimePolicyManifest.Resolve(manifest, Topology(), [new PrincipalId(1)]);

        Assert.True(resolved.IsSuccess, resolved.Failure?.Message);
        ShipDesign retained = Assert.Single(resolved.Value!.ShipDefinitions);
        Assert.Equal(design.StationConstructionCapability, retained.StationConstructionCapability);
        Assert.Same(retained, Assert.Single(Assert.Single(resolved.Value.MaterializationPolicies).AllowedDesigns).Value);
    }

    private static ShipDesign Design(StationConstructionCapability? construction)
    {
        ShipDesign source = GameSessionTestFixture.Design;
        return new(source.Id, source.Name, source.Recipe, source.CargoCapacity, source.ManeuverCapability,
            source.SensorRadiusUnits, source.PickupRangeUnits, construction);
    }

    private static RuntimePolicyManifestCheckpoint Capture(ShipDesign design) => RuntimePolicyManifest.Capture(Topology(),
        new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)), [], 32, [design]).Value!;

    private static WorldTopology Topology() => new([new StarSystem(new(1), "One")], new ConnectorTopology([], []));
}
