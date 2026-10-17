using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PreparedInteractionSweptTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 500)]
    [InlineData(0, 10000)]
    [InlineData(500, 10000)]
    [InlineData(2100, 10000)]
    public void AllStrategiesMatchExactForecastsAndPartitionReduction(ulong now, ulong end)
    {
        var movement = Movement();
        var view = movement.CaptureInteractionReadView(new SimulationTime(now));
        var prepared = InteractionSweptQuery.Prepare(view, new SimulationTime(end));
        Assert.Equal(view.Timestamp, prepared.From);
        Assert.Equal(new SimulationTime(end), prepared.Through);
        ShipId[] scope = [new(2), new(3), new(4), new(5), new(6), new(7), new(8), new(99), new(2)];
        foreach (InteractionPreparationStrategy strategy in Strategies())
        foreach (ShipId source in new ShipId[] { new(1), new(2), new(3), new(5), new(7), new(8), new(99) })
        foreach (ulong range in new ulong[] { 0, 1, 30, ulong.MaxValue })
        {
            InteractionSweptMatch[] expected = scope.Distinct().OrderBy(id => id.Value).Where(id => id != source)
                .Select(id => Expected(view, source, id, new SimulationTime(end), range)).OfType<InteractionSweptMatch>().ToArray();
            Assert.Equal(expected, prepared.PrepareScope(scope).Find(source, range));
            Assert.Equal(expected, prepared.PrepareScope(scope.Reverse(), strategy).Find(source, range));
            InteractionSweptMatch[] combined = scope.Chunk(2).Reverse()
                .SelectMany(batch => prepared.PrepareScope(batch, strategy).Find(source, range))
                .Distinct().OrderBy(match => match.Target.Value).ToArray();
            Assert.Equal(expected, combined);
        }
    }

    [Fact]
    public void ScopeAndGeometrySurviveOwnerAndCallerMutation()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(0));
        var view = InteractionSweptQuery.Prepare(movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(100));
        var scope = new List<ShipId> { new(2), new(2) };
        var queries = Strategies().Select(strategy => view.PrepareScope(scope, strategy)).ToArray();
        scope.Clear();
        movement.CommitRemove(new ShipId(2), SimulationTime.Zero);
        foreach (var query in queries)
        {
            var match = Assert.Single(query.Find(new ShipId(1), 0));
            Assert.Equal(new ShipId(2), match.Target);
            Assert.Equal(new SweptRangeContact(SimulationTime.Zero, true, true), match.Contact);
            Assert.Throws<NotSupportedException>(() => ((IList<InteractionSweptMatch>)query.Find(new ShipId(1), 0)).Clear());
        }
        Assert.Empty(InteractionSweptQuery.Prepare(movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(100))
            .PrepareScope([new ShipId(2)]).Find(new ShipId(1), 0));
    }

    [Fact]
    public void InvalidInputsAreRejectedAtPreparationAndQueryBoundaries()
    {
        Assert.Throws<ArgumentNullException>(() => InteractionSweptQuery.Prepare(null!, SimulationTime.Zero));
        var captured = Movement().CaptureInteractionReadView(new SimulationTime(500));
        Assert.Throws<ArgumentOutOfRangeException>(() => InteractionSweptQuery.Prepare(captured, SimulationTime.Zero));
        var view = InteractionSweptQuery.Prepare(captured, new SimulationTime(1000));
        Assert.Throws<ArgumentNullException>(() => view.PrepareScope(null!));
        Assert.Throws<ArgumentNullException>(() => view.PrepareScope([], null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.PrepareScope([default]));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.PrepareScope([]).Find(default, 0));
    }

    [Fact]
    public void InsertedBoundaryStopsSearchAndLaterViewPreservesTheCurve()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(25));
        var plan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
            new ShipKinematicState(Position(0), ShipVelocity.Zero, ShipHeading.Zero), Position(90), null,
            new ManeuverAcceleration(10000), new ManeuverAcceleration(10000), new ManeuverSpeed(30000),
            new ManeuverTurnRate(45000)).ExecutablePlan!.WithAdditionalBoundaries([new SimulationTime(2100)]);
        var commit = movement.CommitStartTerminalManeuver(new ShipId(1), plan, ManeuverObjective.FastestArrival,
            SimulationTime.Zero, static value => value);
        var before = InteractionSweptQuery.Prepare(movement.CaptureInteractionReadView(SimulationTime.Zero), plan.EndsAt);
        movement.HandleEvent(commit.EventProposals[0].Payload, commit.Maneuver.Generation, new SimulationTime(2100));
        var captured = movement.CaptureInteractionReadView(new SimulationTime(2100));
        var after = InteractionSweptQuery.Prepare(captured, plan.EndsAt);
        var expected = ManeuverSweptRangeQuery.FindFirstContact(plan, Position(25), new SimulationTime(2100), new SimulationTime(3000), 0);
        Assert.NotNull(expected);
        Assert.False(expected.InRangeAtTimestamp);
        foreach (var strategy in Strategies())
        {
            Assert.Empty(before.PrepareScope([new ShipId(2)], strategy).Find(new ShipId(1), 0));
            Assert.Equal(expected, Assert.Single(after.PrepareScope([new ShipId(2)], strategy).Find(new ShipId(1), 0)).Contact);
        }
    }

    [Fact]
    public void BriefCrossingSurvivesRoundingButCompletionReceiptDoesNotReplayIt()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(10));
        var commit = movement.CommitStartOrReplace(new ShipId(1), new TravelLeg.Local(Position(0), Position(20),
            new SimulationDuration(1)), SimulationTime.Zero, static value => value);
        var before = InteractionSweptQuery.Prepare(movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(100));
        movement.HandleEvent(commit.EventProposal!.Payload, commit.Motion!.Generation, new SimulationTime(1));
        var captured = movement.CaptureInteractionReadView(new SimulationTime(1));
        Assert.NotNull(captured.Get(new ShipId(1))!.Completion);
        var after = InteractionSweptQuery.Prepare(captured, new SimulationTime(100));
        foreach (var strategy in Strategies())
        {
            var contact = Assert.Single(before.PrepareScope([new ShipId(2)], strategy).Find(new ShipId(1), 0)).Contact;
            Assert.Equal(new SweptRangeContact(new SimulationTime(1), false, false), contact);
            Assert.Empty(after.PrepareScope([new ShipId(2)], strategy).Find(new ShipId(1), 0));
        }
    }

    [Fact]
    public void FractionalBrakingHoldAndCommittedEndResetMatchExactPlanQuery()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(1));
        var brake = new DecelerationManeuverSegment(ManeuverDecelerationKind.PassiveDrag, SimulationTime.Zero,
            new ShipKinematicState(Position(0), new ShipVelocity(2000000, 0), ShipHeading.Zero), new ManeuverAcceleration(3000000000));
        var plan = ExecutableBoundedTerminalManeuverPlan.FromPassiveDrag(brake);
        var captured = movement.CaptureInteractionReadView(SimulationTime.Zero);
        ShipKinematicState initial = plan.StateAt(SimulationTime.Zero);
        var snapshot = new ShipSpatialSnapshot(new ShipId(1), initial.Velocity, initial.Heading,
            new ShipSpatialSnapshotState.AnalyticManeuver(initial,
                new TerminalManeuverSnapshot(new MotionId(1), new EventGeneration(1), plan.Kind,
                    ManeuverObjective.FastestArrival, 0, null, null)));
        var seeded = new InteractionSpatialReadView(SimulationTime.Zero,
            [captured.Get(new ShipId(1))! with { Spatial = snapshot, ManeuverPlan = plan }, captured.Get(new ShipId(2))!]);
        foreach (var strategy in Strategies())
        foreach (ulong range in new ulong[] { 0, 1 })
        {
            var expected = ManeuverSweptRangeQuery.FindFirstContact(plan, Position(1), SimulationTime.Zero, plan.EndsAt, range);
            var query = InteractionSweptQuery.Prepare(seeded, new SimulationTime(100))
                .PrepareScope([new ShipId(2)], strategy);
            Assert.Equal(expected, Assert.Single(query.Find(new ShipId(1), range)).Contact);
        }
    }

    [Fact]
    public void ExtremeCoordinatesAndEmptyScopesRemainExact()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), new SystemPosition(new SystemId(1),
            new SpatialPosition(new SpatialCoordinate(long.MinValue), new SpatialCoordinate(long.MinValue))));
        movement.Add(new ShipId(2), new SystemPosition(new SystemId(1),
            new SpatialPosition(new SpatialCoordinate(long.MaxValue), new SpatialCoordinate(long.MinValue))));
        movement.Add(new ShipId(3), new SystemPosition(new SystemId(1),
            new SpatialPosition(new SpatialCoordinate(long.MaxValue), new SpatialCoordinate(long.MaxValue))));
        var view = InteractionSweptQuery.Prepare(movement.CaptureInteractionReadView(SimulationTime.Zero), new SimulationTime(ulong.MaxValue));
        foreach (var strategy in Strategies())
        {
            Assert.Equal(new ShipId(2), Assert.Single(view.PrepareScope([new ShipId(3), new ShipId(2)], strategy)
                .Find(new ShipId(1), ulong.MaxValue)).Target);
            Assert.Empty(view.PrepareScope([], strategy).Find(new ShipId(1), ulong.MaxValue));
        }
    }

    [Fact]
    public void MissingActiveAnalyticPlanIsRejectedEvenOutsideTheQueryScope()
    {
        var captured = Movement().CaptureInteractionReadView(SimulationTime.Zero);
        var incomplete = new InteractionSpatialReadView(captured.Timestamp,
            [captured.Get(new ShipId(1))! with { ManeuverPlan = null }, captured.Get(new ShipId(3))!]);
        Assert.Throws<InvalidOperationException>(() => InteractionSweptQuery.Prepare(incomplete, new SimulationTime(1000)));
    }

    private static InteractionSweptMatch? Expected(InteractionSpatialReadView view, ShipId source, ShipId target, SimulationTime through, ulong range)
    {
        var interest = new ShipPairInteractionInterest(new InteractionInterestId(1), "test", "range", source, target, range, new EventGeneration(1));
        return InteractionForecast.Evaluate(interest, view, through) is { } result ? new(target, result.Candidate.Contact) : null;
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
        movement.Add(new ShipId(3), Position(10));
        movement.Add(new ShipId(4), Position(0, 2));
        movement.Add(new ShipId(5), Position(0));
        movement.Add(new ShipId(6), Position(90));
        movement.Add(new ShipId(7), Position(50));
        movement.Add(new ShipId(8), Position(-10));
        var plan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
            new ShipKinematicState(Position(0), ShipVelocity.Zero, ShipHeading.Zero), Position(90), null,
            new ManeuverAcceleration(10000), new ManeuverAcceleration(10000), new ManeuverSpeed(30000),
            new ManeuverTurnRate(45000)).ExecutablePlan!.WithAdditionalBoundaries([new SimulationTime(2100)]);
        movement.CommitStartTerminalManeuver(new ShipId(1), plan, ManeuverObjective.FastestArrival, SimulationTime.Zero, static value => value);
        movement.CommitStartOrReplace(new ShipId(2), new TravelLeg.Local(Position(25), Position(0), new SimulationDuration(6000)), SimulationTime.Zero, static value => value);
        movement.CommitStartConnector(new ShipId(5), new TravelLeg.Connector(new TransitConnectionId(1), Position(0), Position(0, 2),
            new SimulationDuration(10000)), SimulationTime.Zero, static value => value);
        var otherPlan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
            new ShipKinematicState(Position(50), ShipVelocity.Zero, ShipHeading.Zero), Position(-40), null,
            new ManeuverAcceleration(10000), new ManeuverAcceleration(10000), new ManeuverSpeed(30000),
            new ManeuverTurnRate(45000)).ExecutablePlan!;
        movement.CommitStartTerminalManeuver(new ShipId(7), otherPlan, ManeuverObjective.FastestArrival, SimulationTime.Zero, static value => value);
        movement.CommitStartOrReplace(new ShipId(8), new TravelLeg.Local(Position(-10), Position(100),
            new SimulationDuration(4000)), SimulationTime.Zero, static value => value);
        return movement;
    }

    private static SystemPosition Position(long x, ulong system = 1) => new(new SystemId(system),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
