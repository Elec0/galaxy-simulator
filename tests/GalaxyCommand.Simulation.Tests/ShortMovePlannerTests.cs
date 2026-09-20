using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ShortMovePlannerTests
{
    [Fact]
    public void SatisfiedTerminalStateSettlesBeforePlanningMotion()
    {
        ShortMovePlanSelection selection = Select(
            State(0, 0, 1_000, 0),
            Position(1, 0));

        Assert.Equal(ShortMovePlanKind.TerminalSettle, selection.Kind);
        Assert.Equal(
            State(0, 0, 0, 0),
            Assert.IsType<ShipKinematicState>(selection.SettledState));
        Assert.Null(selection.TriangularPlan);
        Assert.Null(selection.ImmediateBrakingPlan);
    }

    [Fact]
    public void SwitchBoundarySelectsImmediateBrakingBeforeTriangularMotion()
    {
        ShortMovePlanSelection selection = Select(
            State(0, 0, 30_000, 0),
            Position(45, 0));

        Assert.Equal(ShortMovePlanKind.ImmediateBraking, selection.Kind);
        Assert.NotNull(selection.ImmediateBrakingPlan);
        Assert.Null(selection.TriangularPlan);
    }

    [Fact]
    public void StationaryShortMoveSelectsTriangularMotion()
    {
        ShortMovePlanSelection selection = Select(
            State(0, 0, 0, 0),
            Position(100, 0));

        Assert.Equal(ShortMovePlanKind.Triangular, selection.Kind);
        Assert.NotNull(selection.TriangularPlan);
        Assert.Null(selection.ImmediateBrakingPlan);
    }

    [Fact]
    public void MovingStartBeforeSwitchSelectsTriangularMotion()
    {
        ShortMovePlanSelection selection = Select(
            State(0, 0, 10_000, 0),
            Position(100, 0));

        Assert.Equal(ShortMovePlanKind.Triangular, selection.Kind);
        Assert.Equal(
            10_000UL,
            selection.TriangularPlan?.Profile.InitialSpeedMillimetersPerSecond);
    }

    [Fact]
    public void OvershootStillSelectsImmediateBrakingAndFollowUpReplan()
    {
        ShortMovePlanSelection selection = Select(
            State(0, 0, 40_000, 0),
            Position(45, 0),
            maximumSpeed: 30_000);

        Assert.Equal(ShortMovePlanKind.ImmediateBraking, selection.Kind);
        Assert.False(selection.ImmediateBrakingPlan?.CompletesTerminalArrival);
    }

    [Theory]
    [InlineData(0, 0, 100, 0, 30_000)]
    [InlineData(0, 1_000, 10, 0, 300_000)]
    public void CappedTravelOrUnalignedMotionUsesGeneralPlanner(
        long velocityX,
        long velocityY,
        long destinationX,
        long destinationY,
        ulong maximumSpeed)
    {
        ShortMovePlanSelection selection = Select(
            State(0, 0, velocityX, velocityY),
            Position(destinationX, destinationY),
            maximumSpeed);

        Assert.Equal(ShortMovePlanKind.GeneralPlanner, selection.Kind);
        Assert.Null(selection.SettledState);
        Assert.Null(selection.TriangularPlan);
        Assert.Null(selection.ImmediateBrakingPlan);
    }

    [Fact]
    public void CrossSystemDestinationIsRejectedBeforeSelection()
    {
        Assert.Throws<ArgumentException>(() =>
            ShortMovePlanner.SelectAligned(
                SimulationTime.Zero,
                State(0, 0, 0, 0),
                new SystemPosition(
                    new SystemId(2),
                    new SpatialPosition(
                        new SpatialCoordinate(10),
                        new SpatialCoordinate(0))),
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(300_000)));
    }

    private static ShortMovePlanSelection Select(
        ShipKinematicState start,
        SystemPosition destination,
        ulong maximumSpeed = 300_000) =>
        ShortMovePlanner.SelectAligned(
            SimulationTime.Zero,
            start,
            destination,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(10_000),
            new ManeuverSpeed(maximumSpeed));

    private static ShipKinematicState State(
        long x,
        long y,
        long velocityX,
        long velocityY) =>
        new(
            Position(x, y),
            new ShipVelocity(velocityX, velocityY),
            ShipHeading.Zero);

    private static SystemPosition Position(long x, long y) =>
        new(
            new SystemId(1),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));
}
