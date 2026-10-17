using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PreparedInteractionProximityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(2100)]
    [InlineData(6000)]
    public void PreparedQueriesMatchReferenceCurvesAndBoundaries(ulong time)
    {
        var movement = Movement();
        ExecutableBoundedTerminalManeuverPlan plan = BoundedTerminalManeuverPlanner.Select(
            SimulationTime.Zero, new ShipKinematicState(Position(0), ShipVelocity.Zero, ShipHeading.Zero), Position(90), null,
            new ManeuverAcceleration(10_000), new ManeuverAcceleration(10_000), new ManeuverSpeed(30_000),
            new ManeuverTurnRate(45_000)).ExecutablePlan!.WithAdditionalBoundaries([new SimulationTime(2100)]);
        movement.CommitStartTerminalManeuver(new ShipId(1), plan, ManeuverObjective.FastestArrival,
            SimulationTime.Zero, static value => value);
        movement.CommitStartOrReplace(new ShipId(2), new TravelLeg.Local(Position(25), Position(0),
            new SimulationDuration(6000)), SimulationTime.Zero, static value => value);
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(new SimulationTime(time));
        ShipId[] scope = [new ShipId(4), new ShipId(3), new ShipId(2), new ShipId(1), new ShipId(99), new ShipId(3)];
        PreparedInteractionProximityView prepared = InteractionProximityQuery.Prepare(view);
        Assert.Equal(view.Timestamp, prepared.Timestamp);
        foreach (InteractionPreparationStrategy strategy in Strategies())
        {
            PreparedInteractionProximityScope queries = prepared.PrepareScope(scope, strategy);
            foreach (ShipId source in scope)
            foreach (ulong range in new ulong[] { 0, 1, 10, ulong.MaxValue })
                Assert.Equal(InteractionProximityQuery.Find(view, source, scope, range), queries.Find(source, range));
        }
    }

    [Fact]
    public void PreparedScopeAndGeometrySurviveCallerAndOwnerMutation()
    {
        var movement = Movement();
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        PreparedInteractionProximityView prepared = InteractionProximityQuery.Prepare(view);
        var scope = new List<ShipId> { new(2), new(3), new(3) };
        PreparedInteractionProximityScope[] queries = Strategies().Select(strategy => prepared.PrepareScope(scope, strategy)).ToArray();
        scope.Clear();
        movement.CommitRemove(new ShipId(3), SimulationTime.Zero);
        foreach (PreparedInteractionProximityScope query in queries)
        {
            Assert.Equal<ShipId>([new ShipId(3)], query.Find(new ShipId(1), 0));
            Assert.Throws<NotSupportedException>(() => ((IList<ShipId>)query.Find(new ShipId(1), 0)).Clear());
        }
        Assert.Empty(prepared.PrepareScope([]).Find(new ShipId(1), ulong.MaxValue));
        Assert.Empty(InteractionProximityQuery.Prepare(movement.CaptureInteractionReadView(SimulationTime.Zero))
            .PrepareScope([new ShipId(3)]).Find(new ShipId(1), 0));
    }

    [Fact]
    public void SourceOutsideScopeAndNonlocalParticipantsRetainReferenceSemantics()
    {
        var movement = Movement();
        movement.CommitStartConnector(new ShipId(3), new TravelLeg.Connector(new TransitConnectionId(1),
            Position(0), Position(0, 2), new SimulationDuration(10)), SimulationTime.Zero, static value => value);
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        var prepared = InteractionProximityQuery.Prepare(view);
        ShipId[] scope = [new ShipId(2), new ShipId(3), new ShipId(4)];
        foreach (InteractionPreparationStrategy strategy in Strategies())
        {
            PreparedInteractionProximityScope query = prepared.PrepareScope(scope, strategy);
            foreach (ShipId source in new ShipId[] { new(1), new(3), new(99) })
                Assert.Equal(InteractionProximityQuery.Find(view, source, scope, ulong.MaxValue), query.Find(source, ulong.MaxValue));
        }
    }

    [Fact]
    public void InvalidInputIsRejectedAtItsPreparationBoundary()
    {
        Assert.Throws<ArgumentNullException>(() => InteractionProximityQuery.Prepare(null!));
        PreparedInteractionProximityView view = InteractionProximityQuery.Prepare(Movement().CaptureInteractionReadView(SimulationTime.Zero));
        Assert.Throws<ArgumentNullException>(() => view.PrepareScope(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.PrepareScope([default]));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.PrepareScope([]).Find(default, 0));
    }

    [Fact]
    public void PreparedPointRetainsFractionalBrakeHoldExactly()
    {
        var time = new SimulationTime(1);
        var brake = new DecelerationManeuverSegment(ManeuverDecelerationKind.ActiveBrake, SimulationTime.Zero,
            new ShipKinematicState(Position(0), new ShipVelocity(2_000_000, 0), ShipHeading.Zero), new ManeuverAcceleration(3_000_000_000));
        var moving = PreparedInteractionPoint.Capture(InteractionPhaseCurve.From(brake), time);
        var fixedPoint = PreparedInteractionPoint.Capture(InteractionPhaseCurve.Fixed(Position(1), time, time), time);
        Assert.False(moving.InRange(fixedPoint, 0));
        Assert.True(moving.InRange(fixedPoint, 1));
        var other = new AnalyticManeuverSegment(SimulationTime.Zero, time,
            new ShipKinematicState(Position(1), new ShipVelocity(-500_000, 0), ShipHeading.Zero),
            ShipAcceleration.Zero, ShipAngularRate.Zero);
        Assert.False(moving.InRange(PreparedInteractionPoint.Capture(InteractionPhaseCurve.From(other), time), 0));
    }

    [Fact]
    public void PreparedDistanceRemainsExactAtCoordinateAndRangeLimits()
    {
        var low = new SystemPosition(new SystemId(1), new SpatialPosition(new SpatialCoordinate(long.MinValue), new SpatialCoordinate(long.MinValue)));
        var high = new SystemPosition(new SystemId(1), new SpatialPosition(new SpatialCoordinate(long.MaxValue), new SpatialCoordinate(long.MaxValue)));
        var first = PreparedInteractionPoint.Capture(InteractionPhaseCurve.Fixed(low, SimulationTime.Zero, SimulationTime.Zero), SimulationTime.Zero);
        var second = PreparedInteractionPoint.Capture(InteractionPhaseCurve.Fixed(high, SimulationTime.Zero, SimulationTime.Zero), SimulationTime.Zero);
        Assert.False(first.InRange(second, ulong.MaxValue));
        var axisHigh = new SystemPosition(new SystemId(1), new SpatialPosition(high.Position.X, low.Position.Y));
        Assert.True(first.InRange(PreparedInteractionPoint.Capture(InteractionPhaseCurve.Fixed(axisHigh, SimulationTime.Zero, SimulationTime.Zero), SimulationTime.Zero), ulong.MaxValue));
    }

    [Fact]
    public void AllStrategiesPreserveExtremeCoordinatesAndEmptyScopes()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), new SystemPosition(new SystemId(1),
            new SpatialPosition(new SpatialCoordinate(long.MinValue), new SpatialCoordinate(long.MinValue))));
        movement.Add(new ShipId(2), new SystemPosition(new SystemId(1),
            new SpatialPosition(new SpatialCoordinate(long.MaxValue), new SpatialCoordinate(long.MinValue))));
        movement.Add(new ShipId(3), new SystemPosition(new SystemId(1),
            new SpatialPosition(new SpatialCoordinate(long.MaxValue), new SpatialCoordinate(long.MaxValue))));
        var prepared = InteractionProximityQuery.Prepare(movement.CaptureInteractionReadView(SimulationTime.Zero));
        foreach (InteractionPreparationStrategy strategy in Strategies())
        {
            Assert.Equal<ShipId>([new(2)], prepared.PrepareScope([new(3), new(2)], strategy).Find(new ShipId(1), ulong.MaxValue));
            Assert.Empty(prepared.PrepareScope([], strategy).Find(new ShipId(1), ulong.MaxValue));
        }
    }

    [Fact]
    public void IndexedStrategiesRequireValidExplicitTuning()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InteractionPreparationStrategy.SparseGrid(0, 4096));
        Assert.Throws<ArgumentOutOfRangeException>(() => InteractionPreparationStrategy.SparseGrid(16, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InteractionPreparationStrategy.BoundingBoxHierarchy(0));
        var view = InteractionProximityQuery.Prepare(Movement().CaptureInteractionReadView(SimulationTime.Zero));
        Assert.Throws<ArgumentNullException>(() => view.PrepareScope([], null!));
    }

    private static IEnumerable<InteractionPreparationStrategy> Strategies() =>
        [InteractionPreparationStrategy.Scan, InteractionPreparationStrategy.SparseGrid(1, 1),
         InteractionPreparationStrategy.SparseGrid(16, 4096), InteractionPreparationStrategy.SparseGrid(ulong.MaxValue, 2),
         InteractionPreparationStrategy.BoundingBoxHierarchy(1), InteractionPreparationStrategy.BoundingBoxHierarchy(8)];

    private static SpatialMovement Movement()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(25));
        movement.Add(new ShipId(3), Position(0));
        movement.Add(new ShipId(4), Position(0, 2));
        return movement;
    }
    private static SystemPosition Position(long x, ulong system = 1) => new(new SystemId(system),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
