using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationEconomyArchiveTests
{
    [Fact]
    public void FinalModuleRemovalRetiresTerminalOwnerAndKeepsExactAllocatorAndStaleEventHistory()
    {
        var state = Create();
        var activation = Prepare(state.Owner, Module(new(3)), null, true, 0)!;
        activation.Apply();
        var before = state.Owner.CaptureCheckpoint(state.Planner).Value!;
        var line = Assert.Single(before.Production.Lines)!;
        var job = Assert.Single(line.Jobs)!;
        var removal = state.Owner.PrepareStationDestruction(new(1), [new(1)], false, [])!;
        state.Owner.ApplyStationProviderRemoval(removal);
        var checkpoint = state.Owner.CaptureCheckpoint(state.Planner).Value!;
        Assert.Empty(checkpoint.Facilities);
        Assert.Empty(checkpoint.Production.Lines);
        var retired = Assert.Single(checkpoint.RetiredFacilities!);
        Assert.Equal(ProductionJobStatus.Cancelled, Assert.Single(retired!.Production!.Jobs)!.Status);
        Assert.Equal(before.FacilityIds, checkpoint.FacilityIds);
        Assert.Equal(before.LocationIds, checkpoint.LocationIds);
        var stale = new EconomicEvent.ProductionComplete(line.FacilityId, job.Id);
        Assert.True(state.Owner.ContainsEventReference(stale));
        var restored = Restore(state, checkpoint);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.True(restored.Value!.ContainsEventReference(stale));
        Assert.Empty(restored.Value.CaptureStationFacilities());
        Assert.Empty(restored.Value.Runtime.Reconcile(new(100), state.Timing).Production.Commit.CompletionProposals);
    }

    [Fact]
    public void RestoreRejectsActiveArchiveAndMissingOrRegressedProviderAllocator()
    {
        var state = Create();
        Prepare(state.Owner, Module(new(3)), null, true, 0)!.Apply();
        var before = state.Owner.CaptureCheckpoint(state.Planner).Value!;
        var removal = state.Owner.PrepareStationDestruction(new(1), [new(1)], false, [])!;
        state.Owner.ApplyStationProviderRemoval(removal);
        var checkpoint = state.Owner.CaptureCheckpoint(state.Planner).Value!;
        var retired = Assert.Single(checkpoint.RetiredFacilities!)!;
        Assert.False(Restore(state, checkpoint with { RetiredFacilities = [retired with { Production = before.Production.Lines[0] }] }).IsSuccess);
        Assert.False(Restore(state, checkpoint with { FacilityIds = null }).IsSuccess);
        Assert.False(Restore(state, checkpoint with { FacilityIds = new(1) }).IsSuccess);
        Assert.False(Restore(state, checkpoint with { LocationIds = new(1) }).IsSuccess);
    }

    [Fact]
    public void DeletedStorageAllowsOnlyZeroMarketsAndTerminalTransportNamedByRetiredDescriptor()
    {
        var state = Create();
        Prepare(state.Owner, Module(new(3)), null, true, 0)!.Apply();
        state.Owner.ApplyStationProviderRemoval(state.Owner.PrepareStationDestruction(new(1), [new(1)], false, [])!);
        var checkpoint = state.Owner.CaptureCheckpoint(state.Planner).Value!;
        var facility = checkpoint.RetiredFacilities![0]!.Facility;
        var emptyLifecycle = new EntityLifecycleOwner(new(), new(), new(), []);
        var market = new TransportDemandCheckpoint(new(1), facility.InventoryId, facility.LocationId, new(1), Quantity.Zero,
            default, SimulationTime.Zero);
        var supply = new TransportSupplyCheckpoint(new(1), facility.InventoryId, facility.LocationId, new(1), Quantity.Zero);
        var job = new TransportJobCheckpoint(new(1), new(1), new(1), new(1), facility.InventoryId, facility.LocationId,
            facility.InventoryId, facility.LocationId, new(1), new(1), new(1), null, SimulationTime.Zero, new(1),
            TransportJobStatus.Cancelled, null);
        checkpoint = checkpoint with { Transport = checkpoint.Transport with
        {
            Ids = new(new(2), new(2), new(2)), ReservationIds = new(2),
            Board = new([supply], [market], [job]),
        }};
        var missing = (state.Owner, state.Timing, state.Inventory, state.Definition, state.Planner, emptyLifecycle);
        Assert.True(Restore(missing, checkpoint).IsSuccess);
        Assert.False(Restore(missing, checkpoint with { Transport = checkpoint.Transport with
        { Board = checkpoint.Transport.Board with { Demands = [market with { Remaining = new(1) }] } } }).IsSuccess);
        Assert.False(Restore(missing, checkpoint with { Transport = checkpoint.Transport with
        { Board = checkpoint.Transport.Board with { Jobs = [job with { Status = TransportJobStatus.Assigned }] } } }).IsSuccess);
        Assert.False(Restore(missing, checkpoint with { RetiredFacilities = [] }).IsSuccess);
    }

    [Fact]
    public void RestoreRetainsCancelledConstructionDesignAndRejectsUnknownDesignOrMissingCompletedReceipt()
    {
        var state = Create();
        Prepare(state.Owner, Module(new(3)), null, true, 0)!.Apply();
        state.Owner.ApplyStationProviderRemoval(state.Owner.PrepareStationDestruction(new(1), [new(1)], false, [])!);
        var checkpoint = state.Owner.CaptureCheckpoint(state.Planner).Value!;
        var retired = checkpoint.RetiredFacilities![0]!;
        var order = new ConstructionOrderCheckpoint(new(1), GameSessionTestFixture.Design.Id,
            ConstructionOrderStatus.Cancelled, null, new(1), []);
        var process = new ConstructionProcessCheckpoint(retired.Facility.FacilityId, retired.Facility.InventoryId,
            new(3), null, [], [order], [], []) { IsProviderAvailable = false };
        checkpoint = checkpoint with { Construction = checkpoint.Construction with { OrderIds = new(2) },
            RetiredFacilities = [retired with { Construction = process }] };
        var topology = new WorldTopology([new StarSystem(GameSessionTestFixture.System, "Test")], new ConnectorTopology([], []));
        var restored = SessionEconomyOwner.RestoreCheckpoint(checkpoint, state.Lifecycle, topology, state.Planner, [GameSessionTestFixture.Design]);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        Assert.True(restored.Value!.ContainsEventReference(new EconomicEvent.ConstructionComplete(process.FacilityId, order.Id)));
        Assert.True(restored.Value.TryResolveArchivedConstructionOrder(process.FacilityId, order.Id, out var design));
        Assert.Equal(order.DesignId, design);
        Assert.False(Restore(state, checkpoint).IsSuccess);
        var completed = order with { Status = ConstructionOrderStatus.Completed, CompletesAt = new(100), WorkState = new(0, null) };
        var finishedProcess = process with { Orders = [completed] };
        Assert.False(SessionEconomyOwner.RestoreCheckpoint(checkpoint with { RetiredFacilities = [retired with
            { Construction = finishedProcess }] }, state.Lifecycle, topology, state.Planner, [GameSessionTestFixture.Design]).IsSuccess);
        var effect = new ConstructionMaterializationEffect(process.FacilityId, order.Id, order.DesignId,
            new(100), order.Generation, null);
        finishedProcess = finishedProcess with { MaterializationReceipts = [new(effect, null)] };
        Assert.True(SessionEconomyOwner.RestoreCheckpoint(checkpoint with { RetiredFacilities = [retired with
            { Construction = finishedProcess }] }, state.Lifecycle, topology, state.Planner, [GameSessionTestFixture.Design]).IsSuccess);
    }

    [Fact]
    public void RestoreRejectsAnyLiveCapacityClaimOwnedByTerminalArchive()
    {
        var state = Create();
        Prepare(state.Owner, Module(new(3)), null, true, 0)!.Apply();
        state.Owner.ApplyStationProviderRemoval(state.Owner.PrepareStationDestruction(new(1), [new(1)], false, [])!);
        var checkpoint = state.Owner.CaptureCheckpoint(state.Planner).Value!;
        var job = checkpoint.RetiredFacilities![0]!.Production!.Jobs[0]!;
        state.Inventory.ReserveCapacity(new(99), new(1), new ReservationOwner.ProductionJob(job.Id));
        Assert.False(Restore(state, checkpoint).IsSuccess);
    }

    [Fact]
    public void BaseLossAfterLastProviderRetiredCancelsLoadedTransportAndWithdrawsHistoricalMarkets()
    {
        var state = Create();
        state.Lifecycle.RegisterSetup([new InitialShipSetup(GameSessionTestFixture.Entity, GameSessionTestFixture.Ship,
            GameSessionTestFixture.CargoInventory, GameSessionTestFixture.Principal, GameSessionTestFixture.Design,
            GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)]);
        state.Lifecycle.Inventories.Get(GameSessionTestFixture.CargoInventory)!.Add(new(1), new(1));
        Prepare(state.Owner, Module(new(3)), null, true, 0)!.Apply();
        state.Owner.ApplyStationProviderRemoval(state.Owner.PrepareStationDestruction(new(1), [new(1)], false, [])!);
        var checkpoint = state.Owner.CaptureCheckpoint(state.Planner).Value!;
        var facility = checkpoint.RetiredFacilities![0]!.Facility;
        var job = new TransportJobCheckpoint(new(1), GameSessionTestFixture.Ship, new(1), new(1), facility.InventoryId,
            facility.LocationId, facility.InventoryId, facility.LocationId, new(1), new(1), new(1), null,
            SimulationTime.Zero, new(1), TransportJobStatus.TravelingToDestination, new(100));
        checkpoint = checkpoint with { Transport = checkpoint.Transport with
        {
            Ids = new(new(2), new(2), new(2)), ReservationIds = new(2),
            Board = new([new(new(1), facility.InventoryId, facility.LocationId, new(1), new(1))],
                [new(new(1), facility.InventoryId, facility.LocationId, new(1), Quantity.Zero, default, SimulationTime.Zero)], [job]),
            Freighters = [new(GameSessionTestFixture.Ship, facility.LocationId, GameSessionTestFixture.CargoInventory, new(1))],
        }};
        var restored = Restore(state, checkpoint);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        var moduleOnly = restored.Value!.PrepareStationDestruction(new(1), [], false, [])!;
        Assert.Empty(moduleOnly.Transports);
        var removal = restored.Value.PrepareStationDestruction(new(1), [], true, [])!;
        Assert.Single(removal.Transports);
        var loss = CargoSourceLoss.Prepare(state.Inventory, [], new([state.Definition]), []).Prepared!;
        Assert.True(removal.CanCommit(loss));
        Assert.True(loss.Apply());
        removal.ApplyAfterSourceLoss(loss);
        var after = restored.Value.CaptureCheckpoint(state.Planner).Value!;
        Assert.Equal(TransportJobStatus.Cancelled, Assert.Single(after.Transport.Board.Jobs)!.Status);
        Assert.Equal(Quantity.Zero, Assert.Single(after.Transport.Board.Supplies)!.Remaining);
        Assert.Equal(Quantity.Zero, Assert.Single(after.Transport.Board.Demands)!.Remaining);
        Assert.Equal(new Quantity(1), state.Lifecycle.Inventories.Get(GameSessionTestFixture.CargoInventory)!.Stored(new(1)));
    }

    private static CheckpointResult<SessionEconomyOwner> Restore(
        (SessionEconomyOwner Owner, TransportTiming Timing, Inventory Inventory, PhysicalDefinition Definition, ISpatialNavigationPlanner Planner, EntityLifecycleOwner Lifecycle) state,
        SessionEconomyCheckpoint checkpoint) => SessionEconomyOwner.RestoreCheckpoint(checkpoint, state.Lifecycle,
            new WorldTopology([new StarSystem(GameSessionTestFixture.System, "Test")], new ConnectorTopology([], [])), state.Planner, []);

    private static PreparedStationProviderChange? Prepare(SessionEconomyOwner owner, StationModuleDefinition module,
        FacilityId? facility, bool available, ulong at) => owner.PrepareStationProvider(new(1), new(10), new(7),
            new(1), GameSessionTestFixture.Position(0, 0), new(1), facility, module, available, new(at));

    private static StationModuleDefinition Module(Throughput throughput, MaterialId? output = null) => new(
        QualifiedContentKey.Create("test.dynamic-provider", "station-module", "factory"), "Factory", 1, new(0), 0,
        null, new([], new(1)), new(1), [], [], productionProvider:
            new StationProductionProvider(new([], output ?? new(1), new(1), new(7)), throughput, false));

    private static (SessionEconomyOwner Owner, TransportTiming Timing, Inventory Inventory, PhysicalDefinition Definition, ISpatialNavigationPlanner Planner, EntityLifecycleOwner Lifecycle) Create()
    {
        var definition = new PhysicalDefinition(QualifiedContentKey.Create("test.dynamic-provider", "material", "output"),
            PhysicalHoldingKind.Fungible, new(1));
        var materials = new MaterialInventoryCompatibilityMap([new(new MaterialId(1), definition)]);
        var lifecycle = new EntityLifecycleOwner(new(), new(), new(), [], materials);
        var inventory = new Inventory(new(7), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(new(10)), new(1)), new(20), materials);
        lifecycle.RegisterEconomyInventory(inventory);
        var planner = new DirectLocalNavigationPlanner(new GameSessionTestFixture.FixedTravelTimeEstimator());
        var navigation = new HierarchicalLogisticsNavigation(new Dictionary<LocationId, SystemPosition>(), planner);
        var timing = new TransportTiming(new(1), new(1), new(1));
        var setup = new GameSessionEconomySetup([], [], [], [], [], [], [], navigation, timing, materials);
        return (new SessionEconomyOwner(setup, lifecycle), timing, inventory, definition, planner, lifecycle);
    }
}
