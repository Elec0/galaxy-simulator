using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ScheduledTerminalManeuverTests
{
    [Fact]
    public void UnboundNextBoundaryExposesTheCanonicalPhaseExpectation()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(73),
            new EventGeneration(74),
            plan);

        ManeuverBoundaryDiagnostic boundary = Assert.IsType<
            ManeuverBoundaryDiagnostic>(scheduled.NextBoundary);

        Assert.Equal(scheduled.MotionId, boundary.MotionId);
        Assert.Equal(scheduled.Generation, boundary.Generation);
        Assert.Equal(0, boundary.PhaseIndex);
        Assert.Equal(plan.Phases[0].Kind, boundary.PhaseKind);
        Assert.Equal(plan.Phases[0].EndsAt, boundary.Timestamp);
        Assert.Null(boundary.EventKey);
        var payload = Assert.IsType<ManeuverScheduleEvent.PhaseBoundary>(
            boundary.Payload);
        Assert.Equal(0, payload.PhaseIndex);
    }

    [Fact]
    public void UnifiedDispatchAdvancesTheBoundDiagnosticUntilCompletion()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(75),
            new EventGeneration(76),
            plan);
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        BindPendingEvents(scheduled, agenda, new ShipId(77));
        for (int index = 0; index < plan.Phases.Count; index++)
        {
            ManeuverBoundaryDiagnostic boundary = Assert.IsType<
                ManeuverBoundaryDiagnostic>(scheduled.NextBoundary);
            Assert.Equal(index, boundary.PhaseIndex);
            Assert.Equal(plan.Phases[index].Kind, boundary.PhaseKind);
            Assert.Equal(
                scheduled.PendingEvents![0].EventKey,
                boundary.EventKey);
            if (index < plan.Phases.Count - 1)
            {
                Assert.IsType<ManeuverScheduleEvent.PhaseBoundary>(
                    boundary.Payload);
            }
            else
            {
                Assert.IsType<ManeuverScheduleEvent.Complete>(boundary.Payload);
            }

            ScheduledEventDisposition disposition = scheduled.HandleEvent(
                boundary.Payload,
                boundary.Generation,
                boundary.Timestamp,
                out ShipKinematicState? materialized);

            Assert.Equal(ScheduledEventDisposition.Applied, disposition);
            Assert.Equal(plan.StateAt(boundary.Timestamp), materialized);
        }

        Assert.Null(scheduled.NextBoundary);
    }

    [Fact]
    public void StaleUnifiedDispatchDoesNotChangeTheNextBoundary()
    {
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(78),
            new EventGeneration(80),
            Plan());
        ManeuverBoundaryDiagnostic expected = Assert.IsType<
            ManeuverBoundaryDiagnostic>(scheduled.NextBoundary);
        var stale = new ManeuverScheduleEvent.PhaseBoundary(
            scheduled.MotionId,
            new EventGeneration(79),
            phaseIndex: 0);

        ScheduledEventDisposition disposition = scheduled.HandleEvent(
            stale,
            stale.Generation,
            expected.Timestamp,
            out ShipKinematicState? materialized);

        Assert.Equal(
            ScheduledEventDisposition.IgnoredStaleGeneration,
            disposition);
        Assert.Null(materialized);
        Assert.Equal(expected, scheduled.NextBoundary);
    }

    [Fact]
    public void InvalidatedScheduleHasNoExpectedNextBoundary()
    {
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(81),
            new EventGeneration(82),
            Plan());
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        BindPendingEvents(scheduled, agenda, new ShipId(83));

        _ = scheduled.InvalidateAndCancelPendingEvents(
            agenda,
            static maneuverEvent => maneuverEvent);

        Assert.Null(scheduled.NextBoundary);
    }

    [Fact]
    public void InterruptionMaterializesCurrentPhaseAndReturnsNextGeneration()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(56),
            new EventGeneration(57),
            plan);
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        BindPendingEvents(scheduled, agenda, new ShipId(58));
        SimulationTime now = Inside(plan.Phases[0]);
        agenda.AdvanceTo(now);

        AgendaCancellationCheck result = scheduled.TryInterrupt(
            now,
            agenda,
            static maneuverEvent => maneuverEvent,
            out ManeuverInterruption? interruption);

        Assert.Equal(AgendaCancellationCheck.Matches, result);
        Assert.NotNull(interruption);
        Assert.Equal(now, interruption.Timestamp);
        Assert.Equal(plan.StateAt(now), interruption.MaterializedState);
        Assert.Equal(scheduled.Generation.Next(), interruption.NextGeneration);
        Assert.True(scheduled.IsInvalidated);
        Assert.Empty(scheduled.PendingEvents!);
        Assert.Equal(0, agenda.Count);
    }

    [Fact]
    public void InterruptionAtPendingBoundaryIsRejectedBeforeMutation()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(59),
            new EventGeneration(60),
            plan);
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        BindPendingEvents(scheduled, agenda, new ShipId(61));
        SimulationTime boundary = plan.Phases[0].EndsAt;
        agenda.AdvanceTo(boundary);
        int countBefore = agenda.Count;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scheduled.TryInterrupt(
                boundary,
                agenda,
                static maneuverEvent => maneuverEvent,
                out _));

        Assert.Equal(countBefore, agenda.Count);
        Assert.False(scheduled.IsInvalidated);
        Assert.Equal(0, scheduled.CurrentPhaseIndex);
    }

    [Fact]
    public void InterruptionRequiresAgendaClockAtCommitTimestamp()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(67),
            new EventGeneration(68),
            plan);
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        BindPendingEvents(scheduled, agenda, new ShipId(69));
        SimulationTime now = Inside(plan.Phases[0]);
        int countBefore = agenda.Count;

        Assert.Throws<InvalidOperationException>(() =>
            scheduled.TryInterrupt(
                now,
                agenda,
                static maneuverEvent => maneuverEvent,
                out _));

        Assert.Equal(countBefore, agenda.Count);
        Assert.False(scheduled.IsInvalidated);
    }

    [Fact]
    public void InterruptionCancellationFailurePublishesNoMaterializedResult()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(62),
            new EventGeneration(63),
            plan);
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        BindPendingEvents(scheduled, agenda, new ShipId(64));
        PendingManeuverScheduleEvent missing = scheduled.PendingEvents![0];
        Assert.True(agenda.TryCancelExact(
            missing.EventKey,
            missing.Generation,
            missing.Payload));
        SimulationTime now = Inside(plan.Phases[0]);
        agenda.AdvanceTo(now);

        AgendaCancellationCheck result = scheduled.TryInterrupt(
            now,
            agenda,
            static maneuverEvent => maneuverEvent,
            out ManeuverInterruption? interruption);

        Assert.Equal(AgendaCancellationCheck.Missing, result);
        Assert.Null(interruption);
        Assert.False(scheduled.IsInvalidated);
    }

    [Fact]
    public void InterruptionGenerationOverflowPrecedesAgendaMutation()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(65),
            new EventGeneration(ulong.MaxValue),
            plan);
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        BindPendingEvents(scheduled, agenda, new ShipId(66));
        SimulationTime now = Inside(plan.Phases[0]);
        agenda.AdvanceTo(now);
        int countBefore = agenda.Count;

        Assert.Throws<OverflowException>(() =>
            scheduled.TryInterrupt(
                now,
                agenda,
                static maneuverEvent => maneuverEvent,
                out _));

        Assert.Equal(countBefore, agenda.Count);
        Assert.False(scheduled.IsInvalidated);
    }

    [Fact]
    public void InvalidatedScheduleCannotBeInterruptedAgain()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(70),
            new EventGeneration(71),
            plan);
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        BindPendingEvents(scheduled, agenda, new ShipId(72));
        SimulationTime now = Inside(plan.Phases[0]);
        agenda.AdvanceTo(now);
        _ = scheduled.TryInterrupt(
            now,
            agenda,
            static maneuverEvent => maneuverEvent,
            out _);

        Assert.Throws<InvalidOperationException>(() =>
            scheduled.TryInterrupt(
                now,
                agenda,
                static maneuverEvent => maneuverEvent,
                out _));
    }

    [Fact]
    public void ExactPendingCancellationInvalidatesTheOldSchedule()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(45),
            new EventGeneration(46),
            plan);
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        BindPendingEvents(scheduled, agenda, new ShipId(47));
        PendingManeuverScheduleEvent first = scheduled.PendingEvents![0];

        AgendaCancellationCheck result =
            scheduled.InvalidateAndCancelPendingEvents(
                agenda,
                static maneuverEvent => maneuverEvent);

        Assert.Equal(AgendaCancellationCheck.Matches, result);
        Assert.Equal(0, agenda.Count);
        Assert.True(scheduled.IsInvalidated);
        Assert.Empty(scheduled.PendingEvents!);
        var boundary = Assert.IsType<ManeuverScheduleEvent.PhaseBoundary>(
            first.Payload);
        ScheduledEventDisposition staleDisposition =
            scheduled.HandlePhaseBoundary(
                boundary,
                boundary.Generation,
                first.EventKey.Timestamp,
                out ShipKinematicState? materialized);
        Assert.Equal(
            ScheduledEventDisposition.IgnoredStaleGeneration,
            staleDisposition);
        Assert.Null(materialized);
        Assert.Equal(0, scheduled.CurrentPhaseIndex);
    }

    [Fact]
    public void MissingPendingEventRejectsCancellationWithoutRemovingOthers()
    {
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(48),
            new EventGeneration(49),
            Plan());
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        BindPendingEvents(scheduled, agenda, new ShipId(50));
        PendingManeuverScheduleEvent missing = scheduled.PendingEvents![0];
        Assert.True(agenda.TryCancelExact(
            missing.EventKey,
            missing.Generation,
            missing.Payload));
        int countBefore = agenda.Count;

        AgendaCancellationCheck result =
            scheduled.InvalidateAndCancelPendingEvents(
                agenda,
                static maneuverEvent => maneuverEvent);

        Assert.Equal(AgendaCancellationCheck.Missing, result);
        Assert.Equal(countBefore, agenda.Count);
        Assert.False(scheduled.IsInvalidated);
        Assert.Equal(scheduled.Plan.Phases.Count, scheduled.PendingEvents!.Count);
        foreach (PendingManeuverScheduleEvent pending
            in scheduled.PendingEvents.Skip(1))
        {
            Assert.Equal(
                AgendaCancellationCheck.Matches,
                agenda.CheckCancellation(
                    pending.EventKey,
                    pending.Generation,
                    pending.Payload));
        }
    }

    [Fact]
    public void PayloadMismatchRejectsCancellationWithoutAgendaMutation()
    {
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(51),
            new EventGeneration(52),
            Plan());
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        BindPendingEvents(scheduled, agenda, new ShipId(53));
        int countBefore = agenda.Count;

        AgendaCancellationCheck result =
            scheduled.InvalidateAndCancelPendingEvents(
                agenda,
                maneuverEvent => maneuverEvent
                    is ManeuverScheduleEvent.PhaseBoundary
                        ? new ManeuverScheduleEvent.Complete(
                            maneuverEvent.MotionId,
                            maneuverEvent.Generation)
                        : maneuverEvent);

        Assert.Equal(AgendaCancellationCheck.Mismatch, result);
        Assert.Equal(countBefore, agenda.Count);
        Assert.False(scheduled.IsInvalidated);
        Assert.Equal(scheduled.Plan.Phases.Count, scheduled.PendingEvents!.Count);
    }

    [Fact]
    public void CancellationRequiresBoundPendingEvents()
    {
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(54),
            new EventGeneration(55),
            Plan());

        Assert.Throws<InvalidOperationException>(() =>
            scheduled.InvalidateAndCancelPendingEvents(
                new EventAgenda<ManeuverScheduleEvent>(),
                static maneuverEvent => maneuverEvent));
    }

    [Fact]
    public void BoundPendingEventsTrackOnlyUnappliedScheduleWork()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(34),
            new EventGeneration(35),
            plan);
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        IReadOnlyList<AgendaEventProposal<ManeuverScheduleEvent>> proposals =
            scheduled.CreateRemainingEventProposals(
                new ShipId(36),
                static maneuverEvent => maneuverEvent);
        AgendaCommitResult commit = AgendaCommitOwner.Commit(agenda, proposals);

        scheduled.BindPendingEventKeys(commit.EventKeys);

        IReadOnlyList<PendingManeuverScheduleEvent> pending =
            Assert.IsAssignableFrom<IReadOnlyList<PendingManeuverScheduleEvent>>(
                scheduled.PendingEvents);
        Assert.Equal(proposals.Count, pending.Count);
        for (int index = 0; index < pending.Count; index++)
        {
            Assert.Equal(commit.EventKeys[index], pending[index].EventKey);
            Assert.Equal(scheduled.Generation, pending[index].Generation);
            Assert.Equal(proposals[index].Payload, pending[index].Payload);
            Assert.Equal(
                AgendaCancellationCheck.Matches,
                agenda.CheckCancellation(
                    pending[index].EventKey,
                    pending[index].Generation,
                    pending[index].Payload));
        }

        var firstBoundary = Assert.IsType<ManeuverScheduleEvent.PhaseBoundary>(
            pending[0].Payload);
        _ = scheduled.HandlePhaseBoundary(
            firstBoundary,
            firstBoundary.Generation,
            pending[0].EventKey.Timestamp,
            out _);

        pending = Assert.IsAssignableFrom<
            IReadOnlyList<PendingManeuverScheduleEvent>>(
                scheduled.PendingEvents);
        Assert.Equal(proposals.Count - 1, pending.Count);
        Assert.Equal(commit.EventKeys[1], pending[0].EventKey);

        var completion = Assert.IsType<ManeuverScheduleEvent.Complete>(
            pending[^1].Payload);
        _ = scheduled.HandleCompletion(
            completion,
            completion.Generation,
            pending[^1].EventKey.Timestamp,
            out _);

        Assert.Empty(Assert.IsAssignableFrom<
            IReadOnlyList<PendingManeuverScheduleEvent>>(
                scheduled.PendingEvents));
    }

    [Fact]
    public void PendingEventBindingRejectsMismatchedKeysWithoutPartialState()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(37),
            new EventGeneration(38),
            plan);
        IReadOnlyList<AgendaEventProposal<ManeuverScheduleEvent>> proposals =
            scheduled.CreateRemainingEventProposals(
                new ShipId(39),
                static maneuverEvent => maneuverEvent);
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        AgendaCommitResult commit = AgendaCommitOwner.Commit(agenda, proposals);
        EventKey[] mismatched = commit.EventKeys.ToArray();
        mismatched[0] = new EventKey(
            new SimulationTime(mismatched[0].Timestamp.Milliseconds + 1),
            mismatched[0].Phase,
            mismatched[0].CreationSequence);

        Assert.Throws<InvalidOperationException>(() =>
            scheduled.BindPendingEventKeys(mismatched));

        Assert.Null(scheduled.PendingEvents);
    }

    [Fact]
    public void PendingEventBindingRequiresEveryRemainingKeyExactlyOnce()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(40),
            new EventGeneration(41),
            plan);
        IReadOnlyList<AgendaEventProposal<ManeuverScheduleEvent>> proposals =
            scheduled.CreateRemainingEventProposals(
                new ShipId(42),
                static maneuverEvent => maneuverEvent);
        var agenda = new EventAgenda<ManeuverScheduleEvent>();
        AgendaCommitResult commit = AgendaCommitOwner.Commit(agenda, proposals);

        Assert.Throws<InvalidOperationException>(() =>
            scheduled.BindPendingEventKeys(commit.EventKeys.Skip(1).ToArray()));
        scheduled.BindPendingEventKeys(commit.EventKeys);
        Assert.Throws<InvalidOperationException>(() =>
            scheduled.BindPendingEventKeys(commit.EventKeys));
        Assert.Throws<InvalidOperationException>(() =>
            scheduled.CreateRemainingEventProposals(
                new ShipId(42),
                static maneuverEvent => maneuverEvent));
    }

    [Fact]
    public void PendingEventBindingRejectsNonIncreasingCreationSequences()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(43),
            new EventGeneration(44),
            plan);
        EventKey[] keys = plan.Phases
            .Select((phase, index) => new EventKey(
                phase.EndsAt,
                EventPhase.PhysicalCompletion,
                CreationSequence: (ulong)(plan.Phases.Count - index)))
            .ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            scheduled.BindPendingEventKeys(keys));

        Assert.Null(scheduled.PendingEvents);
    }

    [Fact]
    public void RemainingEventProposalsPreserveCanonicalScheduleOrder()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var shipId = new ShipId(22);
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(23),
            new EventGeneration(24),
            plan);

        IReadOnlyList<AgendaEventProposal<ManeuverScheduleEvent>> proposals =
            scheduled.CreateRemainingEventProposals(
                shipId,
                static maneuverEvent => maneuverEvent);

        Assert.Equal(plan.Phases.Count, proposals.Count);
        for (int index = 0; index < plan.Phases.Count; index++)
        {
            AgendaEventProposal<ManeuverScheduleEvent> proposal =
                proposals[index];
            Assert.Equal(
                new AgendaProposalOrder(
                    RuntimeEvaluationWave.PhysicalCompletion,
                    shipId.Value,
                    scheduled.MotionId.Value,
                    EffectKind: 1,
                    LocalOrdinal: index),
                proposal.Order);
            Assert.Equal(plan.Phases[index].EndsAt, proposal.Timestamp);
            Assert.Equal(EventPhase.PhysicalCompletion, proposal.Phase);
            Assert.Equal(scheduled.Generation, proposal.Generation);

            if (index < plan.Phases.Count - 1)
            {
                var boundary = Assert.IsType<ManeuverScheduleEvent.PhaseBoundary>(
                    proposal.Payload);
                Assert.Equal(scheduled.MotionId, boundary.MotionId);
                Assert.Equal(scheduled.Generation, boundary.Generation);
                Assert.Equal(index, boundary.PhaseIndex);
            }
            else
            {
                var completion = Assert.IsType<ManeuverScheduleEvent.Complete>(
                    proposal.Payload);
                Assert.Equal(scheduled.MotionId, completion.MotionId);
                Assert.Equal(scheduled.Generation, completion.Generation);
            }
        }
    }

    [Fact]
    public void RemainingEventProposalsOmitAppliedBoundaries()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(25),
            new EventGeneration(26),
            plan);
        var firstBoundary = new ManeuverScheduleEvent.PhaseBoundary(
            scheduled.MotionId,
            scheduled.Generation,
            phaseIndex: 0);
        _ = scheduled.HandlePhaseBoundary(
            firstBoundary,
            firstBoundary.Generation,
            plan.Phases[0].EndsAt,
            out _);

        IReadOnlyList<AgendaEventProposal<ManeuverScheduleEvent>> proposals =
            scheduled.CreateRemainingEventProposals(
                new ShipId(27),
                static maneuverEvent => maneuverEvent);

        Assert.Equal(plan.Phases.Count - 1, proposals.Count);
        for (int index = 1; index < plan.Phases.Count; index++)
        {
            AgendaEventProposal<ManeuverScheduleEvent> proposal =
                proposals[index - 1];
            Assert.Equal(index, proposal.Order.LocalOrdinal);
            if (index < plan.Phases.Count - 1)
            {
                var boundary = Assert.IsType<ManeuverScheduleEvent.PhaseBoundary>(
                    proposal.Payload);
                Assert.Equal(index, boundary.PhaseIndex);
            }
            else
            {
                Assert.IsType<ManeuverScheduleEvent.Complete>(proposal.Payload);
            }
        }
    }

    [Fact]
    public void CompletedManeuverProducesNoRemainingEventProposals()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(28),
            new EventGeneration(29),
            plan);
        var completion = new ManeuverScheduleEvent.Complete(
            scheduled.MotionId,
            scheduled.Generation);
        AdvanceToFinalPhase(scheduled);
        _ = scheduled.HandleCompletion(
            completion,
            completion.Generation,
            plan.EndsAt,
            out _);

        IReadOnlyList<AgendaEventProposal<ManeuverScheduleEvent>> proposals =
            scheduled.CreateRemainingEventProposals(
                new ShipId(30),
                static maneuverEvent => maneuverEvent);

        Assert.Empty(proposals);
    }

    [Fact]
    public void EventProposalWrapperIsRequired()
    {
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(31),
            new EventGeneration(32),
            Plan());

        Assert.Throws<ArgumentNullException>(() =>
            scheduled.CreateRemainingEventProposals<object>(
                new ShipId(33),
                null!));
    }

    [Fact]
    public void ExactCompletionMaterializesThePlanEndpoint()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(3),
            new EventGeneration(7),
            plan);
        var completion = new ManeuverScheduleEvent.Complete(
            scheduled.MotionId,
            scheduled.Generation);
        AdvanceToFinalPhase(scheduled);

        ScheduledEventDisposition disposition = scheduled.HandleCompletion(
            completion,
            completion.Generation,
            plan.EndsAt,
            out ShipKinematicState? materialized);

        Assert.Equal(ScheduledEventDisposition.Applied, disposition);
        Assert.Equal(plan.StateAt(plan.EndsAt), materialized);
        Assert.True(scheduled.IsComplete);
        Assert.Null(scheduled.CurrentPhase);
    }

    [Fact]
    public void ExactInternalBoundaryMaterializesAndAdvancesTheCursor()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(12),
            new EventGeneration(13),
            plan);
        ManeuverScheduledPhase first = plan.Phases[0];
        var boundary = new ManeuverScheduleEvent.PhaseBoundary(
            scheduled.MotionId,
            scheduled.Generation,
            phaseIndex: 0);

        ScheduledEventDisposition disposition = scheduled.HandlePhaseBoundary(
            boundary,
            boundary.Generation,
            first.EndsAt,
            out ShipKinematicState? materialized);

        Assert.Equal(ScheduledEventDisposition.Applied, disposition);
        Assert.Equal(plan.StateAt(first.EndsAt), materialized);
        Assert.Equal(1, scheduled.CurrentPhaseIndex);
        Assert.Equal(plan.Phases[1], scheduled.CurrentPhase);
        Assert.False(scheduled.IsComplete);
    }

    [Fact]
    public void SkippedInternalBoundaryDoesNotMaterializeOrAdvance()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(13),
            new EventGeneration(14),
            plan);
        var skipped = new ManeuverScheduleEvent.PhaseBoundary(
            scheduled.MotionId,
            scheduled.Generation,
            phaseIndex: 1);

        ScheduledEventDisposition disposition = scheduled.HandlePhaseBoundary(
            skipped,
            skipped.Generation,
            plan.Phases[1].EndsAt,
            out ShipKinematicState? materialized);

        Assert.Equal(
            ScheduledEventDisposition.IgnoredStateMismatch,
            disposition);
        Assert.Null(materialized);
        Assert.Equal(0, scheduled.CurrentPhaseIndex);
        Assert.Equal(plan.Phases[0], scheduled.CurrentPhase);
    }

    [Fact]
    public void DuplicateInternalBoundaryDoesNotReopenEarlierPhase()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(14),
            new EventGeneration(15),
            plan);
        var boundary = new ManeuverScheduleEvent.PhaseBoundary(
            scheduled.MotionId,
            scheduled.Generation,
            phaseIndex: 0);
        _ = scheduled.HandlePhaseBoundary(
            boundary,
            boundary.Generation,
            plan.Phases[0].EndsAt,
            out _);

        ScheduledEventDisposition disposition = scheduled.HandlePhaseBoundary(
            boundary,
            boundary.Generation,
            plan.Phases[0].EndsAt,
            out ShipKinematicState? materialized);

        Assert.Equal(
            ScheduledEventDisposition.IgnoredStateMismatch,
            disposition);
        Assert.Null(materialized);
        Assert.Equal(1, scheduled.CurrentPhaseIndex);
    }

    [Fact]
    public void MistimedInternalBoundaryDoesNotMaterializeOrAdvance()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(18),
            new EventGeneration(20),
            plan);
        var boundary = new ManeuverScheduleEvent.PhaseBoundary(
            scheduled.MotionId,
            scheduled.Generation,
            phaseIndex: 0);
        var early = new SimulationTime(
            plan.Phases[0].EndsAt.Milliseconds - 1);

        ScheduledEventDisposition disposition = scheduled.HandlePhaseBoundary(
            boundary,
            boundary.Generation,
            early,
            out ShipKinematicState? materialized);

        Assert.Equal(
            ScheduledEventDisposition.IgnoredStateMismatch,
            disposition);
        Assert.Null(materialized);
        Assert.Equal(0, scheduled.CurrentPhaseIndex);
    }

    [Fact]
    public void CompletionCannotSkipInternalBoundaries()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(15),
            new EventGeneration(16),
            plan);
        var completion = new ManeuverScheduleEvent.Complete(
            scheduled.MotionId,
            scheduled.Generation);

        ScheduledEventDisposition disposition = scheduled.HandleCompletion(
            completion,
            completion.Generation,
            plan.EndsAt,
            out ShipKinematicState? materialized);

        Assert.Equal(
            ScheduledEventDisposition.IgnoredStateMismatch,
            disposition);
        Assert.Null(materialized);
        Assert.Equal(0, scheduled.CurrentPhaseIndex);
        Assert.False(scheduled.IsComplete);
    }

    [Fact]
    public void DuplicateCompletionDoesNotMaterializeAgain()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(19),
            new EventGeneration(21),
            plan);
        var completion = new ManeuverScheduleEvent.Complete(
            scheduled.MotionId,
            scheduled.Generation);
        AdvanceToFinalPhase(scheduled);
        _ = scheduled.HandleCompletion(
            completion,
            completion.Generation,
            plan.EndsAt,
            out _);

        ScheduledEventDisposition disposition = scheduled.HandleCompletion(
            completion,
            completion.Generation,
            plan.EndsAt,
            out ShipKinematicState? materialized);

        Assert.Equal(
            ScheduledEventDisposition.IgnoredStateMismatch,
            disposition);
        Assert.Null(materialized);
        Assert.True(scheduled.IsComplete);
    }

    [Fact]
    public void StaleInternalBoundaryDoesNotMaterializeOrAdvance()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var scheduled = new ScheduledTerminalManeuver(
            new MotionId(16),
            new EventGeneration(18),
            plan);
        var stale = new ManeuverScheduleEvent.PhaseBoundary(
            scheduled.MotionId,
            new EventGeneration(17),
            phaseIndex: 0);

        ScheduledEventDisposition disposition = scheduled.HandlePhaseBoundary(
            stale,
            stale.Generation,
            plan.Phases[0].EndsAt,
            out ShipKinematicState? materialized);

        Assert.Equal(
            ScheduledEventDisposition.IgnoredStaleGeneration,
            disposition);
        Assert.Null(materialized);
        Assert.Equal(0, scheduled.CurrentPhaseIndex);
    }

    [Fact]
    public void ReplacedGenerationMakesEarlierCompletionANoOp()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var active = new ScheduledTerminalManeuver(
            new MotionId(4),
            new EventGeneration(8),
            plan);
        var stale = new ManeuverScheduleEvent.Complete(
            active.MotionId,
            new EventGeneration(7));

        ScheduledEventDisposition disposition = active.HandleCompletion(
            stale,
            stale.Generation,
            plan.EndsAt,
            out ShipKinematicState? materialized);

        Assert.Equal(
            ScheduledEventDisposition.IgnoredStaleGeneration,
            disposition);
        Assert.Null(materialized);
    }

    [Fact]
    public void AgendaAndPayloadGenerationMismatchDoesNotMaterialize()
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var active = new ScheduledTerminalManeuver(
            new MotionId(5),
            new EventGeneration(9),
            plan);
        var completion = new ManeuverScheduleEvent.Complete(
            active.MotionId,
            active.Generation);

        ScheduledEventDisposition disposition = active.HandleCompletion(
            completion,
            new EventGeneration(10),
            plan.EndsAt,
            out ShipKinematicState? materialized);

        Assert.Equal(
            ScheduledEventDisposition.IgnoredStateMismatch,
            disposition);
        Assert.Null(materialized);
    }

    [Theory]
    [InlineData(6, 7, 0)]
    [InlineData(6, 6, -1)]
    [InlineData(6, 6, 1)]
    public void WrongIdentityOrTimestampDoesNotMaterialize(
        ulong activeMotionId,
        ulong eventMotionId,
        long timeOffset)
    {
        ExecutableBoundedTerminalManeuverPlan plan = Plan();
        var active = new ScheduledTerminalManeuver(
            new MotionId(activeMotionId),
            new EventGeneration(11),
            plan);
        var completion = new ManeuverScheduleEvent.Complete(
            new MotionId(eventMotionId),
            active.Generation);
        var now = new SimulationTime(checked(
            (ulong)((long)plan.EndsAt.Milliseconds + timeOffset)));

        ScheduledEventDisposition disposition = active.HandleCompletion(
            completion,
            completion.Generation,
            now,
            out ShipKinematicState? materialized);

        Assert.Equal(
            ScheduledEventDisposition.IgnoredStateMismatch,
            disposition);
        Assert.Null(materialized);
    }

    [Fact]
    public void NegativePhaseIndexIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ManeuverScheduleEvent.PhaseBoundary(
                new MotionId(17),
                new EventGeneration(19),
                phaseIndex: -1));
    }

    private static void AdvanceToFinalPhase(
        ScheduledTerminalManeuver scheduled)
    {
        for (int index = 0; index < scheduled.Plan.Phases.Count - 1; index++)
        {
            var boundary = new ManeuverScheduleEvent.PhaseBoundary(
                scheduled.MotionId,
                scheduled.Generation,
                index);
            ScheduledEventDisposition disposition =
                scheduled.HandlePhaseBoundary(
                    boundary,
                    boundary.Generation,
                    scheduled.Plan.Phases[index].EndsAt,
                    out ShipKinematicState? materialized);
            Assert.Equal(ScheduledEventDisposition.Applied, disposition);
            Assert.NotNull(materialized);
        }
    }

    private static void BindPendingEvents(
        ScheduledTerminalManeuver scheduled,
        EventAgenda<ManeuverScheduleEvent> agenda,
        ShipId shipId)
    {
        IReadOnlyList<AgendaEventProposal<ManeuverScheduleEvent>> proposals =
            scheduled.CreateRemainingEventProposals(
                shipId,
                static maneuverEvent => maneuverEvent);
        AgendaCommitResult commit = AgendaCommitOwner.Commit(agenda, proposals);
        scheduled.BindPendingEventKeys(commit.EventKeys);
    }

    private static SimulationTime Inside(ManeuverScheduledPhase phase)
    {
        ulong duration = phase.EndsAt.Milliseconds - phase.StartsAt.Milliseconds;
        return new SimulationTime(
            phase.StartsAt.Milliseconds + (duration / 2));
    }

    private static ExecutableBoundedTerminalManeuverPlan Plan()
    {
        EffectiveShipManeuverCapability capability =
            new ShipManeuverCapability(
                baseMassKilograms: 10_000,
                new ManeuverAcceleration(10_000),
                customPassiveDeceleration: null,
                new ManeuverSpeed(30_000),
                new ManeuverSpeed(1_000_000),
                new ManeuverTurnRate(45_000),
                new SimulationDuration(10_000))
            .ResolveForMass(effectiveMassKilograms: 10_000);
        BoundedTerminalPlanSelection selection =
            BoundedTerminalManeuverPlanner.Select(
                new SimulationTime(100),
                new ShipKinematicState(
                    Position(0, 0),
                    ShipVelocity.Zero,
                    new ShipHeading(90_000)),
                Position(25, 0),
                new ShipHeading(90_000),
                capability,
                ManeuverObjective.FastestArrival);
        return Assert.IsType<ExecutableBoundedTerminalManeuverPlan>(
            selection.ExecutablePlan);
    }

    private static SystemPosition Position(long x, long y) =>
        new(
            new SystemId(1),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));
}
