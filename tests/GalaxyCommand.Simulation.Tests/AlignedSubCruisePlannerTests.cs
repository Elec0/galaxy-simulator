using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class AlignedSubCruisePlannerTests
{
    [Fact]
    public void SatisfiedStateStillSelectsTerminalSettleFirst()
    {
        AlignedSubCruisePlanSelection selection = Select(
            State(0, 0, 1_000, 0),
            Position(1, 0));

        Assert.Equal(AlignedSubCruisePlanKind.TerminalSettle, selection.Kind);
        Assert.Equal(ShipVelocity.Zero, selection.SettledState?.Velocity);
    }

    [Fact]
    public void AboveCapVelocityStillSelectsImmediateBraking()
    {
        AlignedSubCruisePlanSelection selection = Select(
            State(0, 0, 40_000, 0),
            Position(45, 0),
            maximumSpeed: 30_000);

        Assert.Equal(AlignedSubCruisePlanKind.ImmediateBraking, selection.Kind);
        Assert.NotNull(selection.ImmediateBrakingPlan);
        Assert.False(selection.ImmediateBrakingPlan?.CompletesTerminalArrival);
    }

    [Fact]
    public void ShortMoveStillSelectsTriangularPlan()
    {
        AlignedSubCruisePlanSelection selection = Select(
            State(0, 0, 0, 0),
            Position(90, 0),
            maximumSpeed: 30_000);

        Assert.Equal(AlignedSubCruisePlanKind.Triangular, selection.Kind);
        Assert.NotNull(selection.TriangularPlan);
        Assert.Null(selection.CappedSpeedPlan);
    }

    [Fact]
    public void LongerAlignedMoveSelectsCappedSpeedPlan()
    {
        AlignedSubCruisePlanSelection selection = Select(
            State(0, 0, 0, 0),
            Position(200, 0),
            maximumSpeed: 30_000);

        Assert.Equal(AlignedSubCruisePlanKind.CappedSpeed, selection.Kind);
        Assert.NotNull(selection.CappedSpeedPlan);
        Assert.Equal(
            Position(200, 0),
            selection.CappedSpeedPlan?.StateAt(
                selection.CappedSpeedPlan.EndsAt).Position);
    }

    [Fact]
    public void UnalignedVelocityRequiresDirectionalPlanner()
    {
        AlignedSubCruisePlanSelection selection = Select(
            State(0, 0, 0, 1_000),
            Position(200, 0),
            maximumSpeed: 30_000);

        Assert.Equal(
            AlignedSubCruisePlanKind.DirectionalPlanner,
            selection.Kind);
        Assert.Null(selection.SettledState);
        Assert.Null(selection.ImmediateBrakingPlan);
        Assert.Null(selection.TriangularPlan);
        Assert.Null(selection.CappedSpeedPlan);
    }

    [Fact]
    public void CrossSystemDestinationIsRejectedBeforeSelection()
    {
        Assert.Throws<ArgumentException>(() =>
            AlignedSubCruisePlanner.Select(
                SimulationTime.Zero,
                State(0, 0, 0, 0),
                new SystemPosition(
                    new SystemId(2),
                    new SpatialPosition(
                        new SpatialCoordinate(200),
                        new SpatialCoordinate(0))),
                new ManeuverAcceleration(10_000),
                new ManeuverAcceleration(10_000),
                new ManeuverSpeed(30_000)));
    }

    private static AlignedSubCruisePlanSelection Select(
        ShipKinematicState start,
        SystemPosition destination,
        ulong maximumSpeed = 300_000) =>
        AlignedSubCruisePlanner.Select(
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
