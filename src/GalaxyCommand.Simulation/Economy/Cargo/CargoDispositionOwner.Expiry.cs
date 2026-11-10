namespace GalaxyCommand.Simulation;

internal sealed record CargoObjectExpiredFact(EntityId EntityId, CargoDispositionKey Origin) : GameFact;

internal sealed partial class CargoDispositionOwner
{
    /// <summary>
    /// Processes an exact due expiry after the wave's eligible collection commits.
    /// Remaining contents are permanently destroyed; stale envelopes never affect
    /// a different object, and no replacement cargo is generated.
    /// </summary>
    internal ScheduledEventDisposition Expire(ScheduledEvent<GameEvent> scheduled)
    {
        ThrowIfUnhealthy();
        ArgumentNullException.ThrowIfNull(scheduled);
        if (scheduled.Payload is not GameEvent.CargoExpiry expiry) return ScheduledEventDisposition.IgnoredStateMismatch;
        if (!_objects.TryGetValue(expiry.ObjectId, out CargoLiveObject? cargo)) return ScheduledEventDisposition.IgnoredMissingReference;
        if (cargo.Generation != scheduled.Generation) return ScheduledEventDisposition.IgnoredStaleGeneration;
        if (cargo.ExpiryEvent != scheduled.Key || scheduled.Key.Timestamp != _agenda.CurrentTime
            || scheduled.Key.Timestamp != cargo.ExpiresAt || scheduled.Key.Phase != EventPhase.StateUpdate
            || _agenda.CurrentPhase != EventPhase.StateUpdate
            || _agenda.CheckCancellation(scheduled.Key, scheduled.Generation, scheduled.Payload) != AgendaCancellationCheck.Missing)
            return ScheduledEventDisposition.IgnoredStateMismatch;
        try
        {
            if (!_lifecycle.TryPrepareCargoObjectRetirement(cargo.EntityId, CargoObjectRetirementKind.Expired, out var retirement))
                throw new InvalidOperationException("A due cargo expiry has no valid physical object to retire.");
            PreparedCargoDebrisRemoval? field = Fields.PrepareRemoval(cargo.EntityId);
            if (cargo.OriginalFieldId is not null && field is null || field is not null && !Fields.CanCommit(field)
                || !_facts.CanCommit(2) || !_lifecycle.CanCommitCargoObjectRetirement(retirement!))
                throw new InvalidOperationException("Cargo expiry participants cannot commit together.");
            _lifecycle.ApplyPreparedCargoObjectRetirement(retirement!);
            if (field is not null) Fields.Apply(field);
            _objects.Remove(cargo.EntityId);
            _facts.Commit(_agenda.CurrentTime, new ScheduledEventFactCause(scheduled.Key),
                [new GameFactProposal(new(GameFactCommitCategory.EntityLifecycle, cargo.EntityId.Value, 0, 0),
                    new CargoObjectExpiredFact(cargo.EntityId, cargo.Origin)),
                 new GameFactProposal(new(GameFactCommitCategory.EntityLifecycle, cargo.EntityId.Value, 0, 1),
                    new CargoObjectDisappearedFact(cargo.EntityId, retirement!.Object.Position))]);
            return ScheduledEventDisposition.Applied;
        }
        catch { _isHealthy = false; throw; }
    }
}
