using System.Numerics;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionSweptBoundsTests
{
    [Fact]
    public void BoundsIncludeIndependentInteriorAxisExtrema()
    {
        var segment = new AnalyticManeuverSegment(SimulationTime.Zero, new SimulationTime(4_000),
            new ShipKinematicState(Position(0, 0), new ShipVelocity(4_000, -2_000), ShipHeading.Zero),
            new ShipAcceleration(-2_000, 2_000), ShipAngularRate.Zero);
        InteractionSweptBounds bounds = Assert.IsType<InteractionSweptBounds>(InteractionSweptBoundsQuery.Find(
            InteractionPhaseCurve.From(segment), SimulationTime.Zero, segment.EndsAt));
        Assert.Equal(new InteractionSweptBounds(new SystemId(1), 0, -1, 4, 8), bounds);
    }

    [Fact]
    public void SignedFractionalCoordinatesRoundOutward()
    {
        InteractionPhaseCurve curve = Curve(-1, 1_000, 0, 100, 1100);
        InteractionSweptBounds bounds = Assert.IsType<InteractionSweptBounds>(InteractionSweptBoundsQuery.Find(
            curve, new SimulationTime(350), new SimulationTime(850)));
        Assert.Equal(new BigInteger(-1), bounds.MinX);
        Assert.Equal(BigInteger.Zero, bounds.MaxX);
    }

    [Fact]
    public void FractionalExtremumIsRetainedBetweenMillisecondSamples()
    {
        InteractionPhaseCurve curve = Curve(0, 20_000_000, -30_000_000_000, 0, 1);
        InteractionSweptBounds bounds = Assert.IsType<InteractionSweptBounds>(InteractionSweptBoundsQuery.Find(
            curve, SimulationTime.Zero, new SimulationTime(1)));
        Assert.Equal(BigInteger.Zero, bounds.MinX);
        Assert.Equal(new BigInteger(7), bounds.MaxX);
    }

    [Fact]
    public void FractionalBrakeHoldDoesNotExtendTheEquationIntoReversal()
    {
        var brake = new DecelerationManeuverSegment(ManeuverDecelerationKind.ActiveBrake,
            SimulationTime.Zero, State(0, -1_000_000), new ManeuverAcceleration(4_000_000_000));
        InteractionSweptBounds bounds = Assert.IsType<InteractionSweptBounds>(InteractionSweptBoundsQuery.Find(
            InteractionPhaseCurve.From(brake), brake.EndsAt, brake.EndsAt));
        Assert.Equal(new BigInteger(-1), bounds.MinX);
        Assert.Equal(BigInteger.Zero, bounds.MaxX);
    }

    [Fact]
    public void WholePlanIncludesCommittedEndpointReset()
    {
        var brake = new DecelerationManeuverSegment(ManeuverDecelerationKind.PassiveDrag,
            SimulationTime.Zero, State(0, 2_000_000), new ManeuverAcceleration(3_000_000_000));
        var plan = ExecutableBoundedTerminalManeuverPlan.FromPassiveDrag(brake);
        InteractionSweptBounds bounds = Assert.IsType<InteractionSweptBounds>(InteractionSweptBoundsQuery.Find(
            plan, plan.EndsAt, plan.EndsAt));
        Assert.Equal(BigInteger.One, bounds.MinX);
        Assert.Equal(BigInteger.One, bounds.MaxX);
    }

    [Fact]
    public void InsertedBoundariesPreserveContinuousBounds()
    {
        var brake = new DecelerationManeuverSegment(ManeuverDecelerationKind.PassiveDrag,
            SimulationTime.Zero, State(0, 1_000), new ManeuverAcceleration(1_000));
        var original = ExecutableBoundedTerminalManeuverPlan.FromPassiveDrag(brake);
        var split = original.WithAdditionalBoundaries([new SimulationTime(500)]);
        InteractionSweptBounds? bounds = InteractionSweptBoundsQuery.Find(split, new SimulationTime(500), new SimulationTime(500));
        Assert.Equal(InteractionSweptBoundsQuery.Find(original, new SimulationTime(500), new SimulationTime(500)), bounds);
        Assert.Equal(new InteractionSweptBounds(new SystemId(1), 0, 0, 1, 0), bounds);
    }

    [Fact]
    public void BoundsClipToKnownPhaseAndRejectReversedIntervals()
    {
        InteractionPhaseCurve curve = Curve(10, 1_000, 0, 1000, 2000);
        Assert.Equal(new InteractionSweptBounds(new SystemId(1), 10, 0, 11, 0),
            InteractionSweptBoundsQuery.Find(curve, SimulationTime.Zero, new SimulationTime(3000)));
        Assert.Null(InteractionSweptBoundsQuery.Find(curve, SimulationTime.Zero, new SimulationTime(999)));
        Assert.Null(InteractionSweptBoundsQuery.Find(curve, new SimulationTime(2001), new SimulationTime(3000)));
        Assert.Throws<ArgumentOutOfRangeException>(() => InteractionSweptBoundsQuery.Find(curve, new SimulationTime(2), new SimulationTime(1)));
    }

    [Fact]
    public void CoordinatesBeyondLongAndTimelineNearUlongMaximumRemainExact()
    {
        ulong start = ulong.MaxValue - 1000;
        InteractionPhaseCurve curve = Curve(long.MaxValue, 1_000, 0, start, ulong.MaxValue);
        Assert.Equal(new InteractionSweptBounds(new SystemId(1), long.MaxValue, 0, (BigInteger)long.MaxValue + 1, 0),
            InteractionSweptBoundsQuery.Find(curve, new SimulationTime(start), new SimulationTime(ulong.MaxValue)));
    }

    [Fact]
    public void WholePlanUnionsCurvesAndClipsItsKnownHorizon()
    {
        var plan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
            new ShipKinematicState(Position(0, 0), ShipVelocity.Zero, ShipHeading.Zero), Position(90, 0), null,
            new ManeuverAcceleration(10_000), new ManeuverAcceleration(10_000),
            new ManeuverSpeed(30_000), new ManeuverTurnRate(45_000)).ExecutablePlan!;
        InteractionSweptBounds bounds = Assert.IsType<InteractionSweptBounds>(InteractionSweptBoundsQuery.Find(
            plan, SimulationTime.Zero, new SimulationTime(ulong.MaxValue)));
        Assert.Equal(BigInteger.Zero, bounds.MinX);
        Assert.True(bounds.MaxX >= 90);
        Assert.Equal(BigInteger.Zero, bounds.MinY);
        Assert.Equal(BigInteger.Zero, bounds.MaxY);
        Assert.Null(InteractionSweptBoundsQuery.Find(plan, new SimulationTime(plan.EndsAt.Milliseconds + 1), new SimulationTime(ulong.MaxValue)));
        Assert.Throws<ArgumentOutOfRangeException>(() => InteractionSweptBoundsQuery.Find(plan, new SimulationTime(2), new SimulationTime(1)));
    }

    [Theory]
    [InlineData(3, 4, 5, true)]
    [InlineData(3, 4, 4, false)]
    [InlineData(0, 0, 0, true)]
    [InlineData(1, 0, 0, false)]
    public void BoundsRejectOnlyPairsWhoseMinimumDistanceExceedsRange(long gapX, long gapY, ulong range, bool expected)
    {
        var first = new InteractionSweptBounds(new SystemId(1), -10, -10, 0, 0);
        var second = new InteractionSweptBounds(new SystemId(1), gapX, gapY, gapX + 10, gapY + 10);
        Assert.Equal(expected, first.MayContact(second, range));
        Assert.Equal(expected, second.MayContact(first, range));
        Assert.False(first.MayContact(second with { System = new SystemId(2) }, ulong.MaxValue));
    }

    [Fact]
    public void ContainedBoundsAndExtremeCoordinatesDoNotOverflowRejection()
    {
        var outer = new InteractionSweptBounds(new SystemId(1), long.MinValue, long.MinValue, long.MaxValue, long.MaxValue);
        var inner = new InteractionSweptBounds(new SystemId(1), -1, -1, 1, 1);
        Assert.True(outer.MayContact(inner, 0));
        var beyond = new InteractionSweptBounds(new SystemId(1), (BigInteger)long.MaxValue + 1, 0,
            (BigInteger)long.MaxValue + 10, 0);
        Assert.True(outer.MayContact(beyond, 1));
        var distant = beyond with { MinX = (BigInteger)long.MaxValue + ulong.MaxValue + 1,
            MaxX = (BigInteger)long.MaxValue + ulong.MaxValue + 10 };
        Assert.False(outer.MayContact(distant, ulong.MaxValue));
    }

    [Fact]
    public void SweptCrossingsSurviveConservativeRejection()
    {
        InteractionPhaseCurve curve = Curve(0, 20_000_000, -30_000_000_000, 0, 1);
        InteractionPhaseCurve target = InteractionPhaseCurve.Fixed(Position(6, 0), SimulationTime.Zero, new SimulationTime(1));
        Assert.NotNull(InteractionPhaseRangeQuery.Find(curve, target, SimulationTime.Zero, new SimulationTime(1), 0));
        InteractionSweptBounds moving = InteractionSweptBoundsQuery.Find(curve, SimulationTime.Zero, new SimulationTime(1))!;
        InteractionSweptBounds fixedBounds = InteractionSweptBoundsQuery.Find(target, SimulationTime.Zero, new SimulationTime(1))!;
        Assert.True(moving.MayContact(fixedBounds, 0));
    }

    private static InteractionPhaseCurve Curve(long x, long velocity, long acceleration, ulong start, ulong end) =>
        InteractionPhaseCurve.From(new AnalyticManeuverSegment(new SimulationTime(start), new SimulationTime(end),
            State(x, velocity), new ShipAcceleration(acceleration, 0), ShipAngularRate.Zero));

    private static ShipKinematicState State(long x, long velocity) => new(Position(x, 0), new ShipVelocity(velocity, 0), ShipHeading.Zero);
    private static SystemPosition Position(long x, long y) => new(new SystemId(1),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(y)));
}
