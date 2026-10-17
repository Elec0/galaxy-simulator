using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionBatchExecutionTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 7)]
    [InlineData(8, 16)]
    public void ForecastEvidenceAndAgendaKeysMatchSerialAcrossRealWorkers(int workers, int batchSize)
    {
        var (view, interests) = Fixture();
        var through = new SimulationTime(6000);
        var expected = interests.Select(interest => InteractionForecast.Evaluate(interest, view, through))
            .OfType<InteractionForecast>().OrderBy(forecast => forecast.Candidate.Key).ToArray();
        Assert.NotEmpty(expected);
        for (int repeat = 0; repeat < 3; repeat++)
        {
            var actual = InteractionForecast.EvaluateBatch(interests.Reverse().Concat(interests), view, through,
                new InteractionBatchExecution(workers, batchSize));
            Assert.Equal(expected, actual);
            var referenceAgenda = new EventAgenda<InteractionForecast>();
            var actualAgenda = new EventAgenda<InteractionForecast>();
            Assert.Equal(new InteractionForecastScheduler().Commit(referenceAgenda, expected, static forecast => forecast),
                new InteractionForecastScheduler().Commit(actualAgenda, actual, static forecast => forecast));
            Assert.Throws<NotSupportedException>(() => ((IList<InteractionForecast>)actual).Clear());
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 7)]
    [InlineData(8, 16)]
    public void PreparedQueryBatchesRetainSourceOrderAndExactResults(int workers, int batchSize)
    {
        var (view, _) = Fixture();
        ShipId[] scope = [new(4), new(3), new(2), new(1), new(3)];
        ShipId[] sources = [new(99), new(3), new(2), new(1), new(3), new(4)];
        foreach (var strategy in new[] { InteractionPreparationStrategy.Scan, InteractionPreparationStrategy.SparseGrid(16, 4096),
            InteractionPreparationStrategy.BoundingBoxHierarchy(4) })
        {
            var instant = InteractionProximityQuery.Prepare(view).PrepareScope(scope, strategy);
            var swept = InteractionSweptQuery.Prepare(view, new SimulationTime(6000)).PrepareScope(scope, strategy);
            var execution = new InteractionBatchExecution(workers, batchSize);
            var instantResults = instant.FindBatch(sources, 10, execution);
            var sweptResults = swept.FindBatch(sources, 10, execution);
            Assert.Equal(sources.Length, instantResults.Count);
            Assert.Equal(sources.Length, sweptResults.Count);
            for (int i = 0; i < sources.Length; i++)
            {
                Assert.Equal(instant.Find(sources[i], 10), instantResults[i]);
                Assert.Equal(swept.Find(sources[i], 10), sweptResults[i]);
            }
            Assert.Throws<NotSupportedException>(() => ((IList<IReadOnlyList<ShipId>>)instantResults).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<IReadOnlyList<InteractionSweptMatch>>)sweptResults).Clear());
        }
    }

    [Fact]
    public void ExecutionRequiresPositiveExplicitTuning()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InteractionBatchExecution(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InteractionBatchExecution(1, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void MultipleEvaluationFailuresRethrowTheEarliestInputException(int workers)
    {
        var execution = new InteractionBatchExecution(workers, 2);
        var expected = new InvalidOperationException("earliest input failure");
        using var laterFailed = new ManualResetEventSlim();
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => execution.Evaluate(20, index =>
        {
            if (index == 1)
            {
                if (workers > 1) Assert.True(laterFailed.Wait(TimeSpan.FromSeconds(10)));
                throw expected;
            }
            if (index == 5)
            {
                laterFailed.Set();
                throw new ArgumentException("later input failure");
            }
            return index;
        })));
    }

    [Fact]
    public void SharedPoolRunsCoarseBatchesConcurrently()
    {
        using var barrier = new Barrier(2);
        int entered = 0;
        var execution = new InteractionBatchExecution(2, 4);
        int[] results = execution.Evaluate(8, index =>
        {
            if (index is 0 or 4)
            {
                Interlocked.Increment(ref entered);
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            }
            return index;
        });
        Assert.Equal(2, entered);
        Assert.Equal(Enumerable.Range(0, 8), results);
    }

    [Fact]
    public void PublicBatchBoundariesRejectNullsAndRetainEmptyResults()
    {
        var (view, _) = Fixture();
        var execution = new InteractionBatchExecution(4, 3);
        var instant = InteractionProximityQuery.Prepare(view).PrepareScope([]);
        var swept = InteractionSweptQuery.Prepare(view, new SimulationTime(6000)).PrepareScope([]);
        Assert.Throws<ArgumentNullException>(() => instant.FindBatch(null!, 0, execution));
        Assert.Throws<ArgumentNullException>(() => swept.FindBatch([], 0, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => instant.FindBatch([new ShipId(1), default], 0, execution));
        Assert.Throws<ArgumentNullException>(() => InteractionForecast.EvaluateBatch([], null!, SimulationTime.Zero, execution));
        Assert.Throws<ArgumentNullException>(() => InteractionForecast.EvaluateBatch([], view, SimulationTime.Zero, null!));
        Assert.Empty(instant.FindBatch([], 0, execution));
        Assert.Empty(swept.FindBatch([], 0, execution));
        Assert.Empty(InteractionForecast.EvaluateBatch([], view, SimulationTime.Zero, execution));
    }

    [Fact]
    public void BatchReductionRejectsConflictingCorrelationBeforePublication()
    {
        var (view, interests) = Fixture();
        var first = interests[0];
        var conflict = new ShipPairInteractionInterest(first.Id, first.DomainKind, first.PurposeId,
            first.Source, first.Target, first.RangeUnits, new EventGeneration(123));
        Assert.Throws<InvalidOperationException>(() => InteractionForecast.EvaluateBatch([first, conflict], view,
            new SimulationTime(6000), new InteractionBatchExecution(4, 1)));
    }

    private static (InteractionSpatialReadView View, ShipPairInteractionInterest[] Interests) Fixture()
    {
        var movement = new SpatialMovement();
        for (ulong i = 1; i <= 4; i++) movement.Add(new ShipId(i), Position((long)(i - 1) * 10));
        movement.CommitStartOrReplace(new ShipId(1), new TravelLeg.Local(Position(0), Position(40), new SimulationDuration(1000)),
            SimulationTime.Zero, static value => value);
        var plan = BoundedTerminalManeuverPlanner.Select(SimulationTime.Zero,
            new ShipKinematicState(Position(20), ShipVelocity.Zero, ShipHeading.Zero), Position(110), null,
            new ManeuverAcceleration(10000), new ManeuverAcceleration(10000), new ManeuverSpeed(30000),
            new ManeuverTurnRate(45000)).ExecutablePlan!;
        movement.CommitStartTerminalManeuver(new ShipId(3), plan, ManeuverObjective.FastestArrival, SimulationTime.Zero, static value => value);
        var owner = new InteractionInterestOwner("batch", new IdSequence<InteractionInterestId>());
        for (int i = 0; i < 40; i++) owner.Register($"contact-{i}", new ShipId((ulong)(i % 3) + 1), new ShipId(4), (ulong)(i % 2) * 10);
        return (movement.CaptureInteractionReadView(SimulationTime.Zero), owner.CaptureSnapshot().ToArray());
    }

    private static SystemPosition Position(long x) => new(new SystemId(1), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
