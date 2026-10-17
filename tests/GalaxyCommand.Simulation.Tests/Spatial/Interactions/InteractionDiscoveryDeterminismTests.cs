using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InteractionDiscoveryDeterminismTests
{
    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 7, true)]
    [InlineData(2, 1, true)]
    [InlineData(2, 3, false)]
    [InlineData(5, 1, false)]
    [InlineData(5, 3, true)]
    [InlineData(8, 7, false)]
    [InlineData(8, 7, true)]
    public void MixedForecastDiscoveryPreservesEvidenceAndExactAgendaOrder(int partitions, int batchSize, bool reverse)
    {
        (SpatialMovement movement, ShipPairInteractionInterest[] interests) = Fixture();
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        InteractionForecast[] reference = Evaluate(interests, view);
        Assert.True(reference.Length >= 10);
        Assert.Contains(reference, forecast => forecast.Candidate.Key.Timestamp == new SimulationTime(2237));
        Assert.Contains(reference, forecast => forecast.Candidate.Key.Timestamp == new SimulationTime(10));
        Assert.Contains(reference, forecast => forecast.Candidate.Contact.AlreadyInRange);
        ShipPairInteractionInterest[][] batches = Layout(interests, partitions, batchSize, reverse);
        // Repeated discovery represents overlapping candidate scopes, without
        // prescribing an index or pretending to execute concurrent workers.
        InteractionForecast[] discovered = batches.SelectMany(batch => Evaluate(batch, view))
            .Concat(batches.Reverse().SelectMany(batch => Evaluate(batch, view))).ToArray();
        Assert.Equal(InteractionCandidateNormalizer.Normalize(reference.Select(item => item.Candidate)),
            InteractionCandidateNormalizer.Normalize(discovered.Select(item => item.Candidate)));

        var expectedAgenda = new EventAgenda<InteractionForecast>();
        var actualAgenda = new EventAgenda<InteractionForecast>();
        var expectedScheduler = new InteractionForecastScheduler();
        var actualScheduler = new InteractionForecastScheduler();
        IReadOnlyList<EventKey> expectedKeys = expectedScheduler.Commit(expectedAgenda, reference, static value => value);
        IReadOnlyList<EventKey> actualKeys = actualScheduler.Commit(actualAgenda, discovered, static value => value);
        Assert.Equal(expectedKeys, actualKeys);
        Assert.Equal(reference.Length, actualScheduler.PendingCount);
        Assert.Empty(actualScheduler.Commit(actualAgenda, discovered.Reverse(), static value => value));
        Assert.Equal(expectedAgenda.CaptureCheckpoint().Value!.NextCreationSequence,
            actualAgenda.CaptureCheckpoint().Value!.NextCreationSequence);
        EventAgendaCheckpoint<InteractionForecast> checkpoint = actualAgenda.CaptureCheckpoint().Value!;
        var restoredScheduler = new InteractionForecastScheduler();
        restoredScheduler.RestorePending(checkpoint.PendingEvents.Reverse());
        CheckpointResult<EventAgenda<InteractionForecast>> restore = EventAgenda<InteractionForecast>.RestoreCheckpoint(checkpoint);
        Assert.True(restore.IsSuccess, restore.Failure?.Message);
        EventAgenda<InteractionForecast> restoredAgenda = restore.Value!;
        Assert.Empty(restoredScheduler.Commit(restoredAgenda, discovered, static value => value));
        Assert.Equal(checkpoint.NextCreationSequence, restoredAgenda.CaptureCheckpoint().Value!.NextCreationSequence);
        ScheduledEvent<InteractionForecast>[] expectedEvents = Drain(expectedAgenda);
        Assert.Equal(expectedEvents, Drain(actualAgenda));
        Assert.Equal(expectedEvents, Drain(restoredAgenda));
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(2, 3, true)]
    [InlineData(5, 7, false)]
    [InlineData(8, 1, true)]
    public void CallerScopedProximityPreservesResultsAcrossPartitionAndBatchLayouts(int partitions, int batchSize, bool reverse)
    {
        (SpatialMovement movement, _) = Fixture();
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(new SimulationTime(5));
        ShipId[] scope = Enumerable.Range(1, 6).Select(id => new ShipId((ulong)id))
            .Concat(Enumerable.Range(101, 6).Select(id => new ShipId((ulong)id)))
            .Append(new ShipId(999)).ToArray();
        var source = new ShipId(3);
        IReadOnlyList<ShipId> expected = InteractionProximityQuery.Find(view, source, scope, 1);
        Assert.Contains(new ShipId(2), expected);
        ShipId[][] batches = Layout(scope.Concat(scope.Reverse()).ToArray(), partitions, batchSize, reverse);
        ShipId[] actual = batches.SelectMany(batch => InteractionProximityQuery.Find(view, source, batch, 1))
            .Distinct().OrderBy(ship => ship.Value).ToArray();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(2, 3, true)]
    [InlineData(5, 2, false)]
    [InlineData(8, 7, true)]
    public void DueWaveDispositionsAndConsumptionIgnoreInputLayout(int partitions, int batchSize, bool reverse)
    {
        var movement = new SpatialMovement();
        for (ulong id = 1; id <= 6; id++) movement.Add(new ShipId(id), Position(1, 0));
        var owner = new InteractionInterestOwner("due-test", new IdSequence<InteractionInterestId>());
        owner.Register("replanned", new ShipId(1), new ShipId(2), 0);
        ShipPairInteractionInterest canceled = owner.Register("canceled", new ShipId(2), new ShipId(1), 0);
        owner.Register("removed", new ShipId(2), new ShipId(3), 0);
        owner.Register("forward", new ShipId(4), new ShipId(5), 0);
        owner.Register("reverse", new ShipId(5), new ShipId(4), 0);
        owner.Register("other", new ShipId(2), new ShipId(6), 0);
        using var coordinator = new SpatialInteractionCoordinator(movement, [owner]);
        var agenda = new EventAgenda<InteractionForecast>();
        coordinator.Reevaluate(agenda, SimulationTime.Zero, static value => value);
        ScheduledEvent<InteractionForecast>[] due = Drain(agenda);
        Assert.Equal(6, due.Length);
        owner.Cancel(canceled.Id);
        movement.CommitRemove(new ShipId(3), SimulationTime.Zero);
        movement.CommitStartOrReplace(new ShipId(1),
            new TravelLeg.Local(Position(1, 0), Position(1, 10), new SimulationDuration(10)),
            SimulationTime.Zero, static value => value);

        InteractionDueWave expected = coordinator.EvaluateDueWave(due, SimulationTime.Zero);
        InteractionDueWave actual = coordinator.EvaluateDueWave(
            Layout(due, partitions, batchSize, reverse).SelectMany(batch => batch), SimulationTime.Zero);
        Assert.Equal(expected.Results, actual.Results);
        Assert.Equal(3, actual.Results.Count(result => result.Disposition == ScheduledEventDisposition.Applied));
        Assert.Equal(2, actual.Results.Count(result => result.Disposition == ScheduledEventDisposition.IgnoredMissingReference));
        Assert.Equal(1, actual.Results.Count(result => result.Disposition == ScheduledEventDisposition.IgnoredStaleGeneration));
        Assert.Equal(6, coordinator.Scheduler.PendingCount);
        coordinator.CompleteDueWave(actual);
        Assert.Equal(0, coordinator.Scheduler.PendingCount);
    }

    /// <summary>Changes assignment and completion order while preserving the complete logical input wave.</summary>
    private static T[][] Layout<T>(T[] inputs, int partitions, int batchSize, bool reverse)
    {
        IEnumerable<T[]> batches = Enumerable.Range(0, partitions)
            .SelectMany(partition => inputs.Where((_, index) => index % partitions == partition).Chunk(batchSize));
        return (reverse ? batches.Reverse().Select(batch => batch.Reverse().ToArray()) : batches).ToArray();
    }

    private static InteractionForecast[] Evaluate(IEnumerable<ShipPairInteractionInterest> interests, InteractionSpatialReadView view) =>
        interests.Select(interest => InteractionForecast.Evaluate(interest, view, new SimulationTime(6000)))
            .OfType<InteractionForecast>().ToArray();

    private static ScheduledEvent<InteractionForecast>[] Drain(EventAgenda<InteractionForecast> agenda)
    {
        var events = new List<ScheduledEvent<InteractionForecast>>();
        while (agenda.PopNextThrough(new SimulationTime(6000)) is { } scheduled) events.Add(scheduled);
        return events.ToArray();
    }

    /// <summary>Provides directed interests, simultaneous crossings, analytic contact, absent and cross-system pairs.</summary>
    private static (SpatialMovement Movement, ShipPairInteractionInterest[] Interests) Fixture()
    {
        var movement = new SpatialMovement();
        var owner = new InteractionInterestOwner("determinism-test", new IdSequence<InteractionInterestId>());
        foreach (ulong system in new ulong[] { 1, 2 })
        {
            ulong offset = (system - 1) * 100;
            long[] positions = [-10, 0, 0, 10, 25, 999];
            for (int index = positions.Length - 1; index >= 0; index--)
                movement.Add(new ShipId(offset + (ulong)index + 1), Position(system, positions[index]));
            movement.CommitStartOrReplace(new ShipId(offset + 1),
                new TravelLeg.Local(Position(system, -10), Position(system, 10), new SimulationDuration(20)),
                SimulationTime.Zero, static value => value);
            movement.CommitStartOrReplace(new ShipId(offset + 4),
                new TravelLeg.Local(Position(system, 10), Position(system, -10), new SimulationDuration(20)),
                SimulationTime.Zero, static value => value);
            ExecutableBoundedTerminalManeuverPlan plan = BoundedTerminalManeuverPlanner.Select(
                SimulationTime.Zero, new ShipKinematicState(Position(system, 0), ShipVelocity.Zero, ShipHeading.Zero),
                Position(system, 90), null, new ManeuverAcceleration(10_000), new ManeuverAcceleration(10_000),
                new ManeuverSpeed(30_000), new ManeuverTurnRate(45_000)).ExecutablePlan!;
            movement.CommitStartTerminalManeuver(new ShipId(offset + 3), plan,
                ManeuverObjective.FastestArrival, SimulationTime.Zero, static value => value);
            owner.Register($"linear-forward-{system}", new ShipId(offset + 1), new ShipId(offset + 2), 0);
            owner.Register($"linear-reverse-{system}", new ShipId(offset + 2), new ShipId(offset + 1), 0);
            owner.Register($"two-linear-{system}", new ShipId(offset + 1), new ShipId(offset + 4), 0);
            owner.Register($"analytic-{system}", new ShipId(offset + 3), new ShipId(offset + 5), 0);
            owner.Register($"already-{system}", new ShipId(offset + 3), new ShipId(offset + 2), 0);
            owner.Register($"outside-{system}", new ShipId(offset + 3), new ShipId(offset + 6), 0);
        }
        owner.Register("cross-system", new ShipId(1), new ShipId(101), ulong.MaxValue);
        owner.Register("missing", new ShipId(1), new ShipId(999), ulong.MaxValue);
        return (movement, owner.CaptureSnapshot().ToArray());
    }

    private static SystemPosition Position(ulong system, long x) => new(new SystemId(system),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
