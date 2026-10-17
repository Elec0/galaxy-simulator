using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SpatialManeuverHandoffTests
{
    [Fact]
    public void BatchHandoffPreservesExactStateAndCancelsOnlySelectedSchedules()
    {
        var (movement, agenda) = Create();
        var now = new SimulationTime(500);
        agenda.AdvanceTo(now);
        var expected = new[] { new ShipId(1), new ShipId(2) }
            .ToDictionary(ship => ship, ship => movement.KinematicStateAt(ship, now));
        var unrelated = agenda.CaptureCheckpoint().Value!.PendingEvents
            .Where(item => ((SpatialMovementEvent.Maneuver)item.Payload).ShipId == new ShipId(3)).ToArray();
        ulong nextSequence = agenda.CaptureCheckpoint().Value!.NextCreationSequence;
        using var changes = movement.AttachInteractionChanges();
        changes.Acknowledge(changes.Capture());

        Assert.Equal(AgendaCancellationCheck.Matches, movement.TryInterruptTerminalManeuvers(
            [new ShipId(2), new ShipId(1)], now, agenda, static item => item, out var handoffs));

        Assert.NotNull(handoffs);
        Assert.Equal(new[] { new ShipId(1), new ShipId(2) }, handoffs.Keys);
        foreach (var (ship, handoff) in handoffs)
        {
            Assert.Equal(expected[ship], handoff.MaterializedState);
            Assert.Equal(expected[ship], movement.KinematicStateAt(ship, now));
            Assert.Equal(new EventGeneration(1), handoff.NextGeneration);
            Assert.Equal(new SpatialRevision(2), movement.GetSpatialRevision(ship));
            Assert.IsType<ShipSpatialState.AtPosition>(movement.GetState(ship));
        }
        Assert.Equal(unrelated, agenda.CaptureCheckpoint().Value!.PendingEvents);
        Assert.Equal(nextSequence, agenda.CaptureCheckpoint().Value!.NextCreationSequence);
        Assert.Equal(handoffs.Keys, changes.Capture().Select(item => item.ShipId));
        Assert.IsType<ShipSpatialState.AnalyticManeuver>(movement.GetState(new ShipId(3)));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<ShipId, ManeuverInterruption>)handoffs).Clear());
    }

    [Fact]
    public void MissingLaterScheduleLeavesEveryParticipantAndNoticeUnchanged()
    {
        var (movement, agenda) = Create();
        var pending = agenda.CaptureCheckpoint().Value!.PendingEvents
            .Last(item => ((SpatialMovementEvent.Maneuver)item.Payload).ShipId == new ShipId(2));
        Assert.True(agenda.TryCancelExact(pending.Key, pending.Generation, pending.Payload));
        AssertRejectedWithoutMutation(movement, agenda, AgendaCancellationCheck.Missing, static item => item);
    }

    [Fact]
    public void LaterWrapperFailureLeavesEveryParticipantAndNoticeUnchanged()
    {
        var (movement, agenda) = Create();
        AssertRejectedWithoutMutation(movement, agenda, null, item =>
            ((SpatialMovementEvent.Maneuver)item).ShipId == new ShipId(2)
                ? throw new InvalidOperationException("wrapper failed") : item);
    }

    [Fact]
    public void MismatchedLaterEnvelopeLeavesEveryParticipantUnchanged()
    {
        var (movement, agenda) = Create();
        AssertRejectedWithoutMutation(movement, agenda, AgendaCancellationCheck.Mismatch, item =>
        {
            var maneuver = (SpatialMovementEvent.Maneuver)item;
            return maneuver.ShipId == new ShipId(2)
                ? new SpatialMovementEvent.Maneuver(new ShipId(3), maneuver.Event) : item;
        });
    }

    [Fact]
    public void LaterRevisionExhaustionDoesNotPartiallyInterruptEarlierShip()
    {
        var (movement, agenda) = Create();
        var checkpoint = movement.CaptureCheckpoint(SimulationTime.Zero).Value!;
        var restored = SpatialMovement.RestoreCheckpoint(new SpatialMovementCheckpoint(
            checkpoint.MotionIds, checkpoint.TransitIds, checkpoint.Actors.Select(actor =>
                actor.ShipId == new ShipId(2) ? actor with { SpatialRevision = new SpatialRevision(ulong.MaxValue) } : actor)),
            SimulationTime.Zero);
        Assert.True(restored.IsSuccess, restored.Failure?.Message);
        var now = new SimulationTime(500);
        agenda.AdvanceTo(now);
        var before = agenda.CaptureCheckpoint().Value!;
        Assert.Throws<OverflowException>(() => restored.Value!.TryInterruptTerminalManeuvers(
            [new ShipId(1), new ShipId(2)], now, agenda, static item => item, out _));
        Assert.Equal(new SpatialRevision(1), restored.Value!.GetSpatialRevision(new ShipId(1)));
        Assert.IsType<ShipSpatialState.AnalyticManeuver>(restored.Value.GetState(new ShipId(1)));
        Assert.Equal(before.PendingEvents, agenda.CaptureCheckpoint().Value!.PendingEvents);
    }

    [Fact]
    public void DuplicateParticipantsRejectBeforeWrappingAndEmptyBatchIsANoop()
    {
        var (movement, agenda) = Create();
        int calls = 0;
        SpatialMovementEvent Wrap(SpatialMovementEvent item) { calls++; return item; }
        Assert.Throws<ArgumentException>(() => movement.TryInterruptTerminalManeuvers(
            [new ShipId(1), new ShipId(1)], SimulationTime.Zero, agenda, Wrap, out _));
        Assert.Equal(0, calls);
        var before = agenda.CaptureCheckpoint().Value!;
        Assert.Equal(AgendaCancellationCheck.Matches, movement.TryInterruptTerminalManeuvers(
            [], SimulationTime.Zero, agenda, Wrap, out var result));
        Assert.Empty(result!);
        Assert.Equal(before.PendingEvents, agenda.CaptureCheckpoint().Value!.PendingEvents);
    }

    [Fact]
    public void WrappersRunOncePerPendingEnvelopeOnlyDuringPreparation()
    {
        var (movement, agenda) = Create();
        int expected = agenda.CaptureCheckpoint().Value!.PendingEvents.Count(item =>
            item.Payload.ShipId != new ShipId(3));
        int calls = 0;
        Assert.Equal(AgendaCancellationCheck.Matches, movement.TryInterruptTerminalManeuvers(
            [new ShipId(1), new ShipId(2)], SimulationTime.Zero, agenda,
            item => { calls++; return item; }, out _));
        Assert.Equal(expected, calls);
    }

    private static void AssertRejectedWithoutMutation(SpatialMovement movement, EventAgenda<SpatialMovementEvent> agenda,
        AgendaCancellationCheck? expected, Func<SpatialMovementEvent, SpatialMovementEvent> wrap)
    {
        var now = new SimulationTime(500);
        agenda.AdvanceTo(now);
        var before = movement.CaptureCheckpoint(now).Value!;
        var pending = agenda.CaptureCheckpoint().Value!;
        using var changes = movement.AttachInteractionChanges();
        changes.Acknowledge(changes.Capture());
        if (expected is { } disposition)
        {
            Assert.Equal(disposition, movement.TryInterruptTerminalManeuvers(
                [new ShipId(1), new ShipId(2)], now, agenda, wrap, out var result));
            Assert.Null(result);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => movement.TryInterruptTerminalManeuvers(
                [new ShipId(1), new ShipId(2)], now, agenda, wrap, out _));
        }
        var after = movement.CaptureCheckpoint(now).Value!;
        Assert.Equal(before.Actors.Select(actor => (actor.ShipId, actor.Generation, actor.SpatialRevision)),
            after.Actors.Select(actor => (actor.ShipId, actor.Generation, actor.SpatialRevision)));
        foreach (var actor in after.Actors)
            Assert.False(Assert.IsType<ShipSpatialState.AnalyticManeuver>(movement.GetState(actor.ShipId)).Maneuver.IsInvalidated);
        Assert.Equal(pending.PendingEvents, agenda.CaptureCheckpoint().Value!.PendingEvents);
        Assert.Equal(pending.NextCreationSequence, agenda.CaptureCheckpoint().Value!.NextCreationSequence);
        Assert.Empty(changes.Capture());
    }

    private static (SpatialMovement Movement, EventAgenda<SpatialMovementEvent> Agenda) Create()
    {
        var movement = new SpatialMovement();
        var agenda = new EventAgenda<SpatialMovementEvent>();
        for (ulong id = 1; id <= 3; id++)
        {
            var ship = new ShipId(id);
            var origin = Point((long)id * 10);
            movement.Add(ship, origin);
            var plan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
                new ShipKinematicState(origin, ShipVelocity.Zero, ShipHeading.Zero), Point((long)id * 10 + 90), null,
                new ManeuverAcceleration(10000), new ManeuverAcceleration(10000), new ManeuverSpeed(30000),
                new ManeuverTurnRate(45000)).ExecutablePlan!;
            var commit = movement.CommitStartTerminalManeuver(ship, plan, ManeuverObjective.FastestArrival,
                SimulationTime.Zero, static item => item);
            movement.BindTerminalManeuverEvents(ship, AgendaCommitOwner.Commit(agenda, commit.EventProposals).EventKeys);
        }
        return (movement, agenda);
    }

    private static SystemPosition Point(long x) => new(new SystemId(1),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
