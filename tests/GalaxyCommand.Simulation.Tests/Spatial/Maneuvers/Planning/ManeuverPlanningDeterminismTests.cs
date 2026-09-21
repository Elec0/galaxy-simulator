using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ManeuverPlanningDeterminismTests
{
    [Theory]
    [InlineData(2, 3)]
    [InlineData(4, 7)]
    public void IndependentPlansMatchReferenceAcrossWorkerAndBatchLayouts(
        int workerCount,
        int batchSize)
    {
        PlanningOutcome reference = RunLayout(
            workerCount: 1,
            batchSize: 1,
            reversePartitions: false);

        PlanningOutcome actual = RunLayout(
            workerCount,
            batchSize,
            reversePartitions: true);

        Assert.Equal(reference.CommittedSnapshots, actual.CommittedSnapshots);
        Assert.Equal(reference.CompletedSnapshots, actual.CompletedSnapshots);
        Assert.Equal(reference.Dispositions, actual.Dispositions);
        Assert.NotEmpty(actual.Dispositions);
        Assert.All(
            actual.Dispositions,
            static disposition =>
                Assert.Equal(ScheduledEventDisposition.Applied, disposition));
    }

    /// <summary>
    /// Evaluates immutable plans in the requested partition layout, then
    /// commits them in stable ship order so worker completion never allocates
    /// authoritative motion or agenda identity.
    /// </summary>
    private static PlanningOutcome RunLayout(
        int workerCount,
        int batchSize,
        bool reversePartitions)
    {
        PlanningInput[] inputs = CreateInputs();
        var plans = new ExecutableBoundedTerminalManeuverPlan[inputs.Length];
        (int Start, int End)[] partitions = Enumerable
            .Range(0, (inputs.Length + batchSize - 1) / batchSize)
            .Select(index =>
            {
                int start = index * batchSize;
                return (start, Math.Min(start + batchSize, inputs.Length));
            })
            .ToArray();
        if (reversePartitions)
        {
            Array.Reverse(partitions);
        }

        Parallel.ForEach(
            partitions,
            new ParallelOptions { MaxDegreeOfParallelism = workerCount },
            partition =>
            {
                for (int index = partition.Start; index < partition.End; index++)
                {
                    PlanningInput input = inputs[index];
                    BoundedTerminalPlanSelection selection =
                        BoundedTerminalManeuverPlanner.Select(
                            SimulationTime.Zero,
                            input.Start,
                            input.Destination,
                            requestedHeading: null,
                            Capability,
                            ManeuverObjective.FastestArrival);
                    plans[index] = selection.ExecutablePlan
                        ?? throw new InvalidOperationException(
                            $"Stress input {index} produced no executable plan.");
                }
            });

        var movement = new SpatialMovement();
        var agenda = new EventAgenda<SpatialMovementEvent>();
        var runtime = new PlanningRuntime(movement);
        var engine = new SimulationEngine<SpatialMovementEvent>(runtime, agenda);
        var proposals = new List<AgendaEventProposal<SpatialMovementEvent>>();
        SimulationTime endsAt = SimulationTime.Zero;
        for (int index = 0; index < inputs.Length; index++)
        {
            PlanningInput input = inputs[index];
            movement.Add(input.ShipId, input.Start.Position, input.Start.Heading);
            TerminalManeuverCommit<SpatialMovementEvent> commit =
                movement.CommitStartTerminalManeuver(
                    input.ShipId,
                    plans[index],
                    ManeuverObjective.FastestArrival,
                    SimulationTime.Zero,
                    static movementEvent => movementEvent);
            proposals.AddRange(commit.EventProposals);
            if (plans[index].EndsAt > endsAt)
            {
                endsAt = plans[index].EndsAt;
            }
        }

        AgendaEventProposal<SpatialMovementEvent>[] ordered = proposals
            .OrderBy(static proposal => proposal.Order)
            .ToArray();
        AgendaCommitResult agendaCommit = AgendaCommitOwner.Commit(
            agenda,
            ordered);
        foreach (IGrouping<ShipId, EventKey> shipEvents in ordered
            .Zip(
                agendaCommit.EventKeys,
                static (proposal, key) =>
                    (ShipId: ((SpatialMovementEvent.Maneuver)proposal.Payload).ShipId,
                        Key: key))
            .GroupBy(static entry => entry.ShipId, static entry => entry.Key))
        {
            movement.BindTerminalManeuverEvents(
                shipEvents.Key,
                shipEvents.ToArray());
        }

        ShipSpatialSnapshot[] committedSnapshots = movement
            .CaptureSnapshot(SimulationTime.Zero)
            .ToArray();
        engine.RunUntil(endsAt);
        return new PlanningOutcome(
            committedSnapshots,
            movement.CaptureSnapshot(endsAt).ToArray(),
            runtime.Dispositions.ToArray());
    }

    /// <summary>
    /// Produces independent long cardinal routes with varied starting headings
    /// so the stress case covers both immediate translation and exact turns.
    /// </summary>
    private static PlanningInput[] CreateInputs() =>
        Enumerable.Range(0, 48)
            .Select(index =>
            {
                long x = checked(index * 1_000_000L);
                long y = checked((index % 6) * 250_000L);
                var start = new ShipKinematicState(
                    Position(x, y),
                    ShipVelocity.Zero,
                    new ShipHeading(checked((uint)((index % 4) * 90_000))));
                long direction = (index & 1) == 0 ? 1 : -1;
                var destination = Position(
                    checked(x + (direction * 100_000_000L)),
                    y);
                return new PlanningInput(
                    new ShipId(checked((ulong)index + 1)),
                    start,
                    destination);
            })
            .ToArray();

    private static SystemPosition Position(long x, long y) =>
        new(
            new SystemId(1),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));

    private static EffectiveShipManeuverCapability Capability { get; } =
        new ShipManeuverCapability(
            baseMassKilograms: 10_000,
            new ManeuverAcceleration(10_000),
            customPassiveDeceleration: null,
            new ManeuverSpeed(300_000),
            new ManeuverSpeed(1_000_000),
            new ManeuverTurnRate(45_000),
            new SimulationDuration(10_000))
        .ResolveForMass(10_000);

    private sealed record PlanningInput(
        ShipId ShipId,
        ShipKinematicState Start,
        SystemPosition Destination);

    private sealed record PlanningOutcome(
        IReadOnlyList<ShipSpatialSnapshot> CommittedSnapshots,
        IReadOnlyList<ShipSpatialSnapshot> CompletedSnapshots,
        IReadOnlyList<ScheduledEventDisposition> Dispositions);

    private sealed class PlanningRuntime
        : ISimulationRuntime<SpatialMovementEvent>
    {
        private readonly SpatialMovement _movement;

        public PlanningRuntime(SpatialMovement movement)
        {
            _movement = movement;
        }

        public List<ScheduledEventDisposition> Dispositions { get; } = [];

        public bool ShouldStop => false;

        public void Reconcile(
            SimulationTime now,
            EventAgenda<SpatialMovementEvent> agenda)
        {
        }

        public void AccrueTo(SimulationTime now)
        {
        }

        public ScheduledEventDisposition HandleEvent(
            ScheduledEvent<SpatialMovementEvent> simulationEvent,
            SimulationTime now,
            EventAgenda<SpatialMovementEvent> agenda)
            => _movement.HandleEvent(
                simulationEvent.Payload,
                simulationEvent.Generation,
                now);

        public void RecordEvent(
            ScheduledEvent<SpatialMovementEvent> simulationEvent,
            ScheduledEventDisposition disposition) =>
            Dispositions.Add(disposition);
    }
}
