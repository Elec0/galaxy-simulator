using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class RuntimePolicyManifestTests
{
    private static readonly PrincipalId Principal = new(1);

    [Fact]
    public void CaptureAndResolvePreserveRegisteredBehaviorAndMaterializationPolicy()
    {
        WorldTopology topology = Topology();
        var navigation = new HierarchicalNavigationPlanner(
            topology.Connectors,
            new ChebyshevLocalTravelTimeEstimator(12));
        ShipMaterializationPolicy materialization = MaterializationPolicy();

        CheckpointResult<RuntimePolicyManifestCheckpoint> captured =
            RuntimePolicyManifest.Capture(
                topology,
                navigation,
                [materialization],
                factRetentionCapacity: 128);
        CheckpointResult<ResolvedRuntimePolicies> resolved = RuntimePolicyManifest.Resolve(
            Assert.IsType<RuntimePolicyManifestCheckpoint>(captured.Value),
            topology,
            [Principal]);

        Assert.True(resolved.IsSuccess);
        ResolvedRuntimePolicies policies = Assert.IsType<ResolvedRuntimePolicies>(
            resolved.Value);
        Assert.Equal(128, policies.FactRetentionCapacity);
        ShipMaterializationPolicy restored = Assert.Single(policies.MaterializationPolicies);
        Assert.Equal(materialization.FacilityId, restored.FacilityId);
        Assert.Equal(materialization.PrincipalId, restored.PrincipalId);
        Assert.Equal(materialization.Position, restored.Position);
        Assert.Equal(materialization.BaseController, restored.BaseController);
        Assert.Equal(
            materialization.AllowedDesigns.Single().Value.CargoCapacity,
            restored.AllowedDesigns.Single().Value.CargoCapacity);
        var planned = Assert.IsType<NavigationPlanResult.Planned>(
            policies.Navigation.Plan(new NavigationRequest(
                new ShipId(1),
                Position(1, 0, 0),
                new NavigationDestination.Position(Position(1, 3, 5)),
                SimulationTime.Zero)));
        Assert.Equal(new SimulationDuration(60), planned.Plan.TotalDuration);
    }

    [Fact]
    public void CaptureRejectsUnregisteredNavigationImplementation()
    {
        CheckpointResult<RuntimePolicyManifestCheckpoint> result =
            RuntimePolicyManifest.Capture(
                Topology(),
                new UnregisteredPlanner(),
                [],
                factRetentionCapacity: 8);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.runtimePolicies.navigation", result.Failure!.Path);
    }

    [Fact]
    public void CaptureRejectsHierarchicalPlannerBoundToDifferentTopology()
    {
        WorldTopology topology = Topology();
        var navigation = new HierarchicalNavigationPlanner(
            new ConnectorTopology([], []),
            new ChebyshevLocalTravelTimeEstimator(1));

        CheckpointResult<RuntimePolicyManifestCheckpoint> result =
            RuntimePolicyManifest.Capture(topology, navigation, [], 8);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.runtimePolicies.navigation.topology", result.Failure!.Path);
    }

    [Fact]
    public void ResolveRejectsUnknownNavigationKindOrVersion()
    {
        RuntimePolicyManifestCheckpoint checkpoint = Manifest();
        var unknownKind = checkpoint with
        {
            Navigation = checkpoint.Navigation! with { Kind = "unknown" },
        };
        var unknownVersion = checkpoint with
        {
            Navigation = checkpoint.Navigation! with { BehaviorVersion = 2 },
        };

        Assert.Equal(
            "$.checkpoint.runtimePolicies.navigation.kind",
            RuntimePolicyManifest.Resolve(unknownKind, Topology(), [Principal]).Failure!.Path);
        Assert.Equal(
            "$.checkpoint.runtimePolicies.navigation.behaviorVersion",
            RuntimePolicyManifest.Resolve(unknownVersion, Topology(), [Principal]).Failure!.Path);
    }

    [Fact]
    public void ResolveRejectsUnknownTravelTimeKindOrVersion()
    {
        RuntimePolicyManifestCheckpoint checkpoint = Manifest();
        var unknownKind = checkpoint with
        {
            TravelTime = checkpoint.TravelTime! with { Kind = "unknown" },
        };
        var unknownVersion = checkpoint with
        {
            TravelTime = checkpoint.TravelTime! with { BehaviorVersion = 2 },
        };

        Assert.Equal(
            "$.checkpoint.runtimePolicies.travelTime.kind",
            RuntimePolicyManifest.Resolve(unknownKind, Topology(), [Principal]).Failure!.Path);
        Assert.Equal(
            "$.checkpoint.runtimePolicies.travelTime.behaviorVersion",
            RuntimePolicyManifest.Resolve(unknownVersion, Topology(), [Principal]).Failure!.Path);
    }

    [Fact]
    public void ResolveRejectsInvalidFactRetentionCapacity()
    {
        RuntimePolicyManifestCheckpoint checkpoint = Manifest() with
        {
            FactRetentionCapacity = 0,
        };

        CheckpointResult<ResolvedRuntimePolicies> result =
            RuntimePolicyManifest.Resolve(checkpoint, Topology(), [Principal]);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "$.checkpoint.runtimePolicies.factRetentionCapacity",
            result.Failure!.Path);
    }

    [Fact]
    public void ResolveRejectsMaterializationPolicyWithUnknownReferences()
    {
        RuntimePolicyManifestCheckpoint checkpoint = Manifest();
        MaterializationPolicyCheckpoint policy = Assert.IsType<MaterializationPolicyCheckpoint>(
            Assert.Single(checkpoint.MaterializationPolicies));
        var unknownPrincipal = Copy(checkpoint, policy with { PrincipalId = new PrincipalId(9) });
        var unknownSystem = Copy(checkpoint, policy with { SystemId = new SystemId(9) });

        Assert.Equal(
            "$.checkpoint.runtimePolicies.materializationPolicies[0].principalId",
            RuntimePolicyManifest.Resolve(unknownPrincipal, Topology(), [Principal])
                .Failure!.Path);
        Assert.Equal(
            "$.checkpoint.runtimePolicies.materializationPolicies[0].systemId",
            RuntimePolicyManifest.Resolve(unknownSystem, Topology(), [Principal])
                .Failure!.Path);
    }

    [Fact]
    public void ResolveRejectsDuplicateFacilityAndDesignIdentity()
    {
        RuntimePolicyManifestCheckpoint checkpoint = Manifest();
        MaterializationPolicyCheckpoint policy = Assert.IsType<MaterializationPolicyCheckpoint>(
            Assert.Single(checkpoint.MaterializationPolicies));
        var duplicateFacility = checkpoint with
        {
            MaterializationPolicies = [policy, policy],
        };
        ShipDesignPolicyCheckpoint design = Assert.IsType<ShipDesignPolicyCheckpoint>(
            Assert.Single(policy.AllowedDesigns));
        var duplicateDesign = Copy(
            checkpoint,
            policy with { AllowedDesigns = [design, design] });

        Assert.Equal(
            "$.checkpoint.runtimePolicies.materializationPolicies[1].facilityId",
            RuntimePolicyManifest.Resolve(duplicateFacility, Topology(), [Principal])
                .Failure!.Path);
        Assert.Equal(
            "$.checkpoint.runtimePolicies.materializationPolicies[0].allowedDesigns[1].id",
            RuntimePolicyManifest.Resolve(duplicateDesign, Topology(), [Principal])
                .Failure!.Path);
    }

    private static RuntimePolicyManifestCheckpoint Manifest() =>
        Assert.IsType<RuntimePolicyManifestCheckpoint>(RuntimePolicyManifest.Capture(
            Topology(),
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(10)),
            [MaterializationPolicy()],
            64).Value);

    private static RuntimePolicyManifestCheckpoint Copy(
        RuntimePolicyManifestCheckpoint source,
        MaterializationPolicyCheckpoint policy) =>
        source with { MaterializationPolicies = [policy] };

    private static ShipMaterializationPolicy MaterializationPolicy() => new(
        new FacilityId(1),
        Principal,
        Position(1, 10, 20),
        new ActorController(ActorControllerKind.Autonomous, new CommandSourceId("yard")),
        InitialShipOrderPolicy.NoInitialOrder,
        [
            new ShipDesign(
                new ConstructionDesignId(1),
                "Scout",
                new ConstructionRecipe(
                    [new KeyValuePair<MaterialId, Quantity>(new MaterialId(1), new Quantity(2))],
                    new Work(3)),
                new Quantity(4)),
        ]);

    private static WorldTopology Topology() => new(
        [new StarSystem(new SystemId(1), "One")],
        new ConnectorTopology([], []));

    private static SystemPosition Position(ulong systemId, long x, long y) =>
        new(
            new SystemId(systemId),
            new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(y)));

    private sealed class UnregisteredPlanner : ISpatialNavigationPlanner
    {
        /// <inheritdoc />
        public NavigationPlanResult Plan(NavigationRequest request) =>
            new NavigationPlanResult.Unreachable(NavigationFailureReason.NoConnectorPath);
    }
}
