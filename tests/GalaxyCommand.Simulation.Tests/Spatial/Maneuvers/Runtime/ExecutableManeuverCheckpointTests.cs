using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ExecutableManeuverCheckpointTests
{
    [Theory]
    [InlineData(0, 0, 90_000)]
    [InlineData(90, 0, 90_000)]
    [InlineData(500, 0, 0)]
    [InlineData(100_000, 0, 90_000)]
    public void RestoredConcretePayloadPreservesEveryMillisecondAndSplitSchedule(long x, long y, uint heading)
    {
        ExecutableBoundedTerminalManeuverPlan original = Select(x, y, heading);
        if (original.EndsAt.Milliseconds > 2)
            original = original.WithAdditionalBoundaries([new SimulationTime(1)]);

        ExecutableBoundedTerminalManeuverPlan restored = ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(
            original.CaptureCheckpoint());

        AssertEquivalent(original, restored);
    }

    [Fact]
    public void DeepWaypointContinuationRestoresOneExactSchedule()
    {
        const ulong depth = 40;
        ShipKinematicState state = State(ShipVelocity.Zero);
        ExecutableBoundedTerminalManeuverPlan original = ExecutableBoundedTerminalManeuverPlan.TerminalSettle(new(depth), state);
        for (ulong index = depth; index > 0; index--)
            original = ExecutableBoundedTerminalManeuverPlan.WaypointRoute([
                new AnalyticManeuverSegment(new(index - 1), new(index), state, ShipAcceleration.Zero, ShipAngularRate.Zero),
            ], original);
        ExecutableBoundedTerminalManeuverPlan restored = ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(original.CaptureCheckpoint());
        Assert.Equal((int)depth + 1, restored.Phases.Count);
        AssertEquivalent(original, restored);
    }

    [Fact]
    public void RestoredPassiveDragPreservesFractionalStopAndHeading()
    {
        var original = ExecutableBoundedTerminalManeuverPlan.FromPassiveDrag(new DecelerationManeuverSegment(
            ManeuverDecelerationKind.PassiveDrag, SimulationTime.Zero, State(new ShipVelocity(1111, 2222)),
            new ManeuverAcceleration(777)));
        AssertEquivalent(original, ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(original.CaptureCheckpoint()));
    }

    [Fact]
    public void RestoredWaypointCompositionRetainsTransitionRotationAndTerminalEvaluator()
    {
        ShipKinematicState start = State(ShipVelocity.Zero);
        var transition = new AnalyticManeuverSegment(SimulationTime.Zero, new SimulationTime(10), start,
            ShipAcceleration.Zero, new ShipAngularRate(12_000));
        var original = ExecutableBoundedTerminalManeuverPlan.WaypointRoute([transition],
            ExecutableBoundedTerminalManeuverPlan.TerminalSettle(new SimulationTime(10), transition.StateAt(new SimulationTime(10))));
        AssertEquivalent(original, ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(original.CaptureCheckpoint()));
    }

    [Fact]
    public void RestoreRejectsScheduleThatChangesOriginalPhaseKind()
    {
        ExecutableManeuverCheckpoint checkpoint = Select(90, 0, 90_000).CaptureCheckpoint();
        ManeuverScheduledPhase[] phases = checkpoint.Phases.ToArray();
        phases[0] = new ManeuverScheduledPhase(ManeuverPhaseKind.CoastUnderPassiveDrag, phases[0].StartsAt, phases[0].EndsAt);
        Assert.Throws<ArgumentException>(() => ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(
            checkpoint with { Phases = phases }));
    }

    [Theory]
    [InlineData(0, 90, 30_000)]
    [InlineData(30_000, 45, 300_000)]
    [InlineData(30_000, 100, 30_000)]
    public void RestoredLegacyAlignedPayloadPreservesSelectedBrakingAndAcceleration(long velocity, long destination, ulong cap)
    {
        ExecutableBoundedTerminalManeuverPlan original = BoundedTerminalManeuverPlanner.Select(
            SimulationTime.Zero, State(new ShipVelocity(velocity, 0)), Position(destination), null,
            new ManeuverAcceleration(10_000), new ManeuverAcceleration(10_000), new ManeuverSpeed(cap),
            new ManeuverTurnRate(45_000)).ExecutablePlan!;
        AssertEquivalent(original, ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(original.CaptureCheckpoint()));
    }

    [Fact]
    public void RestoredForcedDropoutPreservesItsNestedSelectedContinuation()
    {
        Assert.True(ForcedCruiseDropoutTerminalManeuverPlan.TryCreate(SimulationTime.Zero,
            State(new ShipVelocity(1_000_000, 0)), Position(100_000), null, Capability(),
            ManeuverObjective.FastestArrival, out ForcedCruiseDropoutTerminalManeuverPlan? selected));
        var original = ExecutableBoundedTerminalManeuverPlan.From(selected!);
        AssertEquivalent(original, ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(original.CaptureCheckpoint()));
    }

    [Theory]
    [InlineData(1_000, 0)]
    [InlineData(0, 2_000)]
    public void RestoredMovingCandidatesPreserveTheirSelectedContinuation(long vx, long vy)
    {
        ExecutableBoundedTerminalManeuverPlan original = BoundedTerminalManeuverPlanner.Select(
            SimulationTime.Zero, State(new ShipVelocity(vx, vy)), Position(90), new ShipHeading(90_000),
            Capability(), ManeuverObjective.FastestArrival).ExecutablePlan!;
        AssertEquivalent(original, ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(original.CaptureCheckpoint()));
    }

    [Fact]
    public void RestoredReducedThrustPreservesSelectedDiscreteSegments()
    {
        Assert.True(ReducedThrustTerminalManeuverPlan.TryCreateFromRest(SimulationTime.Zero,
            State(ShipVelocity.Zero), Position(2), new ManeuverAcceleration(10_000), new ManeuverSpeed(30_000),
            out ReducedThrustTerminalManeuverPlan? selected));
        var original = ExecutableBoundedTerminalManeuverPlan.From(selected!);
        AssertEquivalent(original, ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(original.CaptureCheckpoint()));
    }

    [Fact]
    public void RestoredBrakeOnlyTerminalCandidateNeedsNoInventedContinuation()
    {
        Assert.True(BrakeThenStationaryDirectionalManeuverPlan.TryCreate(SimulationTime.Zero,
            State(new ShipVelocity(0, 1_100)), Position(0), null, Capability(), ManeuverObjective.FastestArrival,
            out BrakeThenStationaryDirectionalManeuverPlan? selected));
        Assert.Null(selected!.StationaryPlan);
        Assert.Null(selected.ReducedThrustPlan);
        var original = ExecutableBoundedTerminalManeuverPlan.From(selected!);
        AssertEquivalent(original, ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(original.CaptureCheckpoint()));
    }

    [Theory]
    [InlineData("time")]
    [InlineData("velocity")]
    [InlineData("heading")]
    [InlineData("position")]
    public void RestoreRejectsCorruptFuturePhaseWhileCurrentStateIsUnchanged(string field)
    {
        ExecutableManeuverCheckpoint checkpoint = BoundedTerminalManeuverPlanner.Select(
            SimulationTime.Zero, State(ShipVelocity.Zero), Position(90), null,
            new ManeuverAcceleration(10_000), new ManeuverAcceleration(10_000), new ManeuverSpeed(30_000),
            new ManeuverTurnRate(45_000)).ExecutablePlan!.CaptureCheckpoint();
        var selected = Assert.IsType<ExecutableManeuverPayload.Triangular>(checkpoint.Payload).Plan;
        DecelerationManeuverSegment braking = selected.BrakingPhase;
        ShipKinematicState corruptStart = field switch
        {
            "velocity" => braking.Start with { Velocity = new ShipVelocity(12_345, 0) },
            "heading" => braking.Start with { Heading = new ShipHeading(90_000) },
            "position" => braking.Start with { Position = Position(999) },
            _ => braking.Start,
        };
        var corruptBraking = new DecelerationManeuverSegment(braking.Kind,
            field == "time" ? new SimulationTime(braking.StartsAt.Milliseconds + 1) : braking.StartsAt,
            corruptStart, braking.Deceleration);
        var constructor = typeof(ShortMoveTriangularPlan).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 4);
        var corrupt = (ShortMoveTriangularPlan)constructor.Invoke([
            selected.Destination, selected.Profile, selected.AccelerationPhase, corruptBraking]);
        Exception failure = Assert.ThrowsAny<Exception>(() => ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(
            checkpoint with { Payload = new ExecutableManeuverPayload.Triangular(corrupt) }));
        Assert.True(failure is ArgumentException or InvalidOperationException);
    }

    private static EffectiveShipManeuverCapability Capability() => new ShipManeuverCapability(10_000,
        new ManeuverAcceleration(10_000), null, new ManeuverSpeed(300_000), new ManeuverSpeed(1_000_000),
        new ManeuverTurnRate(45_000), new SimulationDuration(10_000)).ResolveForMass(10_000);

    private static SystemPosition Position(long x) => new(new SystemId(1),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));

    private static void AssertEquivalent(ExecutableBoundedTerminalManeuverPlan original,
        ExecutableBoundedTerminalManeuverPlan restored)
    {
        Assert.Equal(original.Kind, restored.Kind);
        Assert.Equal(original.Phases, restored.Phases);
        for (ulong time = original.StartsAt.Milliseconds; time <= original.EndsAt.Milliseconds; time++)
            Assert.Equal(original.StateAt(new SimulationTime(time)), restored.StateAt(new SimulationTime(time)));
        Assert.Equal(original.InteractionCurves.Count, restored.InteractionCurves.Count);
        for (int index = 0; index < original.InteractionCurves.Count; index++)
        {
            var expected = original.InteractionCurves[index].Pieces(original.StartsAt);
            var actual = restored.InteractionCurves[index].Pieces(restored.StartsAt);
            Assert.Equal(expected.Count, actual.Count);
            for (int piece = 0; piece < expected.Count; piece++)
            {
                Assert.Equal(expected[piece].From, actual[piece].From);
                Assert.Equal(expected[piece].Through, actual[piece].Through);
                Assert.Equal(expected[piece].X, actual[piece].X);
                Assert.Equal(expected[piece].Y, actual[piece].Y);
                Assert.Equal(expected[piece].Denominator, actual[piece].Denominator);
            }
        }
    }

    private static ExecutableBoundedTerminalManeuverPlan Select(long x, long y, uint heading) =>
        BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero, State(ShipVelocity.Zero),
            new SystemPosition(new SystemId(1), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(y))),
            new ShipHeading(heading), new ShipManeuverCapability(10_000, new ManeuverAcceleration(10_000), null,
                new ManeuverSpeed(30_000), new ManeuverSpeed(1_000_000), new ManeuverTurnRate(45_000),
                new SimulationDuration(10_000)).ResolveForMass(10_000), ManeuverObjective.FastestArrival).ExecutablePlan!;

    private static ShipKinematicState State(ShipVelocity velocity) => new(
        new SystemPosition(new SystemId(1), new SpatialPosition(new SpatialCoordinate(0), new SpatialCoordinate(0))),
        velocity, new ShipHeading(0));
}
