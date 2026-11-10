using GalaxyCommand.Simulation.Acceptance;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PhaseOneStationConversionTests
{
    [Fact]
    public void CanonicalFixtureBindsEveryFacilityToRealOperationalStationAuthority()
    {
        PhaseOneFixtureState fixture = PhaseOneFixture.Create(new());
        Assert.NotNull(fixture.StationOwner);
        IReadOnlyList<StationSnapshot> stations = fixture.StationOwner.Capture();
        Assert.Equal(3, stations.Count);
        Assert.Equal(4, stations.Sum(station => station.Modules.Count));
        Assert.Equal(4, fixture.StationFacilities.Count);
        Assert.All(stations, station =>
        {
            Assert.Equal(StationPhase.Operational, station.Phase);
            Assert.Empty(station.Projects);
            Inventory actual = fixture.World.Inventories.Get(station.InventoryId)!;
            Assert.Same(actual, fixture.StationLifecycle!.Inventories.Get(station.InventoryId));
            Assert.Equal(new InventoryCustody.PrincipalControlled(
                new InventoryOwnerReference.SessionEntity(station.EntityId), station.PrincipalId), actual.Custody);
        });
        foreach (EconomyFacilitySetup facility in fixture.StationFacilities)
        {
            Assert.NotNull(facility.StationBinding);
            StationSnapshot station = stations.Single(item => item.Id == facility.StationBinding.StationId);
            Assert.Contains(station.Modules, module => module.Id == facility.StationBinding.ModuleId);
            Assert.Equal(station.InventoryId, facility.InventoryId);
            Assert.Equal(station.Position, facility.Position);
        }
    }

    [Fact]
    public void ComponentProductionAndShipyardUseTheSameStationInventoryWithoutInternalTransport()
    {
        PhaseOneFixtureState fixture = PhaseOneFixture.Create(new());
        ProductionLine components = fixture.World.ProductionLines[new FacilityId(3)];

        Assert.Equal(components.InventoryId, fixture.Shipyard.InventoryId);
        Assert.Equal(new Quantity(200), fixture.World.Inventories.Get(components.InventoryId)!.Capacity);
        Assert.Equal(5, fixture.World.Inventories.CaptureCheckpoint().Inventories.Count);

        var scenario = new PhaseOneScenario();
        PhaseOneReport report = scenario.RunUntilFirstShip(new(1_000_000));
        Assert.NotNull(report.ConstructedShipId);
        Assert.Equal(3, scenario.CaptureStations().Count);
        Assert.DoesNotContain(report.Metrics.CargoDelivered.Keys,
            delivery => delivery.Inventory == components.InventoryId && delivery.Material == new MaterialId(3));
        Assert.Contains(report.Metrics.MaterialProduced, produced => produced.Key.Material == new MaterialId(3));
        Assert.Contains(report.Metrics.MaterialConsumed, consumed => consumed.Key.Material == new MaterialId(3));
        Assert.Equal(new Quantity(4), report.Metrics.MaterialProduced[(new FacilityId(3), new MaterialId(3))]);
        Assert.Equal(new Quantity(4), report.Metrics.MaterialConsumed[(new FacilityId(4), new MaterialId(3))]);
    }

    [Fact]
    public void AuthoredInitialStationAuthorityRoundTripsWithoutFabricatedProjectHistory()
    {
        PhaseOneFixtureState fixture = PhaseOneFixture.Create(new());
        Assert.NotNull(fixture.StationOwner);
        Assert.NotNull(fixture.StationLifecycle);
        StationRuntimeCheckpoint checkpoint = fixture.StationOwner.CaptureCheckpoint();
        PhysicalDefinitionCatalog definitions = new(checkpoint.Setup.Materials.Mappings.Values);
        CheckpointResult<EntityLifecycleOwner> lifecycle = EntityLifecycleOwner.RestoreCheckpoint(
            fixture.StationLifecycle.CaptureCheckpoint(), new(), new(), new(), [], definitions, checkpoint.Setup.Materials);
        Assert.True(lifecycle.IsSuccess, lifecycle.Failure?.Message);
        CheckpointResult<StationRuntimeOwner> restored = StationRuntimeOwner.Restore(checkpoint, lifecycle.Value!, new(),
            (_, _) => null, SimulationTime.Zero, [new SystemId(1), new SystemId(2), new SystemId(3)], [new PrincipalId(1)]);

        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.Equal(3, restored.Value!.Capture().Count);
        Assert.All(restored.Value.Capture(), station => Assert.Empty(station.Projects));
        StationStateCheckpoint first = checkpoint.Stations[0];
        CheckpointResult<StationRuntimeOwner> omitted = StationRuntimeOwner.Restore(checkpoint with
        { Stations = checkpoint.Stations.Select(station => station.Id == first.Id ? station with { Modules = [] } : station).ToArray() },
            EntityLifecycleOwner.RestoreCheckpoint(fixture.StationLifecycle.CaptureCheckpoint(), new(), new(), new(), [], definitions, checkpoint.Setup.Materials).Value!,
            new(), (_, _) => null, SimulationTime.Zero, [new SystemId(1), new SystemId(2), new SystemId(3)], [new PrincipalId(1)]);
        Assert.False(omitted.IsSuccess);
    }
}
