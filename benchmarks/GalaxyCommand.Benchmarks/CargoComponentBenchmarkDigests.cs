using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

internal sealed partial class CargoComponentBenchmarkScenario
{
    /// <summary>Retains the entire causal body, including exact authorized reservation cleanup.</summary>
    internal static void AddRequest(DeterministicDigest digest, CargoDispositionRequest request)
    {
        AddCause(digest, request.Key); digest.Add(request.SourceInventoryId.Value); AddPosition(digest, request.SourcePosition);
        digest.Add((ulong)request.Cause); AddPolicy(digest, request.Policy); digest.Add(request.Selected.Count);
        foreach (CargoDispositionSelection selection in request.Selected)
        {
            digest.Add(selection.DefinitionKey.ToString()); digest.Add(selection.GetType().Name);
            switch (selection)
            {
                case CargoDispositionSelection.Fungible fungible: digest.Add(fungible.Quantity.Units); break;
                case CargoDispositionSelection.Discrete discrete: digest.Add(discrete.InstanceId.Value); break;
                default: throw Unexpected(selection);
            }
        }
        digest.Add(request.Cleanup.Count);
        foreach (CargoSourceReservationCleanup cleanup in request.Cleanup)
        {
            digest.Add(cleanup.ReservationId.Value);
            digest.Add(cleanup switch { CargoSourceReservationCleanup.Physical => "physical", CargoSourceReservationCleanup.Legacy => "legacy", _ => throw Unexpected(cleanup) });
            AddReservationOwner(digest, cleanup.Owner);
        }
    }

    /// <summary>Closed fixture vocabulary fails loudly when new facts would otherwise disappear from correctness evidence.</summary>
    internal static void AddFact(DeterministicDigest digest, GameFactEnvelope fact)
    {
        digest.Add(fact.Sequence.Value); digest.Add(fact.Timestamp.Milliseconds); digest.Add(fact.Cause.GetType().Name);
        switch (fact.Cause)
        {
            case CommandFactCause command: digest.Add(command.Sequence.Value); break;
            case ScheduledEventFactCause scheduled: AddEventKey(digest, scheduled.Key); break;
            default: throw Unexpected(fact.Cause);
        }
        AddFactPayload(digest, fact.Fact);
        digest.Add(fact.PlayerDisclosure is not null);
        if (fact.PlayerDisclosure is not { } disclosure) return;
        digest.Add(disclosure.ObserverPrincipalId.Value); digest.Add(disclosure.SubjectPrincipalId?.Value ?? 0);
        digest.Add(disclosure.Fact is not null);
        if (disclosure.Fact is { } visible) AddFactPayload(digest, visible);
    }

    private static void AddFactPayload(DeterministicDigest digest, GameFact fact)
    {
        digest.Add(fact.GetType().Name);
        switch (fact)
        {
            case CommandAcceptedFact accepted:
                digest.Add(accepted.CommandSequence.Value); AddSource(digest, accepted.Source); digest.Add(accepted.CommandKind); break;
            case ShipOrderTransitionFact transition:
                digest.Add(transition.ShipId.Value); digest.Add(transition.OrderId.Value); AddSource(digest, transition.Source);
                AddDestination(digest, transition.Destination); digest.Add(transition.PreviousStatus is not null);
                if (transition.PreviousStatus is { } previous) digest.Add((ulong)previous);
                digest.Add((ulong)transition.NextStatus); digest.Add((ulong)transition.Reason);
                digest.Add(transition.RequestedHeading is not null); if (transition.RequestedHeading is { } heading) digest.Add(heading.Millidegrees);
                AddAction(digest, transition.Action); break;
            case CargoDispositionCommittedFact committed: AddCause(digest, committed.Key); break;
            case CargoObjectExpiredFact expired: digest.Add(expired.EntityId.Value); AddCause(digest, expired.Origin); break;
            case CargoObjectAppearedFact appeared: AddCargoView(digest, appeared.Object); break;
            case CargoObjectDisappearedFact disappeared: digest.Add(disappeared.EntityId.Value); AddPosition(digest, disappeared.Position); break;
            case CargoCollectedFact collected:
                digest.Add(collected.ShipId.Value); digest.Add(collected.CollectorEntityId.Value); digest.Add(collected.OrderId.Value);
                digest.Add(collected.AcceptedCommand.Value); digest.Add(collected.AttemptGeneration.Value); digest.Add(collected.TargetId.Value);
                digest.Add(collected.Outcome.DefinitionKey.ToString()); digest.Add(collected.Outcome.Quantity.Units);
                digest.Add(collected.Outcome.InstanceId?.Value ?? 0); digest.Add(collected.Outcome.Retired); break;
            case ObservedCargoAppearanceFact appearance: AddCargoView(digest, appearance.Cargo); break;
            case ObservedCargoDisappearanceFact disappearance: digest.Add(disappearance.EntityId.Value); AddPosition(digest, disappearance.Position); break;
            case PlayerCargoCollectedFact collected:
                digest.Add(collected.ShipId.Value); digest.Add(collected.OrderId.Value); digest.Add(collected.DefinitionKey.ToString());
                digest.Add(collected.Quantity.Units); digest.Add(collected.InstanceId?.Value ?? 0); break;
            case PlayerCommandOutcomeFact outcome:
                digest.Add(outcome.CommandSequence.Value); digest.Add(outcome.CommandKind); digest.Add((ulong)outcome.Status);
                digest.Add(outcome.RejectionCode?.Value ?? "none"); break;
            default: throw Unexpected(fact);
        }
    }

    /// <summary>Includes derivation identity and all persistent stream words, even for currently unused streams.</summary>
    internal static void AddRandom(DeterministicDigest digest, DeterministicRandomCheckpoint random)
    {
        digest.Add(random.RootSeed.Count);
        foreach (byte value in random.RootSeed) digest.Add(value);
        digest.Add(random.DerivationAlgorithm ?? "none"); digest.Add(random.DerivationVersion); digest.Add(random.Streams.Count);
        foreach (RandomStreamCheckpoint stream in random.Streams.Select(value => value!))
        {
            RandomStreamKey key = stream.Key!;
            digest.Add((ulong)key.Scope); digest.Add(key.DomainKind); digest.Add(key.OwnerKind); digest.Add(key.OwnerId); digest.Add(key.PurposeId);
            digest.Add(stream.GeneratorAlgorithm ?? "none"); digest.Add(stream.GeneratorVersion);
            digest.Add(stream.S0); digest.Add(stream.S1); digest.Add(stream.S2); digest.Add(stream.S3);
            digest.Add(stream.NextPosition is not null); digest.Add(stream.NextPosition ?? 0);
        }
    }

    private static void AddSource(DeterministicDigest digest, CommandSource source)
    { digest.Add((ulong)source.Kind); digest.Add(source.Id.Value); }

    private static void AddAction(DeterministicDigest digest, ShipActionIntent? action)
    {
        digest.Add(action is not null);
        if (action is null) return;
        digest.Add(action.OriginCommandSequence.Value); digest.Add(action.CargoInventoryId.Value); digest.Add((ulong)action.Kind);
        if (action is not ShipActionIntent.Cargo cargo) throw Unexpected(action);
        digest.Add(cargo.LastObservedObjectEntityId.Value); AddPosition(digest, cargo.LastObservedPosition);
        switch (cargo)
        {
            case ShipActionIntent.CollectObject collect: digest.Add(collect.TargetEntityId.Value); break;
            case ShipActionIntent.CollectField field:
                digest.Add(field.TargetFieldId.Value); digest.Add(field.Priorities.Count);
                foreach (var key in field.Priorities) digest.Add(key.ToString());
                break;
            default: throw Unexpected(cargo);
        }
    }

    private static void AddDestination(DeterministicDigest digest, NavigationDestination destination)
    {
        if (destination is not NavigationDestination.Position position) throw Unexpected(destination);
        AddPosition(digest, position.Value);
    }

    private static void AddReservationOwner(DeterministicDigest digest, ReservationOwner owner)
    {
        digest.Add(owner.GetType().Name);
        switch (owner)
        {
            case ReservationOwner.TransportJob transport: digest.Add(transport.JobId.Value); break;
            case ReservationOwner.ProductionJob production: digest.Add(production.JobId.Value); break;
            case ReservationOwner.ConstructionOrder construction: digest.Add(construction.OrderId.Value); break;
            case ReservationOwner.CargoCollection collection:
                digest.Add(collection.ShipId.Value); digest.Add(collection.OrderId.Value); digest.Add(collection.AttemptGeneration.Value); break;
            default: throw Unexpected(owner);
        }
    }

    private static InvalidOperationException Unexpected(object value) => new($"Unexpected cargo benchmark payload {value.GetType().Name}.");
}
