using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionTerminalManeuverTests
{
    [Fact]
    public void ZeroDistanceRequestedHeadingTurnsBeforeCompletingOrder()
    {
        GameSession session = CreateSession();
        var requestedHeading = new ShipHeading(90_000);

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(0, 0),
                OrderPlacement.ReplaceAll,
                requestedHeading));

        GameShipSnapshot started = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(BoundedTerminalPlanKind.StationaryTurn, started.Maneuver?.PlanKind);
        Assert.Equal(ShipOrderStatus.Active, started.CurrentOrder?.Status);

        AdvanceThroughManeuver(session);

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            new ShipKinematicState(
                Assert.IsType<SystemPosition>(completed.Position),
                completed.Velocity,
                completed.Heading),
            GameSessionTestFixture.Position(0, 0),
            requestedHeading).IsSatisfied);
    }

    [Fact]
    public void DiagnosticSnapshotExposesManeuverCapabilityRevision()
    {
        GameSession session = CreateSession();
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100_000, 0),
                OrderPlacement.ReplaceAll));

        GameShipSnapshot ship = Assert.Single(session.CaptureSnapshot().Ships);

        Assert.Equal(ShipManeuverCapabilityRevision.Initial, ship.ManeuverCapabilityRevision);
        Assert.NotNull(ship.Maneuver);
        Assert.NotNull(ship.Maneuver.CurrentPhase);
        Assert.NotNull(ship.Maneuver.NextBoundary);
    }

    [Fact]
    public void ReplacementDuringCruisePreservesSpeedThenUsesForcedDropoutBrake()
    {
        GameSession session = CreateSession();
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100_000, 0),
                OrderPlacement.ReplaceAll));
        TerminalManeuverSnapshot accelerating = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        session.AdvanceTo(accelerating.NextBoundary!.Timestamp);
        TerminalManeuverSnapshot spooling = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        session.AdvanceTo(spooling.NextBoundary!.Timestamp);
        TerminalManeuverSnapshot cruising = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        Assert.Equal(ManeuverPhaseKind.CruiseTravel, cruising.CurrentPhase?.Kind);
        session.AdvanceTo(new SimulationTime(50_000));

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(1_000_000, 0),
                OrderPlacement.ReplaceAll));

        GameShipSnapshot interrupted = Assert.Single(session.CaptureSnapshot().Ships);
        TerminalManeuverSnapshot dropout = Assert.IsType<TerminalManeuverSnapshot>(
            interrupted.Maneuver);
        Assert.Equal(
            BoundedTerminalPlanKind.ForcedCruiseDropoutTerminal,
            dropout.PlanKind);
        Assert.Equal(
            ManeuverPhaseKind.CruiseDropoutBrake,
            dropout.CurrentPhase?.Kind);
        Assert.Equal(new ShipVelocity(1_000_000, 0), interrupted.Velocity);
        Assert.Equal(new SimulationTime(50_000), dropout.CurrentPhase?.StartsAt);
        Assert.Equal(new SimulationTime(85_000), dropout.CurrentPhase?.EndsAt);
        ShipCruiseDroppedOutFact droppedOut =
            Assert.IsType<ShipCruiseDroppedOutFact>(
                Assert.Single(
                    session.ReadFactsAfter(null, 32).Facts,
                    envelope => envelope.Fact is ShipCruiseDroppedOutFact).Fact);
        Assert.Equal(cruising.MotionId, droppedOut.MotionId);
        Assert.Equal(new ShipVelocity(1_000_000, 0), droppedOut.Velocity);
        Assert.Equal(new SimulationTime(50_000), droppedOut.DroppedOutAt);

        session.AdvanceTo(new SimulationTime(67_500));
        Assert.Equal(
            new ShipVelocity(650_000, 0),
            Assert.Single(session.CaptureSnapshot().Ships).Velocity);
        session.AdvanceTo(dropout.NextBoundary!.Timestamp);
        GameShipSnapshot atSubCruise = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Equal(new ShipVelocity(300_000, 0), atSubCruise.Velocity);
        Assert.Equal(
            ManeuverPhaseKind.MovingSpool,
            atSubCruise.Maneuver?.CurrentPhase?.Kind);

        AdvanceThroughManeuver(session);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            new ShipKinematicState(
                Assert.IsType<SystemPosition>(
                    Assert.Single(session.CaptureSnapshot().Ships).Position),
                Assert.Single(session.CaptureSnapshot().Ships).Velocity,
                Assert.Single(session.CaptureSnapshot().Ships).Heading),
            GameSessionTestFixture.Position(1_000_000, 0),
            requestedHeading: null).IsSatisfied);
        Assert.DoesNotContain(
            session.EventRecords,
            record => record.Generation == cruising.Generation
                && record.Timestamp > new SimulationTime(50_000));
    }

    [Fact]
    public void CheckpointRestoreDuringForcedDropoutPreservesContinuation()
    {
        GameSession uninterrupted = StartForcedDropoutReplacement();
        uninterrupted.AdvanceTo(new SimulationTime(67_500));
        int recordedEventCountAtCheckpoint = uninterrupted.EventRecords.Count;
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(
            uninterrupted.CaptureCheckpoint().Value);
        GameSession restored = Assert.IsType<GameSession>(
            GameSession.RestoreCheckpoint(checkpoint).Value);

        AdvanceThroughManeuver(uninterrupted);
        AdvanceThroughManeuver(restored);

        GameShipSnapshot expected = Assert.Single(
            uninterrupted.CaptureSnapshot().Ships);
        GameShipSnapshot actual = Assert.Single(
            restored.CaptureSnapshot().Ships);
        Assert.Equal(expected.SpatialState, actual.SpatialState);
        Assert.Equal(expected.Velocity, actual.Velocity);
        Assert.Equal(expected.Heading, actual.Heading);
        Assert.Equal(expected.CurrentOrder, actual.CurrentOrder);
        Assert.Equal(
            uninterrupted.EventRecords.Skip(recordedEventCountAtCheckpoint),
            restored.EventRecords);
        Assert.Equal(
            uninterrupted.ReadFactsAfter(null, 64).Facts,
            restored.ReadFactsAfter(null, 64).Facts);
    }

    [Fact]
    public void ReplacementDuringSpoolRequiresOneFreshCompleteSpool()
    {
        GameSession session = CreateSession();
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100_000, 0),
                OrderPlacement.ReplaceAll));
        TerminalManeuverSnapshot accelerating = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        session.AdvanceTo(accelerating.NextBoundary!.Timestamp);
        TerminalManeuverSnapshot originalSpool = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        Assert.Equal(ManeuverPhaseKind.MovingSpool, originalSpool.CurrentPhase?.Kind);
        session.AdvanceTo(new SimulationTime(35_000));

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(200_000, 0),
                OrderPlacement.ReplaceAll));

        TerminalManeuverSnapshot replacement = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        Assert.NotEqual(originalSpool.MotionId, replacement.MotionId);
        Assert.Equal(originalSpool.Generation.Next(), replacement.Generation);
        Assert.Equal(BoundedTerminalPlanKind.CruiseTerminal, replacement.PlanKind);
        Assert.Equal(ManeuverPhaseKind.MovingSpool, replacement.CurrentPhase?.Kind);
        Assert.Equal(new SimulationTime(35_000), replacement.CurrentPhase?.StartsAt);
        Assert.Equal(new SimulationTime(45_000), replacement.CurrentPhase?.EndsAt);

        session.AdvanceTo(originalSpool.NextBoundary!.Timestamp);
        Assert.Equal(
            ManeuverPhaseKind.MovingSpool,
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver?.CurrentPhase?.Kind);
        Assert.DoesNotContain(
            session.ReadFactsAfter(null, 32).Facts,
            envelope => envelope.Fact is ShipCruiseEnteredFact);

        session.AdvanceTo(replacement.NextBoundary!.Timestamp);
        ShipCruiseEnteredFact entered = Assert.IsType<ShipCruiseEnteredFact>(
            Assert.Single(
                session.ReadFactsAfter(null, 32).Facts,
                envelope => envelope.Fact is ShipCruiseEnteredFact).Fact);
        Assert.Equal(replacement.MotionId, entered.MotionId);
    }

    [Fact]
    public void CruiseBoundariesPublishEntryAndPlannedDropoutFacts()
    {
        GameSession session = CreateSession();

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100_000, 0),
                OrderPlacement.ReplaceAll));

        TerminalManeuverSnapshot started = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        Assert.Equal(BoundedTerminalPlanKind.CruiseTerminal, started.PlanKind);
        Assert.Equal(ManeuverPhaseKind.Accelerate, started.CurrentPhase?.Kind);

        session.AdvanceTo(started.NextBoundary!.Timestamp);
        TerminalManeuverSnapshot spooling = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        Assert.Equal(ManeuverPhaseKind.MovingSpool, spooling.CurrentPhase?.Kind);

        session.AdvanceTo(spooling.NextBoundary!.Timestamp);
        GameShipSnapshot cruisingShip = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Equal(
            ManeuverPhaseKind.CruiseTravel,
            cruisingShip.Maneuver?.CurrentPhase?.Kind);
        Assert.Equal(new ShipVelocity(1_000_000, 0), cruisingShip.Velocity);
        ShipCruiseEnteredFact entered = Assert.IsType<ShipCruiseEnteredFact>(
            Assert.Single(
                session.ReadFactsAfter(null, 32).Facts,
                envelope => envelope.Fact is ShipCruiseEnteredFact).Fact);
        Assert.Equal(started.MotionId, entered.MotionId);
        Assert.Equal(cruisingShip.Position, entered.Position);
        Assert.Equal(cruisingShip.Velocity, entered.Velocity);
        Assert.Equal(spooling.NextBoundary.Timestamp, entered.EnteredAt);
        Assert.Equal(cruisingShip.CurrentOrder?.Id, entered.OrderId);

        session.AdvanceTo(cruisingShip.Maneuver!.NextBoundary!.Timestamp);
        GameShipSnapshot droppedOutShip = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.Equal(
            ManeuverPhaseKind.ActiveBrake,
            droppedOutShip.Maneuver?.CurrentPhase?.Kind);
        Assert.Equal(new ShipVelocity(300_000, 0), droppedOutShip.Velocity);
        ShipCruiseDroppedOutFact droppedOut =
            Assert.IsType<ShipCruiseDroppedOutFact>(
                Assert.Single(
                    session.ReadFactsAfter(null, 32).Facts,
                    envelope => envelope.Fact is ShipCruiseDroppedOutFact).Fact);
        Assert.Equal(started.MotionId, droppedOut.MotionId);
        Assert.Equal(droppedOutShip.Position, droppedOut.Position);
        Assert.Equal(droppedOutShip.Velocity, droppedOut.Velocity);
        TerminalManeuverSnapshot braking = Assert.IsType<TerminalManeuverSnapshot>(
            droppedOutShip.Maneuver);
        Assert.Equal(braking.CurrentPhase?.StartsAt, droppedOut.DroppedOutAt);
        Assert.Equal(droppedOutShip.CurrentOrder?.Id, droppedOut.OrderId);
    }

    [Fact]
    public void CheckpointRestoreDuringCruisePreservesTransitionFactContinuity()
    {
        GameSession uninterrupted = CreateSession();
        uninterrupted.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100_000, 0),
                OrderPlacement.ReplaceAll));
        TerminalManeuverSnapshot accelerating = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(uninterrupted.CaptureSnapshot().Ships).Maneuver);
        uninterrupted.AdvanceTo(accelerating.NextBoundary!.Timestamp);
        TerminalManeuverSnapshot spooling = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(uninterrupted.CaptureSnapshot().Ships).Maneuver);
        uninterrupted.AdvanceTo(spooling.NextBoundary!.Timestamp);
        Assert.Equal(
            ManeuverPhaseKind.CruiseTravel,
            Assert.Single(uninterrupted.CaptureSnapshot().Ships)
                .Maneuver?.CurrentPhase?.Kind);
        int recordedEventCountAtCheckpoint = uninterrupted.EventRecords.Count;

        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(
            uninterrupted.CaptureCheckpoint().Value);
        GameSession restored = Assert.IsType<GameSession>(
            GameSession.RestoreCheckpoint(checkpoint).Value);

        AdvanceThroughManeuver(uninterrupted);
        AdvanceThroughManeuver(restored);

        GameShipSnapshot expected = Assert.Single(
            uninterrupted.CaptureSnapshot().Ships);
        GameShipSnapshot actual = Assert.Single(
            restored.CaptureSnapshot().Ships);
        Assert.Equal(expected.SpatialState, actual.SpatialState);
        Assert.Equal(expected.Velocity, actual.Velocity);
        Assert.Equal(expected.Heading, actual.Heading);
        Assert.Equal(expected.CurrentOrder, actual.CurrentOrder);
        Assert.Equal(
            uninterrupted.EventRecords.Skip(recordedEventCountAtCheckpoint),
            restored.EventRecords);
        Assert.Equal(
            uninterrupted.ReadFactsAfter(null, 64).Facts,
            restored.ReadFactsAfter(null, 64).Facts);
        Assert.Single(
            restored.ReadFactsAfter(null, 64).Facts,
            envelope => envelope.Fact is ShipCruiseEnteredFact);
        Assert.Single(
            restored.ReadFactsAfter(null, 64).Facts,
            envelope => envelope.Fact is ShipCruiseDroppedOutFact);
    }

    [Fact]
    public void OrdinaryMoveUsesAnalyticManeuverAndCompletesOnlyAtTerminalBoundary()
    {
        GameSession session = CreateSession();

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0),
                OrderPlacement.ReplaceAll));

        GameShipSnapshot started = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Null(started.Motion);
        Assert.NotNull(started.Maneuver);
        Assert.Equal(ManeuverObjective.FastestArrival, started.Maneuver!.Objective);
        Assert.NotNull(started.CurrentOrder);

        bool observedInternalBoundary = false;
        while (Assert.Single(session.CaptureSnapshot().Ships).Maneuver is { } maneuver)
        {
            ManeuverBoundaryDiagnostic boundary = Assert.IsType<ManeuverBoundaryDiagnostic>(
                maneuver.NextBoundary);
            bool completes = boundary.Payload is ManeuverScheduleEvent.Complete;

            session.AdvanceTo(boundary.Timestamp);

            GameShipSnapshot afterBoundary = Assert.Single(
                session.CaptureSnapshot().Ships);
            if (completes)
            {
                Assert.Null(afterBoundary.Maneuver);
                Assert.Equal(
                    ShipOrderStatus.Completed,
                    afterBoundary.CurrentOrder?.Status);
                Assert.Equal(
                    GameSessionTestFixture.Position(100, 0),
                    afterBoundary.Position);
            }
            else
            {
                observedInternalBoundary = true;
                Assert.NotNull(afterBoundary.Maneuver);
                Assert.NotNull(afterBoundary.CurrentOrder);
                GameFact[] semanticFacts = session.ReadFactsAfter(null, 64).Facts
                    .Select(static envelope => envelope.Fact)
                    .ToArray();
                Assert.Single(semanticFacts.OfType<ShipLocalMotionStartedFact>());
                Assert.Empty(semanticFacts.OfType<ShipLocalMotionEndedFact>());
            }
        }

        Assert.True(observedInternalBoundary);
        GameFact[] completedFacts = session.ReadFactsAfter(null, 64).Facts
            .Select(static envelope => envelope.Fact)
            .ToArray();
        Assert.Single(completedFacts.OfType<ShipLocalMotionStartedFact>());
        ShipLocalMotionEndedFact arrival = Assert.Single(
            completedFacts.OfType<ShipLocalMotionEndedFact>());
        Assert.Equal(LocalMotionEndReason.Arrived, arrival.Reason);
        Assert.Equal(GameSessionTestFixture.Position(100, 0), arrival.FinalPosition);
        Assert.All(
            session.EventRecords,
            record => Assert.IsType<SpatialMovementEvent.Maneuver>(
                Assert.IsType<GameEventKind.SpatialMovement>(record.Kind).Event));
    }

    [Fact]
    public void ReplacementCancelsOldBoundariesAndContinuesWithNewGeneration()
    {
        GameSession session = CreateSession();
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0),
                OrderPlacement.ReplaceAll));
        TerminalManeuverSnapshot original = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        session.AdvanceTo(new SimulationTime(1_000));

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(0, 100),
                OrderPlacement.ReplaceAll));

        GameShipSnapshot replaced = Assert.Single(session.CaptureSnapshot().Ships);
        TerminalManeuverSnapshot replacement = Assert.IsType<TerminalManeuverSnapshot>(
            replaced.Maneuver);
        Assert.NotEqual(original.MotionId, replacement.MotionId);
        Assert.Equal(original.Generation.Next(), replacement.Generation);
        Assert.Equal(new ShipOrderId(2), replaced.CurrentOrder!.Id);
        GameFact[] replacementFacts = session.ReadFactsAfter(null, 64).Facts
            .Select(static envelope => envelope.Fact)
            .ToArray();
        Assert.Equal(
            [original.MotionId, replacement.MotionId],
            replacementFacts
                .OfType<ShipLocalMotionStartedFact>()
                .Select(static fact => fact.Motion.Id));
        ShipLocalMotionEndedFact interrupted = Assert.Single(
            replacementFacts.OfType<ShipLocalMotionEndedFact>());
        Assert.Equal(original.MotionId, interrupted.Motion.Id);
        Assert.Equal(LocalMotionEndReason.ReplacedByCommand, interrupted.Reason);

        AdvanceThroughManeuver(session);

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(GameSessionTestFixture.Position(0, 100), completed.Position);
        Assert.Equal(
            ShipOrderStatus.Completed,
            completed.CurrentOrder?.Status);
        Assert.DoesNotContain(
            session.EventRecords,
            record => record.Generation == original.Generation
                && record.Timestamp > new SimulationTime(1_000));
    }

    [Fact]
    public void CheckpointRestoreContinuesAnalyticManeuverExactly()
    {
        GameSession uninterrupted = CreateSession();
        uninterrupted.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0),
                OrderPlacement.ReplaceAll));
        uninterrupted.AdvanceTo(new SimulationTime(1_000));
        Assert.NotNull(Assert.Single(
            uninterrupted.CaptureSnapshot().Ships).Maneuver);

        CheckpointResult<GameSessionCheckpoint> capture =
            uninterrupted.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.ToString());
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(
            capture.Value);
        GameSession restored = Assert.IsType<GameSession>(
            GameSession.RestoreCheckpoint(checkpoint).Value);

        AdvanceThroughManeuver(uninterrupted);
        AdvanceThroughManeuver(restored);

        GameShipSnapshot expected = Assert.Single(
            uninterrupted.CaptureSnapshot().Ships);
        GameShipSnapshot actual = Assert.Single(restored.CaptureSnapshot().Ships);
        Assert.Equal(expected.SpatialState, actual.SpatialState);
        Assert.Equal(expected.Velocity, actual.Velocity);
        Assert.Equal(expected.Heading, actual.Heading);
        Assert.Equal(expected.CurrentOrder, actual.CurrentOrder);
        Assert.Equal(uninterrupted.EventRecords, restored.EventRecords);
        Assert.Equal(
            uninterrupted.ReadFactsAfter(null, 64).Facts,
            restored.ReadFactsAfter(null, 64).Facts);
    }

    [Fact]
    public void CollinearMultiLegRouteFliesThroughWaypointOnOneMotion()
    {
        GameSession session = GameSessionTestFixture.Create(
            navigation: new TwoLegPlanner());

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0),
                OrderPlacement.ReplaceAll));

        GameShipSnapshot started = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Null(started.Motion);
        TerminalManeuverSnapshot maneuver = Assert.IsType<TerminalManeuverSnapshot>(
            started.Maneuver);

        int remainingBoundaryLimit = 16;
        while (!session.ReadFactsAfter(null, 64).Facts.Any(
                   envelope => envelope.Fact is ShipWaypointArrivedFact))
        {
            Assert.True(remainingBoundaryLimit-- > 0);
            TerminalManeuverSnapshot current = Assert.IsType<TerminalManeuverSnapshot>(
                Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
            session.AdvanceTo(current.NextBoundary!.Timestamp);
        }

        GameShipSnapshot waypoint = Assert.Single(
            session.CaptureSnapshot().Ships);
        Assert.True(ManeuverArrival.IsFlyThroughWaypointReached(
            new ShipKinematicState(
                Assert.IsType<SystemPosition>(waypoint.Position),
                waypoint.Velocity,
                waypoint.Heading),
            GameSessionTestFixture.Position(50, 0)));
        Assert.NotEqual(ShipVelocity.Zero, waypoint.Velocity);
        Assert.Equal(maneuver.MotionId, waypoint.Maneuver?.MotionId);
        Assert.Equal(ShipOrderStatus.Active, waypoint.CurrentOrder?.Status);
        ShipWaypointArrivedFact arrival = Assert.IsType<ShipWaypointArrivedFact>(
            Assert.Single(
                session.ReadFactsAfter(null, 64).Facts,
                envelope => envelope.Fact is ShipWaypointArrivedFact).Fact);
        Assert.Equal(maneuver.MotionId, arrival.MotionId);
        Assert.Equal(GameSessionTestFixture.Position(50, 0), arrival.Waypoint);
        Assert.Equal(waypoint.Position, arrival.ReachedPosition);
        Assert.Equal(waypoint.CurrentOrder?.Id, arrival.OrderId);

        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            new ShipKinematicState(
                Assert.IsType<SystemPosition>(completed.Position),
                completed.Velocity,
                completed.Heading),
            GameSessionTestFixture.Position(100, 0),
            requestedHeading: null).IsSatisfied);
        GameFactEnvelope[] facts = session.ReadFactsAfter(null, 64).Facts.ToArray();
        Assert.Single(facts, envelope =>
            envelope.Fact is ShipLocalMotionStartedFact);
        Assert.Single(facts, envelope =>
            envelope.Fact is ShipLocalMotionEndedFact);
    }

    [Fact]
    public void NoncollinearMultiLegRouteAlignsVelocityWithOutgoingLegAtWaypoint()
    {
        GameSession session = GameSessionTestFixture.Create(
            navigation: new CornerTwoLegPlanner());

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 100),
                OrderPlacement.ReplaceAll));

        GameShipSnapshot started = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Null(started.Motion);
        Assert.NotNull(started.Maneuver);

        int remainingBoundaryLimit = 16;
        while (!session.ReadFactsAfter(null, 64).Facts.Any(
                   envelope => envelope.Fact is ShipWaypointArrivedFact))
        {
            Assert.True(remainingBoundaryLimit-- > 0);
            TerminalManeuverSnapshot current = Assert.IsType<TerminalManeuverSnapshot>(
                Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
            session.AdvanceTo(current.NextBoundary!.Timestamp);
        }

        GameShipSnapshot waypoint = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.NotEqual(ShipVelocity.Zero, waypoint.Velocity);
        Assert.Equal(
            new ShipHeading(270_000),
            ManeuverHeadingProjection.ResolveCourseHeading(
                waypoint.Velocity.MillimetersPerSecondX,
                waypoint.Velocity.MillimetersPerSecondY));
        Assert.True(ManeuverArrival.IsFlyThroughWaypointReached(
            new ShipKinematicState(
                Assert.IsType<SystemPosition>(waypoint.Position),
                waypoint.Velocity,
                waypoint.Heading),
            GameSessionTestFixture.Position(100, 0)));

        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            new ShipKinematicState(
                Assert.IsType<SystemPosition>(completed.Position),
                completed.Velocity,
                completed.Heading),
            GameSessionTestFixture.Position(100, 100),
            requestedHeading: null).IsSatisfied);
        GameFact[] facts = session.ReadFactsAfter(null, 64).Facts
            .Select(static envelope => envelope.Fact)
            .ToArray();
        Assert.Single(facts.OfType<ShipWaypointArrivedFact>());
        Assert.Single(facts.OfType<ShipLocalMotionStartedFact>());
        Assert.Single(facts.OfType<ShipLocalMotionEndedFact>());
    }

    [Fact]
    public void NoncollinearRouteAppliesRequestedHeadingAtTerminalDestination()
    {
        GameSession session = GameSessionTestFixture.Create(
            navigation: new CornerTwoLegPlanner());
        var requestedHeading = new ShipHeading(180_000);

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 100),
                OrderPlacement.ReplaceAll,
                requestedHeading));

        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            new ShipKinematicState(
                Assert.IsType<SystemPosition>(completed.Position),
                completed.Velocity,
                completed.Heading),
            GameSessionTestFixture.Position(100, 100),
            requestedHeading).IsSatisfied);
        Assert.Equal(requestedHeading, completed.CurrentOrder?.RequestedHeading);
        Assert.Single(
            session.ReadFactsAfter(null, 64).Facts,
            envelope => envelope.Fact is ShipLocalMotionStartedFact);
    }

    [Fact]
    public void MultipleCollinearWaypointsAdvanceOneOrderOnOneMotion()
    {
        GameSession session = GameSessionTestFixture.Create(
            navigation: new ThreeLegPlanner());
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0),
                OrderPlacement.ReplaceAll));
        MotionId motionId = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver).MotionId;

        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);

        ShipWaypointArrivedFact[] arrivals = session
            .ReadFactsAfter(null, 64)
            .Facts
            .Select(static envelope => envelope.Fact)
            .OfType<ShipWaypointArrivedFact>()
            .ToArray();
        Assert.Collection(
            arrivals,
            first =>
            {
                Assert.Equal(motionId, first.MotionId);
                Assert.Equal(GameSessionTestFixture.Position(25, 0), first.Waypoint);
            },
            second =>
            {
                Assert.Equal(motionId, second.MotionId);
                Assert.Equal(GameSessionTestFixture.Position(75, 0), second.Waypoint);
            });
        Assert.Equal(
            ShipOrderStatus.Completed,
            Assert.Single(session.CaptureSnapshot().Ships).CurrentOrder?.Status);
    }

    private static void AdvanceThroughManeuver(GameSession session)
    {
        int remainingBoundaryLimit = 16;
        while (Assert.Single(session.CaptureSnapshot().Ships).Maneuver is { } maneuver)
        {
            Assert.True(remainingBoundaryLimit-- > 0);
            session.AdvanceTo(Assert.IsType<ManeuverBoundaryDiagnostic>(
                maneuver.NextBoundary).Timestamp);
        }
    }

    [Fact]
    public void UnrepresentableFullThrustMoveUsesAnalyticReducedThrust()
    {
        ShipDesign design = ReducedThrustDesign();
        GameSession session = CreateSession(design);

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(2, 0),
                OrderPlacement.ReplaceAll));

        GameShipSnapshot started = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Null(started.Motion);
        Assert.Equal(
            BoundedTerminalPlanKind.ReducedThrustTerminal,
            started.Maneuver?.PlanKind);

        AdvanceThroughManeuver(session);

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            new ShipKinematicState(
                Assert.IsType<SystemPosition>(completed.Position),
                completed.Velocity,
                completed.Heading),
            GameSessionTestFixture.Position(2, 0),
            requestedHeading: null).IsSatisfied);
    }

    [Fact]
    public void RequestedHeadingCompletesAfterReducedThrustMove()
    {
        ShipDesign design = ReducedThrustDesign();
        GameSession session = CreateSession(design);
        var requestedHeading = new ShipHeading(90_000);

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(2, 0),
                OrderPlacement.ReplaceAll,
                requestedHeading));

        GameShipSnapshot started = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(
            BoundedTerminalPlanKind.ReducedThrustTerminal,
            started.Maneuver?.PlanKind);
        Assert.Equal(requestedHeading, started.CurrentOrder?.RequestedHeading);

        AdvanceThroughManeuver(session);

        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.Equal(ShipOrderStatus.Completed, completed.CurrentOrder?.Status);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            new ShipKinematicState(
                Assert.IsType<SystemPosition>(completed.Position),
                completed.Velocity,
                completed.Heading),
            GameSessionTestFixture.Position(2, 0),
            requestedHeading).IsSatisfied);
    }

    [Fact]
    public void ReplacementUsesOnlyTheNewRequestedHeading()
    {
        GameSession session = CreateSession();
        var originalHeading = new ShipHeading(90_000);
        var replacementHeading = new ShipHeading(180_000);
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100, 0),
                OrderPlacement.ReplaceAll,
                originalHeading));

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(50, 0),
                OrderPlacement.ReplaceAll,
                replacementHeading));

        Assert.Equal(
            replacementHeading,
            Assert.Single(session.CaptureSnapshot().Ships)
                .CurrentOrder?.RequestedHeading);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
        GameShipSnapshot completed = Assert.Single(session.CaptureSnapshot().Ships);
        Assert.True(ManeuverArrival.EvaluateTerminal(
            new ShipKinematicState(
                Assert.IsType<SystemPosition>(completed.Position),
                completed.Velocity,
                completed.Heading),
            GameSessionTestFixture.Position(50, 0),
            replacementHeading).IsSatisfied);
        ShipOrderTransitionFact[] transitions = session
            .ReadFactsAfter(null, 64)
            .Facts
            .Select(static envelope => envelope.Fact)
            .OfType<ShipOrderTransitionFact>()
            .ToArray();
        Assert.Contains(transitions, transition =>
            transition.NextStatus == ShipOrderStatus.Cancelled
            && transition.RequestedHeading == originalHeading);
        Assert.Contains(transitions, transition =>
            transition.NextStatus == ShipOrderStatus.Completed
            && transition.RequestedHeading == replacementHeading);
    }

    [Fact]
    public void CheckpointRestoreRetainsRequestedHeadingOrderIntent()
    {
        GameSession uninterrupted = CreateSession(ReducedThrustDesign());
        var requestedHeading = new ShipHeading(90_000);
        uninterrupted.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(2, 0),
                OrderPlacement.ReplaceAll,
                requestedHeading));
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(
            uninterrupted.CaptureCheckpoint().Value);

        GameSession restored = Assert.IsType<GameSession>(
            GameSession.RestoreCheckpoint(checkpoint).Value);

        Assert.Equal(
            requestedHeading,
            Assert.Single(restored.CaptureSnapshot().Ships)
                .CurrentOrder?.RequestedHeading);
        AdvanceThroughManeuver(uninterrupted);
        AdvanceThroughManeuver(restored);
        GameShipSnapshot expected = Assert.Single(
            uninterrupted.CaptureSnapshot().Ships);
        GameShipSnapshot actual = Assert.Single(restored.CaptureSnapshot().Ships);
        Assert.Equal(expected.SpatialState, actual.SpatialState);
        Assert.Equal(expected.Velocity, actual.Velocity);
        Assert.Equal(expected.Heading, actual.Heading);
        Assert.Equal(expected.CurrentOrder, actual.CurrentOrder);
        Assert.Equal(uninterrupted.EventRecords, restored.EventRecords);
    }

    [Fact]
    public void OrderTransitionFactRetainsRequestedHeading()
    {
        GameSession session = CreateSession(ReducedThrustDesign());
        var requestedHeading = new ShipHeading(90_000);

        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(2, 0),
                OrderPlacement.ReplaceAll,
                requestedHeading));

        ShipOrderTransitionFact transition = Assert.IsType<ShipOrderTransitionFact>(
            Assert.Single(
                session.ReadFactsAfter(null, 64).Facts,
                envelope => envelope.Fact is ShipOrderTransitionFact).Fact);
        Assert.Equal(requestedHeading, transition.RequestedHeading);
    }

    private static GameSession StartForcedDropoutReplacement()
    {
        GameSession session = CreateSession();
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(100_000, 0),
                OrderPlacement.ReplaceAll));
        TerminalManeuverSnapshot accelerating = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        session.AdvanceTo(accelerating.NextBoundary!.Timestamp);
        TerminalManeuverSnapshot spooling = Assert.IsType<TerminalManeuverSnapshot>(
            Assert.Single(session.CaptureSnapshot().Ships).Maneuver);
        session.AdvanceTo(spooling.NextBoundary!.Timestamp);
        session.AdvanceTo(new SimulationTime(50_000));
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.Destination(1_000_000, 0),
                OrderPlacement.ReplaceAll));
        return session;
    }

    private static GameSession CreateSession(ShipDesign? design = null)
    {
        design ??= GameSessionTestFixture.Design;
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [new InitialShipSetup(
                GameSessionTestFixture.Entity,
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory,
                GameSessionTestFixture.Principal,
                design,
                GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(
                new FacilityId(1),
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController,
                InitialShipOrderPolicy.NoInitialOrder,
                [design])],
            GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed,
            factRetentionCapacity: 64);
        return new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new ChebyshevLocalTravelTimeEstimator(100)));
    }

    private static ShipDesign ReducedThrustDesign() =>
        new(
            GameSessionTestFixture.Design.Id,
            "Reduced-thrust test ship",
            new ConstructionRecipe([], new Work(1)),
            new Quantity(10),
            new ShipManeuverCapability(
                baseMassKilograms: 10_000,
                new ManeuverAcceleration(1_000_000_000_000),
                customPassiveDeceleration: null,
                new ManeuverSpeed(30_000),
                new ManeuverSpeed(1_000_000),
                new ManeuverTurnRate(45_000),
                new SimulationDuration(10_000)));

    private sealed class TwoLegPlanner : ISpatialNavigationPlanner
    {
        public NavigationPlanResult Plan(NavigationRequest request)
        {
            var destination = Assert.IsType<NavigationDestination.Position>(
                request.Destination);
            var midpoint = new SystemPosition(
                request.Origin.SystemId,
                new SpatialPosition(
                    new SpatialCoordinate(50),
                    new SpatialCoordinate(0)));
            var duration = new SimulationDuration(50);
            return new NavigationPlanResult.Planned(new TravelPlan(
                request.Destination,
                [
                    new TravelLeg.Local(request.Origin, midpoint, duration),
                    new TravelLeg.Local(midpoint, destination.Value, duration),
                ]));
        }
    }

    private sealed class CornerTwoLegPlanner : ISpatialNavigationPlanner
    {
        public NavigationPlanResult Plan(NavigationRequest request)
        {
            var destination = Assert.IsType<NavigationDestination.Position>(
                request.Destination);
            var corner = new SystemPosition(
                request.Origin.SystemId,
                new SpatialPosition(
                    new SpatialCoordinate(100),
                    new SpatialCoordinate(0)));
            var duration = new SimulationDuration(100);
            return new NavigationPlanResult.Planned(new TravelPlan(
                request.Destination,
                [
                    new TravelLeg.Local(request.Origin, corner, duration),
                    new TravelLeg.Local(corner, destination.Value, duration),
                ]));
        }
    }

    private sealed class ThreeLegPlanner : ISpatialNavigationPlanner
    {
        public NavigationPlanResult Plan(NavigationRequest request)
        {
            var destination = Assert.IsType<NavigationDestination.Position>(
                request.Destination);
            SystemPosition first = GameSessionTestFixture.Position(25, 0);
            SystemPosition second = GameSessionTestFixture.Position(75, 0);
            var duration = new SimulationDuration(25);
            return new NavigationPlanResult.Planned(new TravelPlan(
                request.Destination,
                [
                    new TravelLeg.Local(request.Origin, first, duration),
                    new TravelLeg.Local(first, second, duration),
                    new TravelLeg.Local(second, destination.Value, duration),
                ]));
        }
    }
}
