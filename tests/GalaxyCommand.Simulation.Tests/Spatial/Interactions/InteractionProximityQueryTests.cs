using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionProximityQueryTests
{
    [Fact]
    public void ScopeIsExplicitDeduplicatedAndReturnedInShipOrder()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(4), Position(1));
        movement.Add(new ShipId(2), Position(1));
        movement.Add(new ShipId(3), Position(1));
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        IReadOnlyList<ShipId> result = InteractionProximityQuery.Find(view, new ShipId(1),
            [new ShipId(4), new ShipId(2), new ShipId(4), new ShipId(1), new ShipId(99)], 1);
        Assert.Equal<ShipId>([new ShipId(2), new ShipId(4)], result);
        Assert.Throws<NotSupportedException>(() => ((IList<ShipId>)result).Add(new ShipId(3)));
        Assert.Empty(InteractionProximityQuery.Find(view, new ShipId(1), [], ulong.MaxValue));
    }

    [Theory]
    [InlineData(500, 0)]
    [InlineData(0, 1)]
    public void LinearContactUsesContinuousPositions(ulong now, int expectedCount)
    {
        var movement = Pair(0);
        movement.CommitStartOrReplace(new ShipId(1), new TravelLeg.Local(Position(0), Position(2), new SimulationDuration(2000)),
            SimulationTime.Zero, static value => value);
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(new SimulationTime(now));
        Assert.Equal(Position(0), view.Get(new ShipId(1))!.Spatial.Position);
        Assert.Equal(expectedCount, InteractionProximityQuery.Find(view, new ShipId(1), [new ShipId(2)], 0).Count);
        Assert.Equal(expectedCount, InteractionProximityQuery.Find(view, new ShipId(2), [new ShipId(1)], 0).Count);
    }

    [Fact]
    public void InsertedBoundaryPreservesTheAnalyticCurveInsteadOfPublishedRounding()
    {
        var movement = Pair(0);
        ExecutableBoundedTerminalManeuverPlan plan = Move().WithAdditionalBoundaries([new SimulationTime(500)]);
        TerminalManeuverCommit<SpatialMovementEvent> commit = Start(movement, plan);
        Assert.Equal(ScheduledEventDisposition.Applied, movement.HandleEvent(commit.EventProposals[0].Payload,
            commit.Maneuver.Generation, new SimulationTime(500)));
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(new SimulationTime(500));
        Assert.Equal(Position(0), view.Get(new ShipId(1))!.Spatial.Position);
        Assert.Empty(InteractionProximityQuery.Find(view, new ShipId(1), [new ShipId(2)], 0));
    }

    [Fact]
    public void OriginalBoundaryUsesCommittedResetAfterCompletion()
    {
        var movement = Pair(10);
        ExecutableBoundedTerminalManeuverPlan plan = Move();
        TerminalManeuverCommit<SpatialMovementEvent> commit = Start(movement, plan);
        Assert.Empty(InteractionProximityQuery.Find(movement.CaptureInteractionReadView(new SimulationTime(plan.EndsAt.Milliseconds - 1)),
            new ShipId(1), [new ShipId(2)], 0));
        Assert.Single(InteractionProximityQuery.Find(movement.CaptureInteractionReadView(plan.EndsAt),
            new ShipId(1), [new ShipId(2)], 0));
        foreach (var proposal in commit.EventProposals)
            movement.HandleEvent(proposal.Payload, commit.Maneuver.Generation, proposal.Timestamp);
        Assert.Single(InteractionProximityQuery.Find(movement.CaptureInteractionReadView(plan.EndsAt),
            new ShipId(1), [new ShipId(2)], 0));
    }

    [Fact]
    public void TwoAnalyticParticipantsUseTheirExactRelativeCurves()
    {
        var movement = Pair(0);
        Start(movement, Move());
        movement.CommitStartTerminalManeuver(new ShipId(2), Move(), ManeuverObjective.FastestArrival,
            SimulationTime.Zero, static value => value);
        Assert.Single(InteractionProximityQuery.Find(movement.CaptureInteractionReadView(new SimulationTime(500)),
            new ShipId(1), [new ShipId(2)], 0));
    }

    [Fact]
    public void StableViewSurvivesRemovalWithoutChangingResults()
    {
        var movement = Pair(0);
        InteractionSpatialReadView before = movement.CaptureInteractionReadView(SimulationTime.Zero);
        movement.CommitRemove(new ShipId(2), SimulationTime.Zero);
        Assert.Single(InteractionProximityQuery.Find(before, new ShipId(1), [new ShipId(2)], 0));
        Assert.Empty(InteractionProximityQuery.Find(movement.CaptureInteractionReadView(SimulationTime.Zero),
            new ShipId(1), [new ShipId(2)], 0));
    }

    [Fact]
    public void MissingDifferentSystemAndTransitParticipantsAreExcluded()
    {
        var movement = Pair(0);
        movement.Add(new ShipId(3), Position(0, 2));
        movement.CommitStartConnector(new ShipId(2), new TravelLeg.Connector(new TransitConnectionId(1),
            Position(0), Position(0, 2), new SimulationDuration(10)), SimulationTime.Zero, static value => value);
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        Assert.Empty(InteractionProximityQuery.Find(view, new ShipId(1), [new ShipId(2), new ShipId(3)], ulong.MaxValue));
        Assert.Empty(InteractionProximityQuery.Find(view, new ShipId(2), [new ShipId(1)], ulong.MaxValue));
        Assert.Empty(InteractionProximityQuery.Find(view, new ShipId(99), [new ShipId(1)], ulong.MaxValue));
    }

    [Fact]
    public void EuclideanRangeIsInclusiveAndSafeAtCoordinateLimits()
    {
        var movement = Pair(long.MaxValue);
        movement.Add(new ShipId(3), new SystemPosition(new SystemId(1),
            new SpatialPosition(new SpatialCoordinate(3), new SpatialCoordinate(4))));
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        Assert.Single(InteractionProximityQuery.Find(view, new ShipId(1), [new ShipId(3)], 5));
        Assert.Empty(InteractionProximityQuery.Find(view, new ShipId(1), [new ShipId(3)], 4));
        var extreme = new SpatialMovement();
        extreme.Add(new ShipId(1), new SystemPosition(new SystemId(1),
            new SpatialPosition(new SpatialCoordinate(long.MinValue), new SpatialCoordinate(long.MinValue))));
        extreme.Add(new ShipId(2), new SystemPosition(new SystemId(1),
            new SpatialPosition(new SpatialCoordinate(long.MaxValue), new SpatialCoordinate(long.MaxValue))));
        Assert.Empty(InteractionProximityQuery.Find(extreme.CaptureInteractionReadView(SimulationTime.Zero),
            new ShipId(1), [new ShipId(2)], ulong.MaxValue));
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        InteractionSpatialReadView view = Pair(0).CaptureInteractionReadView(SimulationTime.Zero);
        Assert.Throws<ArgumentNullException>(() => InteractionProximityQuery.Find(null!, new ShipId(1), [], 0));
        Assert.Throws<ArgumentNullException>(() => InteractionProximityQuery.Find(view, new ShipId(1), null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InteractionProximityQuery.Find(view, default, [], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InteractionProximityQuery.Find(view, new ShipId(1), [default], 0));
    }

    [Fact]
    public void CompletionReceiptDoesNotReplayPastSweptContactAsInstantProximity()
    {
        var movement = Pair(10);
        LocalMotionCommit<SpatialMovementEvent> commit = movement.CommitStartOrReplace(new ShipId(1),
            new TravelLeg.Local(Position(0), Position(20), new SimulationDuration(1)), SimulationTime.Zero, static value => value);
        movement.HandleEvent(commit.EventProposal!.Payload, commit.Motion!.Generation, new SimulationTime(1));
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(new SimulationTime(1));
        Assert.NotNull(view.Get(new ShipId(1))!.Completion);
        Assert.Empty(InteractionProximityQuery.Find(view, new ShipId(1), [new ShipId(2)], 0));
    }

    [Fact]
    public void MissingAnalyticGeometryIsRejectedRatherThanUsingPublishedPosition()
    {
        var movement = Pair(0);
        Start(movement, Move());
        InteractionSpatialReadView captured = movement.CaptureInteractionReadView(new SimulationTime(500));
        var incomplete = new InteractionSpatialReadView(captured.Timestamp,
            [captured.Get(new ShipId(1))! with { ManeuverPlan = null }, captured.Get(new ShipId(2))!]);
        Assert.Throws<InvalidOperationException>(() => InteractionProximityQuery.Find(incomplete, new ShipId(1), [new ShipId(2)], 0));
        Assert.Throws<InvalidOperationException>(() => InteractionProximityQuery.Prepare(incomplete));
    }

    [Fact]
    public void ScopePermutationsAndBatchLayoutsPreserveTheReferenceResult()
    {
        var movement = Pair(0);
        movement.Add(new ShipId(3), Position(1));
        movement.Add(new ShipId(4), Position(2));
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        ShipId[] scope = [new ShipId(2), new ShipId(3), new ShipId(4), new ShipId(3)];
        IReadOnlyList<ShipId> expected = InteractionProximityQuery.Find(view, new ShipId(1), scope, 1);
        Assert.Equal(expected, InteractionProximityQuery.Find(view, new ShipId(1), scope.Reverse(), 1));
        PreparedInteractionProximityView prepared = InteractionProximityQuery.Prepare(view);
        Assert.Equal(expected, prepared.PrepareScope(scope.Reverse()).Find(new ShipId(1), 1));
        foreach (int size in new[] { 1, 2, 3 })
        {
            ShipId[] combined = scope.Chunk(size).Reverse()
                .SelectMany(batch => InteractionProximityQuery.Find(view, new ShipId(1), batch, 1))
                .Distinct().OrderBy(ship => ship.Value).ToArray();
            Assert.Equal(expected, combined);
            ShipId[] preparedCombined = scope.Chunk(size).Reverse()
                .SelectMany(batch => prepared.PrepareScope(batch).Find(new ShipId(1), 1))
                .Distinct().OrderBy(ship => ship.Value).ToArray();
            Assert.Equal(expected, preparedCombined);
        }
    }

    private static SpatialMovement Pair(long target)
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(target));
        return movement;
    }

    private static ExecutableBoundedTerminalManeuverPlan Move() => BoundedTerminalManeuverPlanner.Select(
        SimulationTime.Zero, new ShipKinematicState(Position(0), ShipVelocity.Zero, ShipHeading.Zero), Position(10), null,
        new ManeuverAcceleration(1000), new ManeuverAcceleration(1000), new ManeuverSpeed(30_000),
        new ManeuverTurnRate(45_000)).ExecutablePlan!;
    private static TerminalManeuverCommit<SpatialMovementEvent> Start(SpatialMovement movement, ExecutableBoundedTerminalManeuverPlan plan) =>
        movement.CommitStartTerminalManeuver(new ShipId(1), plan, ManeuverObjective.FastestArrival, SimulationTime.Zero, static value => value);
    private static SystemPosition Position(long x, ulong system = 1) => new(new SystemId(system),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
