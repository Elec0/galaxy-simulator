using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Diagnostics;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

/// <summary>
/// Synthetic shared-owner and collection-order workloads. These fixtures do not
/// activate station storage accounting or establish live station integration.
/// </summary>
internal sealed partial class CargoComponentBenchmarkScenario : IBenchmarkScenario
{
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("benchmark.cargo", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(1));
    private static readonly PhysicalDefinition Item = new(QualifiedContentKey.Create("benchmark.cargo", "equipment", "item"), PhysicalHoldingKind.Discrete, new(1));
    private static readonly ShipManeuverCapability Capability = ScenarioSetup.CreateShips(1, 1)[0].Design.ManeuverCapability;
    private static readonly ActorController Controller = new(ActorControllerKind.Autonomous, ScenarioSetup.AutonomousSource.Id);
    private static readonly CommandSource Player = new(CommandSourceKind.Player, new CommandSourceId("benchmark-player"));

    /// <summary>Runs bounded deterministic work; execution knobs affect only existing immutable interaction evaluation batches.</summary>
    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration)
    {
        BenchmarkScenarioValidator.Validate(configuration);
        return configuration.BasePreset is BenchmarkPresets.CargoComponentContestedSerial or BenchmarkPresets.CargoComponentContestedBatched
            ? RunContested(configuration) : RunLosses(configuration);
    }

    /// <summary>Measures shared disposition and expiry over separate circles or an exact touching chain.</summary>
    private static ScenarioCorrectnessResult RunLosses(ResolvedBenchmarkScenario configuration)
    {
        int count = configuration.GetInt32(BenchmarkParameterNames.LossOccurrenceCount);
        ulong units = configuration.GetUInt64(BenchmarkParameterNames.CargoUnitsPerLoss);
        bool chain = configuration.BasePreset == BenchmarkPresets.CargoComponentOverlapChain;
        var movement = new SpatialMovement();
        ShipDesign design = Design(1, units);
        var lifecycle = new EntityLifecycleOwner(movement, new ActorControlRegistry(), new ShipOrderCoordinator(), [], shipDefinitions: [design]);
        InitialShipSetup[] sources = Enumerable.Range(0, count).Select(index =>
        {
            ulong id = (ulong)index + 1;
            return new InitialShipSetup(new(id), new(id), new(id), new(1), design,
                Position(index * (chain ? 20000L : 30000L)), Controller);
        }).ToArray();
        lifecycle.RegisterSetup(sources);
        foreach (InitialShipSetup source in sources)
            Require(lifecycle.Inventories.Get(source.CargoInventoryId)!.StoreFungible(Ore, new(units)).IsAccepted);
        var agenda = new EventAgenda<GameEvent>();
        var facts = new GameFactStore(256);
        CargoDispositionPolicy policy = Policy(10000, 1000);
        var random = new DeterministicRandomOwner(Root(configuration));
        var cargo = new CargoDispositionOwner(lifecycle, new([Ore]), policy, random, agenda, facts);
        var measurements = new DomainMeasurements();
        measurements.Measure("cargo.disposition", () =>
        {
            foreach (InitialShipSetup source in sources)
            {
                var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(source.Id.Value)), source.EntityId, 0),
                    source.CargoInventoryId, source.Position, CargoLossCause.StorageModuleLoss, policy,
                    [new CargoDispositionSelection.Fungible(Ore.Key, new(units))], []);
                CargoDispositionPreparation prepared = cargo.Prepare(request);
                Require(prepared.Prepared is not null && cargo.Commit(prepared.Prepared).WasApplied);
            }
        });
        CargoDispositionOwnerCheckpoint published = cargo.CaptureCheckpoint().Value!;
        var digest = new DeterministicDigest();
        AddParameters(digest, configuration);
        AddAuthority(digest, published, lifecycle.CaptureCheckpoint(), agenda.CaptureCheckpoint().Value!, facts.CaptureCheckpoint());
        AddRandom(digest, random.CaptureCheckpoint());
        long expired = 0;
        ulong through = configuration.GetUInt64(BenchmarkParameterNames.SimulatedDurationMilliseconds);
        measurements.Measure("cargo.expiry", () =>
        {
            while (agenda.PopNextThrough(new(through)) is { } scheduled)
            {
                Require(cargo.Expire(scheduled) == ScheduledEventDisposition.Applied);
                expired++;
            }
        });
        CargoDispositionOwnerCheckpoint final = cargo.CaptureCheckpoint().Value!;
        EntityLifecycleCheckpoint finalLife = lifecycle.CaptureCheckpoint();
        AddAuthority(digest, final, finalLife, agenda.CaptureCheckpoint().Value!, facts.CaptureCheckpoint());
        AddRandom(digest, random.CaptureCheckpoint());
        var counts = RetentionCounts(final, agenda.Count);
        counts["retainedFacts"] = facts.CaptureCheckpoint().RetainedFacts.Count;
        counts["eventsProcessed"] = expired;
        // This owner-only fixture dispatches agenda entries directly and submits no session commands.
        counts["commandsSubmitted"] = 0;
        counts["lossOccurrences"] = count;
        counts["objectsPublished"] = published.Objects.Count;
        counts["objectsExpired"] = expired;
        counts["fieldsAtPublication"] = published.Fields.Fields.Count;
        counts["circlesAtPublication"] = published.Fields.Fields.Sum(field => (long)field.Circles.Count);
        counts["recoverableUnits"] = published.Receipts.Sum(receipt => checked((long)receipt.Outcomes.Single().Recoverable.Units));
        counts["destroyedUnits"] = published.Receipts.Sum(receipt => checked((long)receipt.Outcomes.Single().Destroyed.Units));
        counts["sourceRemainingUnits"] = sources.Sum(source => checked((long)lifecycle.Inventories.Get(source.CargoInventoryId)!.FungibleStored(Ore.Key).Units));
        return new(digest.ToString(), through, counts, measurements.Snapshot());
    }

    /// <summary>
    /// Creates one crowded field and concurrent timers on one object. Earlier
    /// accepted commands deliberately belong to higher ship IDs; timer allocation
    /// order must therefore remain independent from the winning collector.
    /// </summary>
    private static ScenarioCorrectnessResult RunContested(ResolvedBenchmarkScenario configuration)
    {
        int count = configuration.GetInt32(BenchmarkParameterNames.CollectorCount);
        int initialItems = checked(count * 2);
        ShipDesign sourceDesign = Design(1, (ulong)initialItems);
        ShipDesign collectorDesign = Design(2, 1);
        var ships = new List<InitialShipSetup>
        {
            new(new(1), new(1), new(1), new(1), sourceDesign, Position(0), Controller, cargo: Enumerable.Repeat(Item, initialItems)),
        };
        for (ulong id = 2; id <= (ulong)count + 1; id++)
            ships.Add(new(new(id), new(id), new(id), new(1), collectorDesign, Position(0), Controller));
        CargoDispositionPolicy policy = Policy(0, 20000);
        var setup = new GameSessionSetup([new StarSystem(new(1), "Cargo component system")], ships, new ConnectorTopology([], []), [], null,
            ScenarioSetup.Relationships, Root(configuration), configuration.GetInt32(BenchmarkParameterNames.FactRetentionCapacity), Player, cargoPolicy: policy);
        var navigation = new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1));
        using GameSession session = configuration.BasePreset == BenchmarkPresets.CargoComponentContestedBatched
            ? new(setup, navigation, new InteractionBatchExecution(configuration.GetInt32(BenchmarkParameterNames.WorkerCount), configuration.GetInt32(BenchmarkParameterNames.BatchSize)))
            : new(setup, navigation);
        var measurements = new DomainMeasurements();
        measurements.Measure("cargo.disposition", () =>
        {
            ScenarioSetup.RequireAccepted(session.SubmitCommand(ScenarioSetup.AutonomousSource,
                new MoveShipCommand(new(1), new NavigationDestination.Position(Position(0)), OrderPlacement.ReplaceAll)), configuration.Id);
            var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1), Position(0),
                CargoLossCause.StorageModuleLoss, policy, Enumerable.Range(1, initialItems)
                    .Select(id => (CargoDispositionSelection)new CargoDispositionSelection.Discrete(Item.Key, new((ulong)id))).ToArray(), []);
            Require(session.CommitCargoDisposition(request).WasApplied);
        });
        CargoObjectView target = session.CaptureVisibleCargo()[0];
        GameSessionCheckpoint? started = null;
        ulong through = configuration.GetUInt64(BenchmarkParameterNames.SimulatedDurationMilliseconds);
        measurements.Measure("cargo.collection", () =>
        {
            for (int id = count + 1; id >= 2; id--)
                ScenarioSetup.RequireAccepted(session.SubmitCommand(ScenarioSetup.AutonomousSource,
                    new CollectCargoObjectCommand(new((ulong)id), target.EntityId, OrderPlacement.ReplaceAll)), configuration.Id);
            session.AdvanceTo(new(0));
            started = session.CaptureCheckpoint().Value!;
            session.AdvanceTo(new(through));
        });
        GameSessionCheckpoint final = session.CaptureCheckpoint().Value!;
        var baseResult = ScenarioResult.Create(session, configuration);
        var digest = new DeterministicDigest();
        AddParameters(digest, configuration);
        digest.Add(baseResult.Digest);
        AddAuthority(digest, started!.Cargo!, started.Lifecycle, started.Engine.Agenda, started.Facts);
        AddOrdersAndAttempts(digest, started);
        AddAuthority(digest, final.Cargo!, final.Lifecycle, final.Engine.Agenda, final.Facts);
        AddOrdersAndAttempts(digest, final);
        AddSequence(digest, final.InventoryCommit!.ItemInstanceIds);
        AddSequence(digest, final.InventoryCommit.ReservationIds);
        AddSequence(digest, final.CommandAdmission.Sequences);
        var counts = RetentionCounts(final.Cargo!, final.Engine.Agenda.PendingEvents.Count);
        counts["retainedFacts"] = final.Facts.RetainedFacts.Count;
        counts["eventsProcessed"] = session.EventRecords.Count;
        counts["commandsSubmitted"] = session.CommandRecords.Count;
        counts["lossOccurrences"] = 1;
        counts["objectsPublished"] = count;
        counts["fieldsAtPublication"] = 1;
        counts["circlesAtPublication"] = 1;
        counts["timersStarted"] = started.CargoCollectionAttempts!.Attempts.Count;
        var collectorOrders = final.Orders.Actors.Where(actor => actor!.ShipId.Value != 1).Select(actor => actor!.Base!.LastTerminal!).ToArray();
        counts["ordersCompleted"] = collectorOrders.LongCount(order => order.Status == ShipOrderStatus.Completed);
        counts["ordersFailed"] = collectorOrders.LongCount(order => order.Status == ShipOrderStatus.Failed);
        counts["winningShip"] = checked((long)final.Lifecycle.Inventories.Inventories.Single(inventory =>
            inventory.Custody is InventoryCustody.PrincipalControlled && inventory.DiscreteItems.Count == 1).Id.Value);
        counts["objectsRemaining"] = final.Cargo!.Objects.Count;
        counts["incomingReservations"] = final.Lifecycle.Inventories.Inventories.Sum(inventory => (long)inventory.PhysicalReservations.Count);
        return new(digest.ToString(), through, counts, measurements.Snapshot());
    }

    private static ShipDesign Design(ulong id, ulong capacity) => new(new(id), "Cargo component ship", new ConstructionRecipe([], new Work(1)), new(capacity), Capability, 10000, 5000);

    private static CargoDispositionPolicy Policy(ulong radius, ulong lifetime) => new(QualifiedContentKey.Create("benchmark.cargo", "cargo-policy", "component"),
        1, 1, 1, 1, [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
        4, radius, new(lifetime), new(1000), new(15000), new(100));

    /// <summary>Derives fixture entropy exclusively from the fixed workload seed parameter.</summary>
    private static RandomRootSeed Root(ResolvedBenchmarkScenario configuration)
    {
        byte[] seed = new byte[RandomRootSeed.ByteCount];
        BinaryPrimitives.WriteInt64LittleEndian(seed, configuration.Get(BenchmarkParameterNames.Seed));
        return RandomRootSeed.FromBytes(seed);
    }

    private static SystemPosition Position(long x) => new(new(1), new(new(x), new(0)));

    /// <summary>Retains semantic workload configuration while excluding only execution and measurement tuning.</summary>
    private static void AddParameters(DeterministicDigest digest, ResolvedBenchmarkScenario configuration)
    {
        foreach (var parameter in configuration.Parameters.Where(pair => pair.Key is not ("workerCount" or "batchSize" or "warmupIterations" or "measuredIterations")))
        { digest.Add(parameter.Key); digest.Add(parameter.Value); }
    }

    private static SortedDictionary<string, long> RetentionCounts(CargoDispositionOwnerCheckpoint cargo, int agenda) => new(StringComparer.Ordinal)
    {
        ["retainedCircles"] = cargo.Fields.Fields.Sum(field => (long)field.Circles.Count),
        ["retainedAliases"] = cargo.Fields.Aliases.Count,
        ["retainedTombstones"] = cargo.Fields.Tombstones.Count,
        ["retainedReceipts"] = cargo.Receipts.Count,
        ["agendaEntries"] = agenda,
    };

    /// <summary>Hashes complete cargo identity, conservation, field, inventory and pending-work authority in canonical owner order.</summary>
    private static void AddAuthority(DeterministicDigest digest, CargoDispositionOwnerCheckpoint cargo, EntityLifecycleCheckpoint life,
        EventAgendaCheckpoint<GameEvent> agenda, GameFactStoreCheckpoint facts)
    {
        digest.Add(cargo.PolicyKey.ToString());
        AddSequence(digest, life.EntityIds); AddSequence(digest, life.InventoryIds); AddSequence(digest, life.ShipIds);
        digest.Add(life.LiveShips.Count);
        foreach (EntityLifecycleShipCheckpoint ship in life.LiveShips.Select(value => value!))
        { digest.Add(ship.EntityId.Value); digest.Add(ship.ShipId.Value); digest.Add(ship.PrincipalId.Value); digest.Add(ship.DesignId.Value); digest.Add(ship.CargoInventoryId.Value); digest.Add(ship.ManeuverCapabilityRevision.Value); }
        digest.Add(cargo.Receipts.Count);
        foreach (CargoDispositionReceipt receipt in cargo.Receipts)
        {
            AddRequest(digest, receipt.Request);
            digest.Add(receipt.CommittedAt.Milliseconds); digest.Add(receipt.FieldHandle?.Value ?? 0);
            digest.Add(receipt.Outcomes.Count);
            foreach (CargoDispositionTypeOutcome outcome in receipt.Outcomes)
            {
                digest.Add(outcome.DefinitionKey.ToString()); digest.Add((ulong)outcome.Kind);
                digest.Add(outcome.Removed.Units); digest.Add(outcome.Destroyed.Units); digest.Add(outcome.Recoverable.Units);
                digest.Add(outcome.SurvivingInstances.Count);
                foreach (ItemInstanceId item in outcome.SurvivingInstances) digest.Add(item.Value);
            }
            digest.Add(receipt.ObjectIds.Count);
            foreach (EntityId id in receipt.ObjectIds) digest.Add(id.Value);
        }
        digest.Add(cargo.Objects.Count);
        foreach (CargoLiveObject value in cargo.Objects)
        {
            digest.Add(value.EntityId.Value); AddCause(digest, value.Origin); digest.Add(value.CreatedAt.Milliseconds); digest.Add(value.ExpiresAt.Milliseconds);
            digest.Add(value.PolicyKey.ToString()); AddEventKey(digest, value.ExpiryEvent); digest.Add(value.Generation.Value); digest.Add(value.OriginalFieldId?.Value ?? 0);
        }
        AddSequence(digest, cargo.Fields.FieldIds); digest.Add(cargo.Fields.Revision);
        digest.Add(cargo.Fields.Fields.Count);
        foreach (CargoDebrisFieldSnapshot field in cargo.Fields.Fields)
        {
            digest.Add(field.Id.Value);
            digest.Add(field.Circles.Count);
            foreach (CargoDebrisCircle circle in field.Circles)
            { digest.Add(circle.OriginalFieldId.Value); AddPosition(digest, circle.Center); digest.Add(circle.RadiusMeters); }
            digest.Add(field.Members.Count);
            foreach (EntityId member in field.Members) digest.Add(member.Value);
        }
        digest.Add(cargo.Fields.Aliases.Count);
        foreach (CargoDebrisAlias alias in cargo.Fields.Aliases) { digest.Add(alias.Alias.Value); digest.Add(alias.Canonical.Value); }
        digest.Add(cargo.Fields.Tombstones.Count);
        foreach (DebrisFieldId tombstone in cargo.Fields.Tombstones) digest.Add(tombstone.Value);
        digest.Add(life.CargoObjects.Count);
        foreach (CargoObjectSnapshot physical in life.CargoObjects.Select(value => value!))
        { digest.Add(physical.EntityId.Value); digest.Add(physical.InventoryId.Value); AddPosition(digest, physical.Position); }
        digest.Add(life.Inventories.Inventories.Count);
        foreach (InventoryCheckpoint inventory in life.Inventories.Inventories)
        {
            digest.Add(inventory.Id.Value); digest.Add(inventory.Capacity.Units); digest.Add(inventory.Custody is InventoryCustody.Unclaimed);
            digest.Add(inventory.EnabledIntakeCapacity.Units);
            digest.Add(inventory.ProtectedIntakeCapacity is not null);
            if (inventory.ProtectedIntakeCapacity is { } protectedCapacity) digest.Add(protectedCapacity.Units);
            digest.Add(inventory.NextIncomingAdmissionSequence is not null);
            if (inventory.NextIncomingAdmissionSequence is { } nextIncoming) digest.Add(nextIncoming);
            digest.Add(inventory.IncomingAdmissions.Count);
            foreach (InventoryIncomingAdmissionCheckpoint admission in inventory.IncomingAdmissions)
            { digest.Add((int)admission.Kind); digest.Add(admission.ReservationValue); digest.Add(admission.AdmissionSequence); }
            digest.Add(inventory.NextOutgoingAdmissionSequence is not null);
            if (inventory.NextOutgoingAdmissionSequence is { } nextOutgoing) digest.Add(nextOutgoing);
            digest.Add(inventory.OutgoingAdmissions.Count);
            foreach (InventoryOutgoingAdmissionCheckpoint admission in inventory.OutgoingAdmissions)
            { digest.Add((int)admission.Kind); digest.Add(admission.ReservationValue); digest.Add(admission.AdmissionSequence); }
            digest.Add(inventory.Custody?.ControllingPrincipalId?.Value ?? 0);
            if (inventory.Custody?.PhysicalOwner is InventoryOwnerReference.SessionEntity ownerEntity) digest.Add(ownerEntity.EntityId.Value);
            else throw new InvalidOperationException("Unexpected cargo fixture inventory owner.");
            Require(inventory.StoredMaterials.Count == 0 && inventory.Reservations.Count == 0 && inventory.CapacityReservations.Count == 0);
            digest.Add(inventory.FungibleHoldings.Count);
            foreach (InventoryFungibleCheckpoint holding in inventory.FungibleHoldings)
            { digest.Add(holding.DefinitionKey.ToString()); digest.Add(holding.Quantity.Units); }
            digest.Add(inventory.DiscreteItems.Count);
            foreach (InventoryDiscreteItemCheckpoint item in inventory.DiscreteItems)
            { digest.Add(item.Id.Value); digest.Add(item.DefinitionKey.ToString()); }
            digest.Add(inventory.PhysicalReservations.Count);
            foreach (PhysicalReservation reservation in inventory.PhysicalReservations)
            {
                digest.Add(reservation.Id.Value); digest.Add(reservation.InventoryId.Value);
                var owner = (ReservationOwner.CargoCollection)reservation.Owner;
                digest.Add(owner.ShipId.Value); digest.Add(owner.OrderId.Value); digest.Add(owner.AttemptGeneration.Value);
                digest.Add(((PhysicalReservationSubject.IncomingCapacity)reservation.Subject).Quantity.Units);
            }
        }
        digest.Add(agenda.CurrentTime.Milliseconds); digest.Add(agenda.NextCreationSequence);
        digest.Add(agenda.PendingEvents.Count);
        foreach (ScheduledEvent<GameEvent> entry in agenda.PendingEvents)
        {
            AddEventKey(digest, entry.Key); digest.Add(entry.Generation.Value); digest.Add(entry.Payload.GetType().Name);
            switch (entry.Payload)
            {
                case GameEvent.CargoExpiry expiry: digest.Add(expiry.ObjectId.Value); break;
                case GameEvent.CargoCollectionCompletion completion: digest.Add(completion.ShipId.Value); digest.Add(completion.OrderId.Value); digest.Add(completion.AttemptGeneration.Value); break;
                case GameEvent.CargoCollectionRangeExit exit: digest.Add(exit.ShipId.Value); digest.Add(exit.OrderId.Value); digest.Add(exit.AttemptGeneration.Value); break;
                case GameEvent.CargoCollectionReevaluation wake:
                    digest.Add(wake.Work.Count);
                    foreach (CargoCollectionWake work in wake.Work) { digest.Add(work.ShipId.Value); digest.Add(work.OrderId.Value); digest.Add(work.AcceptedCommand.Value); }
                    break;
                default: throw new InvalidOperationException($"Unexpected cargo fixture event {entry.Payload.GetType().Name}.");
            }
        }
        digest.Add(facts.Capacity);
        AddSequence(digest, facts.Sequences);
        digest.Add(facts.RetainedFacts.Count);
        foreach (GameFactEnvelope fact in facts.RetainedFacts.Select(value => value!))
        {
            AddFact(digest, fact);
        }
    }

    /// <summary>Hashes original admitted order identities, attempt timing/reservations and retained generation history.</summary>
    private static void AddOrdersAndAttempts(DeterministicDigest digest, GameSessionCheckpoint checkpoint)
    {
        AddRandom(digest, checkpoint.Random!);
        AddSequence(digest, checkpoint.Orders.OrderIds);
        digest.Add(checkpoint.Orders.Actors.Count);
        foreach (ShipActorOrdersCheckpoint actor in checkpoint.Orders.Actors.Select(value => value!))
        {
            digest.Add(actor.ShipId.Value);
            foreach (ShipOrderWorkSetCheckpoint? work in new[] { actor.Base, actor.Override })
            {
                digest.Add(work is not null);
                if (work is null) continue;
                digest.Add(work.Queue.Count);
                foreach (ShipOrderCheckpoint? order in work.Queue.Append(work.Active).Append(work.LastTerminal))
                {
                    digest.Add(order is not null);
                    if (order is null) continue;
                    digest.Add(order.Id.Value); digest.Add((ulong)order.Status!.Value); digest.Add((ulong)order.Reason!.Value);
                    AddSource(digest, order.Source!); digest.Add((ulong)order.Kind);
                    digest.Add(order.RequestedHeading is not null); if (order.RequestedHeading is { } heading) digest.Add(heading.Millidegrees);
                    Require(order.Plan is null);
                    AddDestination(digest, order.Destination!);
                    digest.Add(order.NextLegIndex); digest.Add(order.MotionId?.Value ?? 0); digest.Add(order.TransitId?.Value ?? 0);
                    AddAction(digest, order.Action);
                }
            }
        }
        digest.Add(checkpoint.CargoCollectionAttempts!.Generations.Count);
        foreach (CargoCollectionGenerationCheckpoint row in checkpoint.CargoCollectionAttempts!.Generations.Select(value => value!))
        { digest.Add(row.ShipId.Value); digest.Add(row.Generation.Value); }
        digest.Add(checkpoint.CargoCollectionAttempts.Attempts.Count);
        foreach (CargoCollectionAttempt attempt in checkpoint.CargoCollectionAttempts.Attempts.Select(value => value!))
        {
            digest.Add(attempt.ShipId.Value); digest.Add(attempt.OrderId.Value); digest.Add(attempt.AcceptedCommand.Value); digest.Add(attempt.Generation.Value);
            digest.Add(attempt.TargetId.Value); AddPosition(digest, attempt.TargetPosition); digest.Add(attempt.DefinitionKey.ToString());
            digest.Add(attempt.UnitCapacityCost.Units); digest.Add(attempt.ReservedQuantity.Units); digest.Add(attempt.CapacityReservationId.Value);
            digest.Add(attempt.StartedAt.Milliseconds); digest.Add(attempt.StartStorage.Units); digest.Add(attempt.DueAt.Milliseconds); AddEventKey(digest, attempt.CompletionKey);
            digest.Add(attempt.ExitKey is not null); if (attempt.ExitKey is { } exit) AddEventKey(digest, exit);
            digest.Add(attempt.MotionRevision.Value); digest.Add(attempt.CapabilityRevision.Value); digest.Add(attempt.PickupRange);
        }
        digest.Add(checkpoint.CargoCollectionTargets!.Count);
        foreach (CargoCollectionTarget target in checkpoint.CargoCollectionTargets!)
        { digest.Add(target.ShipId.Value); digest.Add(target.OrderId.Value); digest.Add(target.TargetId.Value); AddPosition(digest, target.Anchor); digest.Add(target.NeedsSelection); }
    }

    /// <summary>Keeps command and scheduled causes in distinct typed digest namespaces.</summary>
    private static void AddCause(DeterministicDigest digest, CargoDispositionKey key)
    {
        digest.Add(key.Trigger is CargoDispositionTrigger.Command);
        if (key.Trigger is CargoDispositionTrigger.Command command) digest.Add(command.Sequence.Value);
        else AddEventKey(digest, ((CargoDispositionTrigger.Scheduled)key.Trigger).Key);
        digest.Add(key.SourceEntityId.Value); digest.Add(key.OccurrenceOrdinal);
    }

    /// <summary>Retains exact behavior versions and rational parameters even when one workload does not exercise every branch.</summary>
    private static void AddPolicy(DeterministicDigest digest, CargoDispositionPolicy policy)
    {
        digest.Add(policy.Key.ToString()); digest.Add(policy.GenerationBehaviorVersion); digest.Add(policy.MergeBehaviorVersion);
        digest.Add(policy.OrderingBehaviorVersion); digest.Add(policy.DurationBehaviorVersion); digest.Add(policy.FungibleObjectCount);
        digest.Add(policy.ScatterRadiusMeters); digest.Add(policy.Lifetime.Milliseconds); digest.Add(policy.MinimumCollectionDuration.Milliseconds);
        digest.Add(policy.MaximumCollectionDuration.Milliseconds); digest.Add(policy.CollectionStorageCap.Units);
        foreach (var cause in policy.RecoveryByCause)
        { digest.Add((ulong)cause.Key); digest.Add(cause.Value.Numerator); digest.Add(cause.Value.Denominator); }
    }
    private static void AddEventKey(DeterministicDigest digest, EventKey key) { digest.Add(key.Timestamp.Milliseconds); digest.Add((ulong)key.Phase); digest.Add(key.CreationSequence); }
    private static void AddCargoView(DeterministicDigest digest, CargoObjectView view)
    {
        digest.Add(view.EntityId.Value); AddPosition(digest, view.Position); digest.Add(view.DefinitionKey.ToString()); digest.Add((ulong)view.HoldingKind);
        digest.Add(view.Quantity.Units); digest.Add(view.UnitCapacityCost.Units); digest.Add(view.OccupiedStorage.Units);
        digest.Add(view.DiscreteItem?.Id.Value ?? 0); digest.Add(view.DiscreteItem?.DefinitionKey.ToString() ?? "none"); digest.Add(view.OriginalFieldId?.Value ?? 0);
    }
    private static void AddPosition(DeterministicDigest digest, SystemPosition position) { digest.Add(position.SystemId.Value); digest.Add(position.Position.X.Units); digest.Add(position.Position.Y.Units); }
    private static void AddSequence(DeterministicDigest digest, IdSequenceCheckpoint sequence) { digest.Add(sequence.NextValue is not null); digest.Add(sequence.NextValue ?? 0); }

    /// <summary>Turns a workload correctness mismatch into an enforced benchmark failure, independent of timing.</summary>
    private static void Require(bool valid)
    {
        if (!valid) throw new InvalidOperationException("Cargo component benchmark invariant failed.");
    }

    private sealed class DomainMeasurements
    {
        private readonly SortedDictionary<string, BenchmarkDomainMeasurement> _values = new(StringComparer.Ordinal);

        /// <summary>Measures process allocation and elapsed domain work without mixing either into authoritative correctness.</summary>
        internal void Measure(string name, Action work)
        {
            long allocated = GC.GetTotalAllocatedBytes(false);
            long started = Stopwatch.GetTimestamp();
            work();
            _values.Add(name, new(Stopwatch.GetElapsedTime(started).TotalMilliseconds, checked(GC.GetTotalAllocatedBytes(false) - allocated)));
        }
        internal ReadOnlyDictionary<string, BenchmarkDomainMeasurement> Snapshot() => new(_values);
    }
}
