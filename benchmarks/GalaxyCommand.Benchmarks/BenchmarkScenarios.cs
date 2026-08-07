using System.Collections.ObjectModel;
using System.Globalization;
using GalaxyCommand.Simulation;
using GalaxyCommand.Simulation.Acceptance;

namespace GalaxyCommand.Benchmarks;

public sealed record ScenarioCorrectnessResult(
    string Digest,
    ulong SimulatedMilliseconds,
    IReadOnlyDictionary<string, long> Counts);

internal interface IBenchmarkScenario
{
    ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration);
}

internal static class BenchmarkScenarioFactory
{
    internal static IBenchmarkScenario Create(string basePreset) =>
        basePreset switch
        {
            BenchmarkPresets.PhaseOneBaseline => new PhaseOneBenchmarkScenario(),
            BenchmarkPresets.SpatialManyQuiet => new SpatialBenchmarkScenario(),
            BenchmarkPresets.SpatialOneCrowded => new SpatialBenchmarkScenario(),
            BenchmarkPresets.NavigationConnectorVolume =>
                new ConnectorNavigationBenchmarkScenario(),
            BenchmarkPresets.FactsRetentionAndRead => new FactBenchmarkScenario(),
            _ => throw new BenchmarkUsageException(
                $"Unsupported benchmark base preset '{basePreset}'."),
        };
}

internal sealed class PhaseOneBenchmarkScenario : IBenchmarkScenario
{
    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration)
    {
        var scenario = new PhaseOneScenario(new PhaseOneConfig
        {
            RandomSeed = configuration.GetUInt64(BenchmarkParameterNames.Seed),
        });
        PhaseOneReport report = scenario.RunUntilFirstShip(
            new SimulationTime(configuration.GetUInt64(
                BenchmarkParameterNames.SimulatedDurationMilliseconds)));
        return new ScenarioCorrectnessResult(
            $"{report.EventLogDigest:x16}:{report.FinalStateDigest:x16}",
            report.EndTime.Milliseconds - report.StartTime.Milliseconds,
            ScenarioSetup.Counts(
                ("commands", 0),
                ("decisions", scenario.DecisionRecords.Count),
                ("events", checked((long)report.EventsProcessed)),
                ("facts", 0),
                ("ships", report.EndingShipCount)));
    }
}

internal sealed class SpatialBenchmarkScenario : IBenchmarkScenario
{
    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration)
    {
        int systemCount = configuration.GetInt32(BenchmarkParameterNames.SystemCount);
        int shipCount = configuration.GetInt32(BenchmarkParameterNames.ShipCount);
        int activeShipCount = configuration.GetInt32(
            BenchmarkParameterNames.ActiveShipCount);
        long distance = configuration.Get(BenchmarkParameterNames.DestinationDistance);
        SimulationDuration travelDuration = new(
            configuration.GetUInt64(BenchmarkParameterNames.TravelDurationMilliseconds));
        StarSystem[] systems = ScenarioSetup.CreateSystems(systemCount);
        InitialShipSetup[] ships = ScenarioSetup.CreateShips(
            shipCount,
            systemCount);
        var session = new GameSession(
            new GameSessionSetup(
                systems,
                ships,
                ScenarioSetup.Relationships,
                configuration.GetInt32(BenchmarkParameterNames.FactRetentionCapacity)),
            new DirectLocalNavigationPlanner(
                new FixedTravelTimeEstimator(travelDuration)));
        CommandSource source = ScenarioSetup.AutonomousSource;
        for (int index = 0; index < activeShipCount; index++)
        {
            InitialShipSetup ship = ships[index];
            var destination = new SystemPosition(
                ship.Position.SystemId,
                new SpatialPosition(
                    new SpatialCoordinate(checked(ship.Position.Position.X.Units + distance)),
                    ship.Position.Position.Y));
            GameplayCommandRecord command = session.SubmitCommand(
                source,
                new MoveShipCommand(
                    ship.Id,
                    new NavigationDestination.Position(destination),
                    OrderPlacement.ReplaceAll));
            ScenarioSetup.RequireAccepted(command, configuration.Id);
        }

        session.AdvanceTo(new SimulationTime(configuration.GetUInt64(
            BenchmarkParameterNames.SimulatedDurationMilliseconds)));
        return ScenarioResult.Create(session, configuration);
    }
}

internal sealed class ConnectorNavigationBenchmarkScenario : IBenchmarkScenario
{
    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration)
    {
        int systemCount = configuration.GetInt32(BenchmarkParameterNames.SystemCount);
        int shipCount = configuration.GetInt32(BenchmarkParameterNames.ShipCount);
        int activeShipCount = configuration.GetInt32(
            BenchmarkParameterNames.ActiveShipCount);
        long distance = configuration.Get(BenchmarkParameterNames.DestinationDistance);
        SimulationDuration duration = new(
            configuration.GetUInt64(BenchmarkParameterNames.TravelDurationMilliseconds));
        StarSystem[] systems = ScenarioSetup.CreateSystems(systemCount);
        InitialShipSetup[] ships = ScenarioSetup.CreateShips(shipCount, 1);
        ConnectorTopology topology = CreateChain(systemCount, distance, duration);
        var session = new GameSession(
            new GameSessionSetup(
                systems,
                ships,
                topology,
                ScenarioSetup.Relationships,
                configuration.GetInt32(BenchmarkParameterNames.FactRetentionCapacity)),
            new HierarchicalNavigationPlanner(
                topology,
                new FixedTravelTimeEstimator(duration)));
        var destination = new NavigationDestination.System(
            new SystemId(checked((ulong)systemCount)));
        for (int index = 0; index < activeShipCount; index++)
        {
            GameplayCommandRecord command = session.SubmitCommand(
                ScenarioSetup.AutonomousSource,
                new MoveShipCommand(
                    ships[index].Id,
                    destination,
                    OrderPlacement.ReplaceAll));
            ScenarioSetup.RequireAccepted(command, configuration.Id);
        }

        session.AdvanceTo(new SimulationTime(configuration.GetUInt64(
            BenchmarkParameterNames.SimulatedDurationMilliseconds)));
        return ScenarioResult.Create(session, configuration);
    }

    private static ConnectorTopology CreateChain(
        int systemCount,
        long distance,
        SimulationDuration duration)
    {
        var endpoints = new List<ConnectorEndpoint>(checked((systemCount - 1) * 2));
        var connections = new List<TransitConnection>(systemCount - 1);
        ulong endpointValue = 1;
        for (int systemIndex = 1; systemIndex < systemCount; systemIndex++)
        {
            var source = new ConnectorEndpoint(
                new ConnectorEndpointId(endpointValue++),
                ScenarioSetup.Position(systemIndex, distance, 0));
            var destination = new ConnectorEndpoint(
                new ConnectorEndpointId(endpointValue++),
                ScenarioSetup.Position(systemIndex + 1, 0, 0));
            endpoints.Add(source);
            endpoints.Add(destination);
            connections.Add(new TransitConnection(
                new TransitConnectionId(checked((ulong)systemIndex)),
                source.Id,
                destination.Id,
                duration));
        }

        return new ConnectorTopology(endpoints, connections);
    }
}

internal sealed class FactBenchmarkScenario : IBenchmarkScenario
{
    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration)
    {
        long distance = configuration.Get(BenchmarkParameterNames.DestinationDistance);
        SimulationDuration duration = new(
            configuration.GetUInt64(BenchmarkParameterNames.TravelDurationMilliseconds));
        StarSystem[] systems = ScenarioSetup.CreateSystems(1);
        InitialShipSetup[] ships = ScenarioSetup.CreateShips(1, 1);
        var session = new GameSession(
            new GameSessionSetup(
                systems,
                ships,
                ScenarioSetup.Relationships,
                configuration.GetInt32(BenchmarkParameterNames.FactRetentionCapacity)),
            new DirectLocalNavigationPlanner(
                new FixedTravelTimeEstimator(duration)));
        int commandCount = configuration.GetInt32(BenchmarkParameterNames.CommandCount);
        for (int index = 0; index < commandCount; index++)
        {
            long destinationX = (index & 1) == 0 ? distance : -distance;
            GameplayCommandRecord command = session.SubmitCommand(
                ScenarioSetup.AutonomousSource,
                new MoveShipCommand(
                    ships[0].Id,
                    new NavigationDestination.Position(
                        ScenarioSetup.Position(1, destinationX, 0)),
                    OrderPlacement.ReplaceAll));
            ScenarioSetup.RequireAccepted(command, configuration.Id);
        }

        session.AdvanceTo(new SimulationTime(configuration.GetUInt64(
            BenchmarkParameterNames.SimulatedDurationMilliseconds)));
        return ScenarioResult.Create(session, configuration);
    }
}

internal sealed class FixedTravelTimeEstimator : ILocalTravelTimeEstimator
{
    private readonly SimulationDuration _duration;

    internal FixedTravelTimeEstimator(SimulationDuration duration)
    {
        _duration = duration;
    }

    public SimulationDuration Estimate(
        ShipId actorId,
        SystemPosition origin,
        SystemPosition destination)
    {
        ArgumentOutOfRangeException.ThrowIfZero(actorId.Value);
        if (origin.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "Fixed local travel timing requires positions in the same system.",
                nameof(destination));
        }

        return _duration;
    }
}

internal static class ScenarioSetup
{
    private static readonly CommandSourceId BenchmarkSourceId =
        new("benchmark-autonomous");
    private static readonly PrincipalId BenchmarkPrincipalId = new(1);
    private static readonly ShipDesign BenchmarkShipDesign = new(
        new ConstructionDesignId(1),
        "Benchmark Ship",
        new ConstructionRecipe([], new Work(1)),
        new Quantity(100));

    internal static CommandSource AutonomousSource { get; } =
        new(CommandSourceKind.Autonomous, BenchmarkSourceId);

    internal static RelationshipSetup Relationships { get; } = new(
        [
            new PrincipalDefinition(
                BenchmarkPrincipalId,
                new PrincipalContentId("benchmark"),
                "Benchmark Principal"),
        ],
        BenchmarkPrincipalId,
        new StandingPolicy(
            new StandingPolicyId("benchmark-standing"),
            new StandingValue(-100),
            new StandingValue(100),
            new StandingValue(0),
            new StandingValue(-50),
            new StandingValue(0),
            new StandingValue(50),
            new StandingValue(90)),
        []);

    internal static StarSystem[] CreateSystems(int count)
    {
        var systems = new StarSystem[count];
        for (int index = 0; index < count; index++)
        {
            ulong id = checked((ulong)index + 1);
            systems[index] = new StarSystem(
                new SystemId(id),
                $"Benchmark System {id.ToString(CultureInfo.InvariantCulture)}");
        }

        return systems;
    }

    internal static InitialShipSetup[] CreateShips(
        int count,
        int systemCount)
    {
        var ships = new InitialShipSetup[count];
        var controller = new ActorController(
            ActorControllerKind.Autonomous,
            BenchmarkSourceId);
        for (int index = 0; index < count; index++)
        {
            int systemIndex = index % systemCount + 1;
            long x = index / systemCount;
            ulong id = checked((ulong)index + 1);
            ships[index] = new InitialShipSetup(
                new EntityId(id),
                new ShipId(id),
                new InventoryId(id),
                BenchmarkPrincipalId,
                BenchmarkShipDesign,
                Position(systemIndex, x, 0),
                controller);
        }

        return ships;
    }

    internal static SystemPosition Position(
        int system,
        long x,
        long y) =>
        new(
            new SystemId(checked((ulong)system)),
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));

    internal static void RequireAccepted(
        GameplayCommandRecord command,
        string scenarioId)
    {
        if (command.Result.Status != CommandResultStatus.Accepted)
        {
            throw new InvalidOperationException(
                $"Benchmark scenario '{scenarioId}' command "
                + $"{command.Envelope.Sequence} was rejected: "
                + $"{command.Result.RejectionCode} {command.Result.Reason}");
        }
    }

    internal static IReadOnlyDictionary<string, long> Counts(
        params (string Name, long Value)[] values) =>
        new ReadOnlyDictionary<string, long>(
            values.ToDictionary(
                value => value.Name,
                value => value.Value,
                StringComparer.Ordinal));
}

internal static class ScenarioResult
{
    internal static ScenarioCorrectnessResult Create(
        GameSession session,
        ResolvedBenchmarkScenario configuration)
    {
        GameSnapshot snapshot = session.CaptureSnapshot();
        GameFactReadResult facts = session.ReadFactsAfter(
            null,
            configuration.GetInt32(BenchmarkParameterNames.FactRetentionCapacity));
        var digest = new DeterministicDigest();
        AddSnapshot(digest, snapshot);
        AddCommands(digest, session.CommandRecords);
        AddEvents(digest, session.EventRecords);
        AddFacts(digest, facts);
        long newestFact = facts.NewestCommittedSequence is { } newest
            ? checked((long)newest.Value)
            : 0;
        return new ScenarioCorrectnessResult(
            digest.ToString(),
            session.CurrentTime.Milliseconds,
            ScenarioSetup.Counts(
                ("commands", session.CommandRecords.Count),
                ("events", session.EventRecords.Count),
                ("factsCommitted", newestFact),
                ("factsRetained", facts.Facts.Count),
                ("ships", snapshot.Ships.Count),
                ("systems", snapshot.Systems.Count)));
    }

    private static void AddSnapshot(
        DeterministicDigest digest,
        GameSnapshot snapshot)
    {
        digest.Add(snapshot.Time.Milliseconds);
        foreach (GameSystemSnapshot system in snapshot.Systems.OrderBy(value => value.Id.Value))
        {
            digest.Add(system.Id.Value);
            digest.Add(system.Name);
        }

        foreach (ConnectorEndpointSnapshot endpoint in
            snapshot.ConnectorEndpoints.OrderBy(value => value.Id.Value))
        {
            digest.Add(endpoint.Id.Value);
            AddPosition(digest, endpoint.Position);
        }

        foreach (TransitConnectionSnapshot connection in
            snapshot.TransitConnections.OrderBy(value => value.Id.Value))
        {
            digest.Add(connection.Id.Value);
            digest.Add(connection.SourceEndpointId.Value);
            digest.Add(connection.DestinationEndpointId.Value);
            digest.Add(connection.Duration.Milliseconds);
        }

        foreach (GameShipSnapshot ship in snapshot.Ships.OrderBy(value => value.Id.Value))
        {
            digest.Add(ship.Id.Value);
            digest.Add(ship.SpatialState.GetType().Name);
            switch (ship.SpatialState)
            {
                case ShipSpatialSnapshotState.AtPosition at:
                    AddPosition(digest, at.Position);
                    break;
                case ShipSpatialSnapshotState.LocalMotion motion:
                    AddPosition(digest, motion.CurrentPosition);
                    digest.Add(motion.Motion.Id.Value);
                    digest.Add(motion.Motion.ArrivesAt.Milliseconds);
                    break;
                case ShipSpatialSnapshotState.ConnectorTransit transit:
                    digest.Add(transit.Transit.Id.Value);
                    digest.Add(transit.Transit.ConnectionId.Value);
                    digest.Add(transit.Transit.ArrivesAt.Milliseconds);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported spatial snapshot {ship.SpatialState.GetType().Name}.");
            }

            digest.Add((ulong)ship.Control.ActiveController.Kind);
            digest.Add(ship.Control.ActiveController.Id.Value);
            digest.Add(ship.Control.Revision.Value);
            AddOrder(digest, ship.CurrentOrder);
            foreach (ShipOrderSnapshot queued in ship.QueuedOrders)
            {
                AddOrder(digest, queued);
            }

            foreach (ShipOrderSnapshot suspended in ship.SuspendedOrders)
            {
                AddOrder(digest, suspended);
            }
        }
    }

    private static void AddCommands(
        DeterministicDigest digest,
        IReadOnlyList<GameplayCommandRecord> commands)
    {
        foreach (GameplayCommandRecord command in commands)
        {
            digest.Add(command.Envelope.Sequence.Value);
            digest.Add(command.Envelope.SubmittedAt.Milliseconds);
            digest.Add(command.Envelope.Command.Kind);
            digest.Add((ulong)command.Result.Status);
            digest.Add(command.Result.RejectionCode?.Value ?? string.Empty);
        }
    }

    private static void AddEvents(
        DeterministicDigest digest,
        IReadOnlyList<GameEventRecord> events)
    {
        foreach (GameEventRecord simulationEvent in events)
        {
            digest.Add(simulationEvent.Timestamp.Milliseconds);
            digest.Add((ulong)simulationEvent.Phase);
            digest.Add(simulationEvent.CreationSequence);
            digest.Add(simulationEvent.Generation.Value);
            digest.Add((ulong)simulationEvent.Disposition);
            digest.Add(simulationEvent.Kind.GetType().Name);
        }
    }

    private static void AddFacts(
        DeterministicDigest digest,
        GameFactReadResult result)
    {
        digest.Add(result.CursorGap);
        digest.Add(result.OldestRetainedSequence?.Value ?? 0);
        digest.Add(result.NewestCommittedSequence?.Value ?? 0);
        foreach (GameFactEnvelope fact in result.Facts)
        {
            digest.Add(fact.Sequence.Value);
            digest.Add(fact.Timestamp.Milliseconds);
            digest.Add(fact.Cause.GetType().Name);
            digest.Add(fact.Fact.GetType().Name);
        }
    }

    private static void AddOrder(
        DeterministicDigest digest,
        ShipOrderSnapshot? order)
    {
        if (order is null)
        {
            digest.Add(false);
            return;
        }

        digest.Add(true);
        digest.Add(order.Id.Value);
        digest.Add((ulong)order.Status);
        digest.Add((ulong)order.Reason);
        digest.Add(order.Destination.GetType().Name);
    }

    private static void AddPosition(
        DeterministicDigest digest,
        SystemPosition position)
    {
        digest.Add(position.SystemId.Value);
        digest.Add(position.Position.X.Units);
        digest.Add(position.Position.Y.Units);
    }
}

internal sealed class DeterministicDigest
{
    private const ulong OffsetBasis = 14_695_981_039_346_656_037;
    private const ulong Prime = 1_099_511_628_211;
    private ulong _value = OffsetBasis;

    internal void Add(bool value) =>
        Add(value ? 1UL : 0UL);

    internal void Add(long value) =>
        Add(unchecked((ulong)value));

    internal void Add(ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            AddByte((byte)(value >> shift));
        }
    }

    internal void Add(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        foreach (char character in value)
        {
            AddByte((byte)character);
            AddByte((byte)(character >> 8));
        }

        AddByte(0xff);
    }

    public override string ToString() =>
        _value.ToString("x16", CultureInfo.InvariantCulture);

    private void AddByte(byte value)
    {
        _value ^= value;
        _value *= Prime;
    }
}
