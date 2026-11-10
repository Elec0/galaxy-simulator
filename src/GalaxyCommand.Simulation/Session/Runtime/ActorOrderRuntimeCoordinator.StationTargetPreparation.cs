namespace GalaxyCommand.Simulation;

internal sealed record StationTargetContinuationCheckpoint(EventKey Key, IReadOnlyList<ShipId> Ships);

internal sealed partial class ActorOrderRuntimeCoordinator
{
    private EventKey? _stationTargetWake;
    private readonly SortedSet<ShipId> _stationTargetShips = new(EntityIdComparer<ShipId>.Instance);

    /// <summary>Preflights exact interruption evidence and the one coalesced continuation envelope before any causal source changes.</summary>
    private bool CanRemoveStationTargets(EntityId station)
    {
        try
        {
            foreach (ShipId ship in _orders.PrepareTargetRemoval(station, default).Where(target => target.WasCurrentActive)
                .Select(target => target.ShipId).Distinct())
            {
                ShipSpatialState? state = _movement.GetState(ship);
                if (state is ShipSpatialState.InteractionControlled) return false;
                if (state is not (ShipSpatialState.Moving or ShipSpatialState.AnalyticManeuver)) continue;
                _ = _movement.GetSpatialRevision(ship)!.Value.Next();
                if (state is ShipSpatialState.Moving moving) _ = moving.Motion.Generation.Next();
                else if (state is ShipSpatialState.AnalyticManeuver analytic)
                {
                    _ = analytic.Maneuver.PrepareInterruption(CurrentTime, _agenda.CurrentTime);
                    if (analytic.Maneuver.PendingEvents is not { } pending || pending.Any(entry =>
                        _agenda.CheckCancellation(entry.EventKey, entry.Generation,
                            new GameEvent.SpatialMovement(new SpatialMovementEvent.Maneuver(ship, entry.Payload))) != AgendaCancellationCheck.Matches)) return false;
                }
            }
            _agenda.EnsureCreationCapacity(StationTargetAgendaCapacity(station));
            return true;
        }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException or ArgumentException) { return false; }
    }

    /// <summary>Returns only newly required agenda capacity; an existing exact same-time wake owns all later additions.</summary>
    private int StationTargetAgendaCapacity(EntityId station)
    {
        if (!_orders.PrepareTargetRemoval(station, default).Any(target => target.WasCurrentActive)) return 0;
        if (_stationTargetWake is not { } wake) return 1;
        if (wake.Timestamp != CurrentTime || wake.Phase != EventPhase.Decision
            || _agenda.CheckCancellation(wake, default, new GameEvent.StationTargetContinuation()) != AgendaCancellationCheck.Matches)
            throw new InvalidOperationException("The station target owner's exact continuation envelope changed.");
        return 0;
    }

    /// <summary>Stops invalid physical work within removal; arbitrary queued continuation belongs to its pre-reserved Decision envelope.</summary>
    private List<GameFactProposal> ApplyStationTargetRemoval(EntityId station)
    {
        if (!CanRemoveStationTargets(station)) throw new InvalidOperationException("Prepared station target removal changed during commit.");
        TargetedShipOrder[] inbound = _orders.PrepareTargetRemoval(station, default).OrderBy(target => target.WasCurrentActive)
            .ThenBy(target => target.ShipId.Value).ThenBy(target => target.OrderId.Value).ToArray();
        var transitions = new List<ShipOrderTransition>();
        var facts = new List<GameFactProposal>();
        foreach (TargetedShipOrder target in inbound)
        {
            if (target.WasCurrentActive)
            {
                EndActiveLocalMotion(target.ShipId, LocalMotionEndReason.TargetRemoved, facts);
                _stationTargetShips.Add(target.ShipId);
            }
            _orders.ApplyTargetRemoval(target, transitions);
        }
        if (_stationTargetShips.Count > 0 && _stationTargetWake is null)
            _stationTargetWake = _agenda.Schedule(CurrentTime, EventPhase.Decision, default, new GameEvent.StationTargetContinuation());
        AddOrderTransitionProposals(transitions, facts);
        return facts;
    }

    /// <summary>Continues all interrupted actors in identity order after the complete physical loss has committed.</summary>
    private ScheduledEventDisposition ContinueStationTargets(ScheduledEvent<GameEvent> scheduled)
    {
        if (_stationTargetWake != scheduled.Key || scheduled.Generation != default(EventGeneration))
            return ScheduledEventDisposition.IgnoredMissingReference;
        ShipId[] ships = _stationTargetShips.ToArray();
        _stationTargetShips.Clear();
        _stationTargetWake = null;
        var transitions = new List<ShipOrderTransition>();
        var facts = new List<GameFactProposal>();
        foreach (ShipId ship in ships)
        {
            if (!_orders.Contains(ship)) continue;
            // A later command may already have started replacement work before
            // this wake. It owns that work; the retained wake is harmless stale input.
            if (_movement.GetState(ship) is not ShipSpatialState.AtPosition || _orders.GetActive(ship)?.Plan is not null) continue;
            SynchronizeSensorInterest(ship);
            SynchronizeCargoInterest(ship);
            StartOrContinueOrders(ship, transitions, facts);
            if (!_orders.HasActive(ship)) StartPassiveDrag(ship);
        }
        AddOrderTransitionProposals(transitions, facts);
        _facts.Commit(CurrentTime, new ScheduledEventFactCause(scheduled.Key), facts);
        return ScheduledEventDisposition.Applied;
    }

    /// <summary>Withdraws dead actor references and the exact now-empty owner wake during ordinary entity cleanup.</summary>
    private void ReleaseRemovedStationTarget(ShipId ship)
    {
        if (!_stationTargetShips.Remove(ship) || _stationTargetShips.Count != 0 || _stationTargetWake is not { } wake) return;
        if (!_agenda.TryCancelExact(wake, default, new GameEvent.StationTargetContinuation()))
            throw new InvalidOperationException("The removed actor's station target continuation changed.");
        _stationTargetWake = null;
    }

    private StationTargetContinuationCheckpoint? CaptureStationTargetContinuation() => _stationTargetWake is { } key
        ? new(key, Array.AsReadOnly(_stationTargetShips.ToArray())) : null;

    private void RestoreStationTargetContinuation(StationTargetContinuationCheckpoint? saved)
    {
        if (saved is null) return;
        _stationTargetWake = saved.Key;
        foreach (ShipId ship in saved.Ships) _stationTargetShips.Add(ship);
    }

    private static ShipOrderWorkSetCheckpoint? CurrentCheckpointOrders(GameSessionRuntimeCheckpoint checkpoint, ShipId ship)
    {
        ShipActorOrdersCheckpoint? actor = checkpoint.Orders.Actors.FirstOrDefault(value => value?.ShipId == ship);
        return actor?.Override ?? actor?.Base;
    }

    private static bool NeedsUnplannedContinuation(GameSessionRuntimeCheckpoint checkpoint, ShipId ship)
    {
        ShipOrderCheckpoint? active = CurrentCheckpointOrders(checkpoint, ship)?.Active;
        return active is { Status: ShipOrderStatus.Active, Plan: null }
            && active is not { Action: not null, Reason: ShipOrderReason.AwaitingActionExecution }
            && active is not { Action: ShipActionIntent.Cargo, Reason: ShipOrderReason.CollectingCargo };
    }

    /// <summary>Only exact failed station-target evidence permits an active order awaiting its first Decision plan.</summary>
    private static bool HasStationInterruptionProof(GameSessionRuntimeCheckpoint checkpoint, ShipId ship) =>
        CurrentCheckpointOrders(checkpoint, ship)?.LastTerminal is
            { Status: ShipOrderStatus.Failed, Reason: ShipOrderReason.TargetRemoved, Destination: NavigationDestination.Entity target }
        && checkpoint.Stations?.Removals.Any(removal => removal.Station.EntityId == target.EntityId
            && removal.RemovedAt == checkpoint.StationTargetContinuation?.Key.Timestamp) == true;

    private static HashSet<ShipId>? UnplannedStationContinuations(GameSessionRuntimeCheckpoint checkpoint) =>
        checkpoint.StationTargetContinuation?.Ships?.Where(ship => HasStationInterruptionProof(checkpoint, ship)).ToHashSet();

    /// <summary>Requires reciprocal exact wake ownership and one allocated live actor per retained continuation.</summary>
    private static CheckpointValidationFailure? ValidateStationTargetContinuation(GameSessionRuntimeCheckpoint checkpoint)
    {
        ScheduledEvent<GameEvent>[] events = checkpoint.Engine.Agenda.PendingEvents.Where(entry => entry.Payload is GameEvent.StationTargetContinuation).ToArray();
        StationTargetContinuationCheckpoint? saved = checkpoint.StationTargetContinuation;
        if (saved is null) return events.Length == 0 ? null : new("$.checkpoint.stationTargetContinuation", "A continuation envelope has no owner.");
        if (saved.Ships is not { Count: > 0 } || saved.Ships.Distinct().Count() != saved.Ships.Count
            || !saved.Ships.SequenceEqual(saved.Ships.OrderBy(ship => ship.Value))
            || saved.Ships.Any(ship => !checkpoint.Orders.Actors.Any(actor => actor is not null && actor.ShipId == ship))
            || saved.Ships.Any(ship => NeedsUnplannedContinuation(checkpoint, ship) && !HasStationInterruptionProof(checkpoint, ship))
            || events.Length != 1 || events[0].Key != saved.Key || events[0].Generation != default(EventGeneration)
            || saved.Key.Phase != EventPhase.Decision || saved.Key.Timestamp != checkpoint.Engine.Agenda.CurrentTime)
            return new("$.checkpoint.stationTargetContinuation", "Continuation actors and their exact Decision envelope must agree.");
        return null;
    }
}
