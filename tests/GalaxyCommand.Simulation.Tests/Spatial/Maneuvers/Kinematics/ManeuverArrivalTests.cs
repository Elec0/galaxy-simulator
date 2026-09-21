using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ManeuverArrivalTests
{
    [Fact]
    public void TerminalArrivalAcceptsEveryInclusiveToleranceBoundary()
    {
        ManeuverArrivalEvaluation result = ManeuverArrival.EvaluateTerminal(
            State(1, 0, 1_000, 0, 1_000),
            Position(0, 0),
            ShipHeading.Zero);

        Assert.True(result.PositionSatisfied);
        Assert.True(result.VelocitySatisfied);
        Assert.True(result.HeadingSatisfied);
        Assert.True(result.IsSatisfied);
    }

    [Fact]
    public void PositionToleranceUsesEuclideanDistance()
    {
        ManeuverArrivalEvaluation result = ManeuverArrival.EvaluateTerminal(
            State(1, 1, 0, 0, 0),
            Position(0, 0),
            requestedHeading: null);

        Assert.False(result.PositionSatisfied);
        Assert.False(result.IsSatisfied);
    }

    [Fact]
    public void VelocityToleranceAppliesToTheCompleteVector()
    {
        ManeuverArrivalEvaluation result = ManeuverArrival.EvaluateTerminal(
            State(0, 0, 1_000, 1, 0),
            Position(0, 0),
            requestedHeading: null);

        Assert.False(result.VelocitySatisfied);
        Assert.False(result.IsSatisfied);
    }

    [Fact]
    public void HeadingToleranceUsesShortestArcAcrossCanonicalZero()
    {
        ManeuverArrivalEvaluation within = ManeuverArrival.EvaluateTerminal(
            State(0, 0, 0, 0, 359_500),
            Position(0, 0),
            new ShipHeading(500));
        ManeuverArrivalEvaluation outside = ManeuverArrival.EvaluateTerminal(
            State(0, 0, 0, 0, 359_499),
            Position(0, 0),
            new ShipHeading(500));

        Assert.True(within.HeadingSatisfied);
        Assert.True(within.IsSatisfied);
        Assert.False(outside.HeadingSatisfied);
        Assert.False(outside.IsSatisfied);
    }

    [Fact]
    public void OmittedHeadingLeavesTerminalHeadingUnconstrained()
    {
        ManeuverArrivalEvaluation result = ManeuverArrival.EvaluateTerminal(
            State(0, 0, 0, 0, 217_000),
            Position(0, 0),
            requestedHeading: null);

        Assert.True(result.HeadingSatisfied);
        Assert.True(result.IsSatisfied);
    }

    [Fact]
    public void PositionInAnotherSystemNeverSatisfiesArrival()
    {
        ManeuverArrivalEvaluation result = ManeuverArrival.EvaluateTerminal(
            State(0, 0, 0, 0, 0),
            Position(0, 0, systemId: 2),
            requestedHeading: null);

        Assert.False(result.PositionSatisfied);
        Assert.False(result.IsSatisfied);
    }

    [Fact]
    public void TerminalSettleCommitsExactZeroWithoutSnappingPositionOrHeading()
    {
        ShipKinematicState current = State(1, 0, 750, -250, 500);

        bool wasSettled = ManeuverArrival.TrySettleTerminal(
            current,
            Position(0, 0),
            ShipHeading.Zero,
            out ShipKinematicState settled);

        Assert.True(wasSettled);
        Assert.Equal(current.Position, settled.Position);
        Assert.Equal(current.Heading, settled.Heading);
        Assert.Equal(ShipVelocity.Zero, settled.Velocity);
    }

    [Fact]
    public void TerminalSettleRejectsWithoutMutatingUnsatisfiedState()
    {
        ShipKinematicState current = State(2, 0, 0, 0, 0);

        bool wasSettled = ManeuverArrival.TrySettleTerminal(
            current,
            Position(0, 0),
            requestedHeading: null,
            out ShipKinematicState settled);

        Assert.False(wasSettled);
        Assert.Equal(current, settled);
    }

    [Fact]
    public void FlyThroughWaypointChecksOnlyPositionTolerance()
    {
        ShipKinematicState current = State(
            1,
            0,
            velocityX: 300_000,
            velocityY: -200_000,
            heading: 180_000);

        Assert.True(ManeuverArrival.IsFlyThroughWaypointReached(
            current,
            Position(0, 0)));
    }

    private static ShipKinematicState State(
        long x,
        long y,
        long velocityX,
        long velocityY,
        uint heading) =>
        new(
            Position(x, y),
            new ShipVelocity(velocityX, velocityY),
            new ShipHeading(heading));

    private static SystemPosition Position(
        long x,
        long y,
        ulong systemId = 1) =>
        new(
            new SystemId(systemId),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));
}
