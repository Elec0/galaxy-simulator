using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationProviderSessionTests
{
    private static readonly QualifiedContentKey Base = Key("station", "base");
    private static readonly QualifiedContentKey Factory = Key("station-module", "factory");
    private static readonly QualifiedContentKey Advanced = Key("station-module", "advanced");
    private static readonly QualifiedContentKey Incompatible = Key("station-module", "incompatible");
    private static readonly PhysicalDefinition Metal = new(Key("material", "metal"), PhysicalHoldingKind.Fungible, new(1));

    [Fact]
    public void ConstructedProviderPausesExactWorkAndRestoresWithoutAllocatingAnotherFacility()
    {
        using GameSession session = Create();
        BuildFactory(session);
        session.AdvanceTo(new(2_101));
        StationSnapshot station = Current(session);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        Assert.NotNull(module.FacilityId);
        Accepted(session, new SetStationModuleEnabledCommand(Principal, station.Id, station.Revision, module.Id, false));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        ProductionLineCheckpoint line = Assert.Single(checkpoint.Economy!.Production.Lines)!;
        Assert.False(line.IsProviderAvailable);
        Assert.Equal(ProductionJobStatus.Paused, Assert.Single(line.Jobs)!.Status);
        Assert.DoesNotContain(checkpoint.Engine.Agenda.PendingEvents, item => item.Payload is GameEvent.Economic
            { Event: EconomicEvent.ProductionComplete });
        CheckpointResult<GameSession> load = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(load.IsSuccess, load.Failure?.Message);
        using GameSession restored = load.Value!;
        restored.AdvanceTo(new(3_000));
        station = Current(restored);
        Accepted(restored, new SetStationModuleEnabledCommand(Principal, station.Id, station.Revision, module.Id, true));
        Assert.Equal(module.FacilityId, Assert.Single(Current(restored).Modules).FacilityId);
        restored.AdvanceTo(new(5_232));
        Assert.Empty(Storage(restored).FungibleHoldings);
        restored.AdvanceTo(new(5_233));
        Assert.Equal(new Quantity(1), Assert.Single(Storage(restored).FungibleHoldings).Quantity);
    }

    [Fact]
    public void UpgradeRetainsProviderIdentityAndRetunesAtStructuralCompletion()
    {
        using GameSession session = Create();
        BuildFactory(session);
        session.AdvanceTo(new(2_101));
        StationSnapshot station = Current(session);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        Accepted(session, new UpgradeStationModuleCommand(Principal, station.Id, station.Revision, module.Id, Advanced));
        AssignActive(session);
        session.AdvanceTo(new(3_101));
        StationModuleSnapshot upgraded = Assert.Single(Current(session).Modules);
        Assert.Equal(module.FacilityId, upgraded.FacilityId);
        Assert.Equal(Advanced, upgraded.DefinitionKey);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        var completion = Assert.Single(checkpoint.Engine.Agenda.PendingEvents, item => item.Payload
            is GameEvent.Economic { Event: EconomicEvent.ProductionComplete });
        Assert.Equal(new SimulationTime(3_841), completion.Key.Timestamp);
        session.AdvanceTo(new(3_841));
        Assert.Equal(new Quantity(1), Assert.Single(Storage(session).FungibleHoldings).Quantity);
    }

    [Fact]
    public void IncompatibleUpgradeRejectsBeforeProjectOrIdentityAllocation()
    {
        using GameSession session = Create();
        BuildFactory(session);
        StationSnapshot station = Current(session);
        GameSessionCheckpoint before = session.CaptureCheckpoint().Value!;
        GameplayCommandRecord result = session.SubmitCommand(GameSessionTestFixture.Player,
            new UpgradeStationModuleCommand(Principal, station.Id, station.Revision, Assert.Single(station.Modules).Id, Incompatible));
        Assert.Equal(CommandResultStatus.Rejected, result.Result.Status);
        GameSessionCheckpoint after = session.CaptureCheckpoint().Value!;
        Assert.Equal(before.Stations!.ProjectIds, after.Stations!.ProjectIds);
        Assert.Equal(before.Stations.ModuleIds, after.Stations.ModuleIds);
        Assert.Equal(station.Revision, Current(session).Revision);
    }

    [Fact]
    public void CancelledUnworkedDismantlingResumesTheSameEconomicWork()
    {
        using GameSession session = Create();
        BuildFactory(session);
        session.AdvanceTo(new(2_101));
        StationSnapshot station = Current(session);
        StationModuleId module = Assert.Single(station.Modules).Id;
        Accepted(session, new DismantleStationModuleCommand(Principal, station.Id, station.Revision, module, station.InventoryId));
        session.AdvanceTo(new(3_000));
        station = Current(session);
        StationProjectSnapshot dismantle = station.Projects.Single(project => project.Kind == StationProjectKind.Dismantling);
        Accepted(session, new ControlStationProjectCommand(Principal, station.Id, dismantle.Id, station.Revision, StationProjectAction.Cancel));
        session.AdvanceTo(new(5_233));
        Assert.Equal(new Quantity(1), Assert.Single(Storage(session).FungibleHoldings).Quantity);
    }

    [Fact]
    public void DynamicallyBuiltShipyardAcceptsOwnedQueueAndMaterializesAtTheStation()
    {
        using GameSession session = Create(shipyard: true);
        BuildFactory(session);
        StationSnapshot station = Current(session);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        Accepted(session, new EnqueueStationShipCommand(Principal, station.Id, station.Revision, module.Id, module.FacilityId!.Value, new(2)));
        session.AdvanceTo(new(2_101));
        station = Current(session);
        Accepted(session, new SetStationModuleEnabledCommand(Principal, station.Id, station.Revision, module.Id, false));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        Assert.Equal(ConstructionOrderStatus.Paused, Assert.Single(Assert.Single(checkpoint.Economy!.Construction.Processes)!.Orders)!.Status);
        CheckpointResult<GameSession> load = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(load.IsSuccess, load.Failure?.Message);
        using GameSession restored = load.Value!;
        restored.AdvanceTo(new(3_000));
        station = Current(restored);
        Accepted(restored, new SetStationModuleEnabledCommand(Principal, station.Id, station.Revision, module.Id, true));
        restored.AdvanceTo(new(5_233));
        GameShipSnapshot built = Assert.Single(restored.CaptureSnapshot().Ships, ship => ship.DesignId == new ConstructionDesignId(2));
        Assert.Equal(Principal, built.PrincipalId);
        Assert.Equal(GameSessionTestFixture.Position(0, 0), built.Position);
        Assert.Single(restored.ReadFactsAfter(null, 256).Facts, envelope => envelope.Fact is StationShipConstructionQueuedFact);
        Assert.True(GameSession.RestoreCheckpoint(restored.CaptureCheckpoint().Value!).IsSuccess);
    }

    [Fact]
    public void FinalDismantleArchivesProviderAndRejectsOmittedHistoricalEvidence()
    {
        using GameSession session = Create();
        BuildFactory(session);
        StationSnapshot station = Current(session);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        Accepted(session, new DismantleStationModuleCommand(Principal, station.Id, station.Revision, module.Id, station.InventoryId));
        AssignActive(session);
        session.AdvanceTo(new(3_000));
        Assert.Empty(Current(session).Modules);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        var archive = Assert.Single(checkpoint.Economy!.RetiredFacilities!);
        Assert.Equal(module.FacilityId, archive!.Facility.FacilityId);
        CheckpointResult<GameSession> load = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(load.IsSuccess, load.Failure?.Message);
        load.Value?.Dispose();
        Assert.False(GameSession.RestoreCheckpoint(checkpoint with { Economy = checkpoint.Economy with { RetiredFacilities = [] } }).IsSuccess);
        var state = Assert.Single(checkpoint.Stations!.Stations);
        var projects = state.Projects.Select(project => project.Kind == StationProjectKind.Dismantling
            ? project with { ProviderFacilityId = new(999) } : project).ToArray();
        Assert.False(GameSession.RestoreCheckpoint(checkpoint with { Stations = checkpoint.Stations with
            { Stations = [state with { Projects = projects }] } }).IsSuccess);
    }

    [Fact]
    public void IdleQueueAndInputChangesCoalesceOneWakeWithoutCommandTimeConsumption()
    {
        using GameSession session = Create(shipyard: true, inputs: true);
        BuildFactory(session);
        StationSnapshot station = Current(session);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        session.CommitInventoryMutations([new StoreFungibleInventoryProposal(new(InventoryOperationSourceKind.Explicit, 1), station.InventoryId, Metal, new(3))]);
        Accepted(session, new EnqueueStationShipCommand(Principal, station.Id, station.Revision, module.Id, module.FacilityId!.Value, new(2)));
        station = Current(session);
        Accepted(session, new EnqueueStationShipCommand(Principal, station.Id, station.Revision, module.Id, module.FacilityId.Value, new(2)));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        Assert.Single(checkpoint.Engine.Agenda.PendingEvents, item => item.Payload is GameEvent.EconomicReevaluation);
        Assert.Equal(new Quantity(3), Assert.Single(Storage(session).FungibleHoldings).Quantity);
        Assert.Empty(Storage(session).Reservations);
        Assert.All(Assert.Single(checkpoint.Economy!.Construction.Processes)!.Orders,
            order => Assert.Equal(ConstructionOrderStatus.WaitingForInputs, order!.Status));
        CheckpointResult<GameSession> load = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(load.IsSuccess, load.Failure?.Message);
        using GameSession restored = load.Value!;
        int beforeEvents = session.EventRecords.Count;
        session.AdvanceTo(new(2_101));
        restored.AdvanceTo(new(2_001));
        restored.AdvanceTo(new(2_101));
        Assert.Empty(Storage(session).FungibleHoldings);
        Assert.Empty(Storage(restored).FungibleHoldings);
        Assert.Equal(Assert.Single(session.CaptureCheckpoint().Value!.Economy!.Construction.Processes)!.ActiveOrderId,
            Assert.Single(restored.CaptureCheckpoint().Value!.Economy!.Construction.Processes)!.ActiveOrderId);
        Assert.Equal(session.EventRecords.Skip(beforeEvents).Select(item => (item.Timestamp, item.Kind)), restored.EventRecords.Select(item => (item.Timestamp, item.Kind)));
    }

    [Fact]
    public void QueueAuthorityFacilityAndDesignRejectionsAllocateNoOrderIdentity()
    {
        using GameSession session = Create(shipyard: true);
        BuildFactory(session);
        StationSnapshot station = Current(session);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        var command = new EnqueueStationShipCommand(Principal, station.Id, station.Revision, module.Id, module.FacilityId!.Value, new(2));
        var before = session.CaptureCheckpoint().Value!.Economy!.Construction.OrderIds;
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(new(CommandSourceKind.Player, new("rogue-player")), command).Result.Status);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(GameSessionTestFixture.Player, command with { ExpectedFacility = new(999) }).Result.Status);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(GameSessionTestFixture.Player, command with { Design = new(999) }).Result.Status);
        Assert.Equal(before, session.CaptureCheckpoint().Value!.Economy!.Construction.OrderIds);
        Assert.Empty(session.CaptureCheckpoint().Value!.Stations!.ShipOrders);
        Accepted(session, new SetStationModuleEnabledCommand(Principal, station.Id, station.Revision, module.Id, false));
        station = Current(session);
        Accepted(session, command with { ExpectedStationRevision = station.Revision });
        station = Current(session);
        Accepted(session, new DismantleStationModuleCommand(Principal, station.Id, station.Revision, module.Id, station.InventoryId));
        station = Current(session);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(GameSessionTestFixture.Player,
            command with { ExpectedStationRevision = station.Revision }).Result.Status);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("phase")]
    [InlineData("generation")]
    [InlineData("future")]
    [InlineData("omitted-envelope")]
    [InlineData("omitted-owner")]
    public void RestoreRejectsMalformedEconomicWake(string fault)
    {
        using GameSession session = Create(shipyard: true);
        BuildFactory(session);
        StationSnapshot station = Current(session);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        Accepted(session, new EnqueueStationShipCommand(Principal, station.Id, station.Revision, module.Id, module.FacilityId!.Value, new(2)));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        EventAgendaCheckpoint<GameEvent> agenda = checkpoint.Engine.Agenda;
        var wake = Assert.Single(agenda.PendingEvents, item => item.Payload is GameEvent.EconomicReevaluation);
        ScheduledEvent<GameEvent> changed = fault switch
        {
            "phase" => wake with { Key = wake.Key with { Phase = EventPhase.PhysicalCompletion } },
            "generation" => wake with { Generation = new(1) },
            "future" => wake with { Key = wake.Key with { Timestamp = new(2_001) } },
            _ => wake with { Key = wake.Key with { CreationSequence = agenda.NextCreationSequence } },
        };
        IEnumerable<ScheduledEvent<GameEvent>> events = fault == "omitted-envelope" ? agenda.PendingEvents.Where(item => item != wake)
            : fault == "duplicate" ? agenda.PendingEvents.Append(changed) : agenda.PendingEvents.Select(item => item == wake ? changed : item);
        var malformed = checkpoint with { Engine = new(checkpoint.Engine.IsInitialized, checkpoint.Engine.AccruedThrough,
            new(agenda.CurrentTime, agenda.NextCreationSequence + 1, events)) };
        if (fault == "omitted-owner") malformed = checkpoint with { Economy = checkpoint.Economy! with { PendingWake = null } };
        Assert.False(GameSession.RestoreCheckpoint(malformed).IsSuccess);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DestroyedProviderCancelsExactWaitingClaimAndRestoresTerminalHistory(bool shipyard)
    {
        using GameSession session = Create(shipyard, inputs: true);
        BuildFactory(session);
        StationSnapshot station = Current(session);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        if (shipyard) Accepted(session, new EnqueueStationShipCommand(Principal, station.Id, station.Revision, module.Id, module.FacilityId!.Value, new(2)));
        session.CommitInventoryMutations([new StoreFungibleInventoryProposal(new(InventoryOperationSourceKind.Explicit, 1), station.InventoryId, Metal, new(2))]);
        session.AdvanceTo(new(2_001));
        Assert.Equal(new Quantity(2), Assert.Single(Storage(session).Reservations).Quantity);
        GameplayCommandRecord trigger = session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll));
        station = Current(session);
        StationDestructionResult result = session.CommitStationDestruction(new(new CargoDispositionTrigger.Command(trigger.Envelope.Sequence),
            station.Id, station.Revision, [module.Id], true, 1));
        Assert.True(result.IsSuccess, result.Failure?.ToString());
        Assert.Empty(session.CaptureSnapshot().Stations);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        RetiredEconomyFacilityCheckpoint archive = Assert.Single(checkpoint.Economy!.RetiredFacilities!)!;
        if (shipyard) Assert.Equal(ConstructionOrderStatus.Cancelled, Assert.Single(archive.Construction!.Orders)!.Status);
        else Assert.Equal(ProductionJobStatus.Cancelled, Assert.Single(archive.Production!.Jobs)!.Status);
        Assert.DoesNotContain(checkpoint.Lifecycle.Inventories.Inventories, inventory => inventory.Id == station.InventoryId);
        CheckpointResult<GameSession> load = GameSession.RestoreCheckpoint(checkpoint);
        Assert.True(load.IsSuccess, load.Failure?.Message);
        load.Value?.Dispose();
    }

    [Fact]
    public void IdleQueueRejectsBeforeOrderAllocationWhenWakeAgendaIsExhausted()
    {
        using GameSession source = Create(shipyard: true);
        BuildFactory(source);
        using GameSession session = AtCreationSequence(source, ulong.MaxValue);
        GameSessionCheckpoint before = session.CaptureCheckpoint().Value!;
        StationSnapshot station = Current(session);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        GameplayCommandRecord? result = null;
        Exception? failure = Record.Exception(() => result = session.SubmitCommand(GameSessionTestFixture.Player,
            new EnqueueStationShipCommand(Principal, station.Id, station.Revision, module.Id, module.FacilityId!.Value, new(2))));
        Assert.Null(failure);
        Assert.Equal(CommandResultStatus.Rejected, result!.Result.Status);
        GameSessionCheckpoint after = session.CaptureCheckpoint().Value!;
        Assert.Equal(before.Economy!.Construction.OrderIds, after.Economy!.Construction.OrderIds);
        Assert.Empty(Assert.Single(after.Economy.Construction.Processes)!.Orders);
        Assert.Equal(station.Revision, Current(session).Revision);
        Assert.Null(after.Economy.PendingWake);
        Assert.Equal(before.Engine.Agenda.NextCreationSequence, after.Engine.Agenda.NextCreationSequence);
    }

    [Fact]
    public void InputBatchRejectsBeforeAnyHoldingOrReservationWhenWakeAgendaIsExhausted()
    {
        using GameSession source = Create(shipyard: true);
        BuildFactory(source);
        using GameSession session = AtCreationSequence(source, ulong.MaxValue);
        InventoryId inventory = Current(session).InventoryId;
        GameSessionCheckpoint before = session.CaptureCheckpoint().Value!;
        InventoryCommitBatchResult? result = null;
        Exception? failure = Record.Exception(() => result = session.CommitInventoryMutations([
            new StoreFungibleInventoryProposal(new(InventoryOperationSourceKind.Explicit, 1), inventory, Metal, new(1)),
            new ReservePhysicalInventoryProposal(new(InventoryOperationSourceKind.Explicit, 2), inventory,
                new PhysicalReservationSubject.Fungible(Metal.Key, new(1)), new ReservationOwner.ConstructionOrder(new(999))),
        ]));
        Assert.Null(failure);
        Assert.All(result!.Outcomes, outcome =>
        {
            Assert.False(outcome.WasApplied);
            Assert.Equal(InventoryCommitRejectionReason.IdentifierCapacityExhausted,
                Assert.IsType<InventoryMutationOutcome.Rejected>(outcome.Outcome).Reason);
        });
        Assert.Empty(Storage(session).FungibleHoldings);
        Assert.Empty(Storage(session).PhysicalReservations);
        GameSessionCheckpoint after = session.CaptureCheckpoint().Value!;
        Assert.Equal(before.InventoryCommit!.ReservationIds, after.InventoryCommit!.ReservationIds);
        Assert.Equal(before.InventoryCommit.ItemInstanceIds, after.InventoryCommit.ItemInstanceIds);
        Assert.Empty(after.InventoryCommit.Receipts);
        Assert.Null(after.Economy!.PendingWake);
    }

    [Fact]
    public void ResumeRejectsBeforeProviderMutationWhenOnlyOneOfTwoAgendaSlotsRemains()
    {
        using GameSession source = Create();
        BuildFactory(source);
        source.AdvanceTo(new(2_101));
        StationSnapshot station = Current(source);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        Accepted(source, new SetStationModuleEnabledCommand(Principal, station.Id, station.Revision, module.Id, false));
        source.AdvanceTo(source.CurrentTime);
        using GameSession session = AtCreationSequence(source, ulong.MaxValue - 1);
        station = Current(session);
        GameSessionCheckpoint before = session.CaptureCheckpoint().Value!;
        GameplayCommandRecord? result = null;
        Exception? failure = Record.Exception(() => result = session.SubmitCommand(GameSessionTestFixture.Player,
            new SetStationModuleEnabledCommand(Principal, station.Id, station.Revision, module.Id, true)));
        Assert.Null(failure);
        Assert.Equal(CommandResultStatus.Rejected, result!.Result.Status);
        var after = session.CaptureCheckpoint().Value!;
        Assert.False(Assert.Single(Current(session).Modules).Enabled);
        Assert.Equal(station.Revision, Current(session).Revision);
        ProductionJobCheckpoint prior = Assert.Single(Assert.Single(before.Economy!.Production.Lines)!.Jobs)!;
        ProductionJobCheckpoint current = Assert.Single(Assert.Single(after.Economy!.Production.Lines)!.Jobs)!;
        Assert.Equal(prior.Status, current.Status);
        Assert.Equal(prior.Generation, current.Generation);
        Assert.Equal(prior.WorkState, current.WorkState);
        Assert.Equal(before.Engine.Agenda.NextCreationSequence, after.Engine.Agenda.NextCreationSequence);
        Assert.Empty(after.Engine.Agenda.PendingEvents);
    }

    [Fact]
    public void ExistingExactWakeNeedsNoNewAgendaIdentityForAnotherQueuedOrder()
    {
        using GameSession source = Create(shipyard: true);
        BuildFactory(source);
        StationSnapshot station = Current(source);
        StationModuleSnapshot module = Assert.Single(station.Modules);
        Accepted(source, new EnqueueStationShipCommand(Principal, station.Id, station.Revision, module.Id, module.FacilityId!.Value, new(2)));
        using GameSession session = AtCreationSequence(source, ulong.MaxValue);
        var before = session.CaptureCheckpoint().Value!;
        station = Current(session);
        Accepted(session, new EnqueueStationShipCommand(Principal, station.Id, station.Revision, module.Id, module.FacilityId.Value, new(2)));
        var after = session.CaptureCheckpoint().Value!;
        Assert.Equal(before.Economy!.PendingWake, after.Economy!.PendingWake);
        Assert.Equal(ulong.MaxValue, after.Engine.Agenda.NextCreationSequence);
        Assert.Equal(2, Assert.Single(after.Economy.Construction.Processes)!.Orders.Count);
    }

    private static GameSession AtCreationSequence(GameSession source, ulong sequence)
    {
        var checkpoint = source.CaptureCheckpoint().Value!;
        var changed = checkpoint with { Engine = new(checkpoint.Engine.IsInitialized, checkpoint.Engine.AccruedThrough,
            new(checkpoint.Engine.Agenda.CurrentTime, sequence, checkpoint.Engine.Agenda.PendingEvents)) };
        CheckpointResult<GameSession> restored = GameSession.RestoreCheckpoint(changed);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        return restored.Value!;
    }

    private static PrincipalId Principal => GameSessionTestFixture.Principal;
    private static QualifiedContentKey Key(string kind, string id) => QualifiedContentKey.Create("test.provider-session", kind, id);
    private static StationSnapshot Current(GameSession session) => Assert.Single(session.CaptureSnapshot().Stations);
    private static InventoryCheckpoint Storage(GameSession session) => session.CaptureCheckpoint().Value!.Lifecycle.Inventories.Inventories
        .Single(inventory => inventory.Id == Current(session).InventoryId);
    private static void Accepted(GameSession session, GameplayCommand command) =>
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player, command).Result.Status);
    private static void AssignActive(GameSession session)
    {
        StationSnapshot station = Current(session);
        StationProjectSnapshot project = station.Projects.Single(project => project.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled));
        Accepted(session, new AssignStationBuilderCommand(Principal, station.Id, project.Id, station.Revision, new(1), new(0)));
    }
    private static void BuildFactory(GameSession session)
    {
        Accepted(session, new CreateStationSiteCommand(Principal, new(1), Base, GameSessionTestFixture.Position(0, 0), new(0)));
        AssignActive(session);
        session.AdvanceTo(new(1_000));
        StationSnapshot station = Current(session);
        Accepted(session, new ConstructStationModuleCommand(Principal, station.Id, station.Revision, Factory));
        AssignActive(session);
        session.AdvanceTo(new(2_000));
    }
    private static GameSession Create(bool shipyard = false, bool inputs = false)
    {
        var materials = new MaterialInventoryCompatibilityMap([new(new MaterialId(1), Metal)]);
        IEnumerable<KeyValuePair<MaterialId, Quantity>> jobInputs = inputs ? [new(new MaterialId(1), new Quantity(3))] : [];
        var producedShip = new ShipDesign(new(2), "Produced ship", new(jobInputs, new(7)), new(9),
            GameSessionTestFixture.ManeuverCapability, 1_000, 500);
        StationProductionProvider Provider(ulong rate, ulong work = 7) => new(new(jobInputs, new(1), new(1), new(work)), new(rate), false);
        var target = new StationModuleDefinition(Advanced, "Advanced", 1, Quantity.Zero, 0, null, new([], new(1)), new(1), [], [], productionProvider: Provider(5));
        var incompatible = new StationModuleDefinition(Incompatible, "Incompatible", 1, Quantity.Zero, 0, null, new([], new(1)), new(1), [], [], productionProvider: Provider(5, 8));
        var module = new StationModuleDefinition(Factory, "Factory", 1, Quantity.Zero, 0, null, new([], new(1)), new(1), [], [],
            [new(Advanced, new([], new(1))), new(Incompatible, new([], new(1)))], productionProvider: shipyard ? null : Provider(3),
            shipConstructionProvider: shipyard ? new([producedShip], new(3), GameSessionTestFixture.PlayerController, InitialShipOrderPolicy.NoInitialOrder) : null);
        var station = new StationDefinition(Base, "Provider Station", 4, new(100), new(100), new([], new(1)), 100,
            [Factory, Advanced, Incompatible], new(1), []);
        var builder = new ShipDesign(new(1), "Builder", new([], new(1)), new(100), GameSessionTestFixture.ManeuverCapability,
            10_000, 5_000, new StationConstructionCapability(100, new(1)));
        var planner = new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1));
        var economy = new GameSessionEconomySetup([], [], [], [], [], [], [],
            new HierarchicalLogisticsNavigation(new Dictionary<LocationId, SystemPosition>(), planner), new(new(1), new(1), new(1)), materials);
        var cargo = new CargoDispositionPolicy(Key("cargo-policy", "default"), 1, 1, 1, 1,
            new Dictionary<CargoLossCause, CargoRecoveryFraction>
            {
                [CargoLossCause.EntityDestruction] = new(1, 2), [CargoLossCause.StorageModuleLoss] = new(1, 2),
                [CargoLossCause.FunctionalEjection] = new(1, 1),
            }, 1, 0, new(60_000), new(1), new(1), new(1_000));
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")],
            [new InitialShipSetup(new(1), new(1), new(1), Principal, builder, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []), [], economy, GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 256,
            GameSessionTestFixture.Player, cargoPolicy: cargo,
            stations: new(new([station], [module, target, incompatible]), materials, StationTerritoryPolicy.Absent,
                storageLossPolicy: new StationStorageLossPolicy(Key("station-storage-loss-policy", "default"), 1, 1, 100)));
        return new(setup, planner);
    }
}
