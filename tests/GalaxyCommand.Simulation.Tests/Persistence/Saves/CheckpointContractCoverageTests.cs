using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CheckpointContractCoverageTests
{
    [Fact]
    public void StationaryForecastRoundTripDoesNotInventTargetShip()
    {
        var movement = new SpatialMovement();
        var position = new SystemPosition(new SystemId(1), new SpatialPosition(new SpatialCoordinate(0), new SpatialCoordinate(0)));
        movement.Add(new ShipId(1), position);
        var interest = new StationaryInteractionInterest(new InteractionInterestId(1), "cargo", "collect",
            new ShipId(1), position, 5, new EventGeneration(1), new EntityId(2), new SpatialRevision(3));
        InteractionForecast forecast = Assert.IsType<InteractionForecast>(InteractionForecast.Evaluate(interest,
            movement.CaptureInteractionReadView(SimulationTime.Zero), SimulationTime.Zero));
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);

        InteractionForecast restored = codec.Decode<InteractionForecast>(codec.Encode(forecast));

        Assert.Equal(forecast.Candidate, restored.Candidate);
        Assert.Equal(new SpatialRevision(3), restored.TargetRevision);
        Assert.Throws<InvalidOperationException>(() => restored.Target);
        Assert.Equal(forecast.Source.Spatial, restored.Source.Spatial);
        Assert.Equal(forecast.Source.Revision, restored.Source.Revision);
        Assert.Equal(codec.Encode(forecast).GetRawText(), codec.Encode(restored).GetRawText());
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(2_000UL)]
    public void DecelerationTargetRetainsExactBoundaryState(ulong targetSpeed)
    {
        var position = new SystemPosition(new SystemId(1), new SpatialPosition(new SpatialCoordinate(0), new SpatialCoordinate(0)));
        var start = new ShipKinematicState(position, new ShipVelocity(0, 10_000), new ShipHeading(123_000));
        var segment = new DecelerationManeuverSegment(ManeuverDecelerationKind.ActiveBrake, new SimulationTime(7),
            start, new ManeuverAcceleration(1_000), targetSpeed == 0 ? default : new ManeuverSpeed(targetSpeed));
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);

        DecelerationManeuverSegment restored = codec.Decode<DecelerationManeuverSegment>(codec.Encode(segment));

        Assert.Equal(segment.TargetSpeedMillimetersPerSecond, restored.TargetSpeedMillimetersPerSecond);
        Assert.Equal(segment.EndsAt, restored.EndsAt);
        Assert.Equal(segment.StateAt(segment.StartsAt), restored.StateAt(segment.StartsAt));
        Assert.Equal(segment.StateAt(new SimulationTime(8)), restored.StateAt(new SimulationTime(8)));
        Assert.Equal(segment.StateAt(segment.EndsAt), restored.StateAt(segment.EndsAt));
    }

    [Fact]
    public void NullManeuverPhaseRejectsAtItsSchemaPathBeforeEvaluatorConstruction()
    {
        var state = new ShipKinematicState(
            new SystemPosition(new SystemId(1), new SpatialPosition(new SpatialCoordinate(0), new SpatialCoordinate(0))),
            ShipVelocity.Zero, ShipHeading.Zero);
        ExecutableBoundedTerminalManeuverPlan plan = ExecutableBoundedTerminalManeuverPlan.TerminalSettle(SimulationTime.Zero, state);
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);
        JsonNode encoded = JsonNode.Parse(codec.Encode(plan).GetRawText())!;
        encoded["phases"]![0] = null;

        CheckpointJsonException failure = Assert.Throws<CheckpointJsonException>(() =>
            codec.Decode<ExecutableBoundedTerminalManeuverPlan>(JsonSerializer.SerializeToElement(encoded)));

        Assert.Equal("$.phases[0]", failure.Path);
    }

    [Fact]
    public void NullWaypointTransitionRejectsAtItsSchemaPathBeforeEvaluatorConstruction()
    {
        var state = new ShipKinematicState(
            new SystemPosition(new SystemId(1), new SpatialPosition(new SpatialCoordinate(0), new SpatialCoordinate(0))),
            ShipVelocity.Zero, ShipHeading.Zero);
        var transition = new AnalyticManeuverSegment(SimulationTime.Zero, new SimulationTime(10),
            state, ShipAcceleration.Zero, ShipAngularRate.Zero);
        ExecutableBoundedTerminalManeuverPlan terminal = ExecutableBoundedTerminalManeuverPlan.TerminalSettle(new SimulationTime(10), state);
        ExecutableBoundedTerminalManeuverPlan plan = ExecutableBoundedTerminalManeuverPlan.WaypointRoute([transition], terminal);
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);
        JsonNode encoded = JsonNode.Parse(codec.Encode(plan).GetRawText())!;
        encoded["payload"]!["transitions"]![0] = null;

        CheckpointJsonException failure = Assert.Throws<CheckpointJsonException>(() =>
            codec.Decode<ExecutableBoundedTerminalManeuverPlan>(JsonSerializer.SerializeToElement(encoded)));

        Assert.Equal("$.payload.transitions[0]", failure.Path);
    }

    [Fact]
    public void DeclaredNullableOwnerCollectionElementsRemainInertUntilOwnerAdmission()
    {
        var checkpoint = new WorldTopologyCheckpoint([null], [], []);
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);

        WorldTopologyCheckpoint restored = codec.Decode<WorldTopologyCheckpoint>(codec.Encode(checkpoint));

        Assert.Null(Assert.Single(restored.Systems));
    }

    [Fact]
    public void ReachableOwnerGraphHasOnlyExplicitlyRegisteredContracts()
    {
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);
        Dictionary<Type, PropertyInfo[]> objects = Objects(codec);
        IDictionary scalars = Registry(codec, "_scalars");
        IDictionary enums = Registry(codec, "_enums");
        IDictionary unions = Registry(codec, "_unions");
        var visited = new HashSet<Type>();

        Visit(typeof(GameSessionCheckpoint));
        Visit(typeof(ExecutableManeuverCheckpoint));

        // This audit starts from owner authority and follows frozen fields rather than scanning unrelated runtime classes.
        void Visit(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!visited.Add(type) || IsBuiltIn(type)) return;
            if (type == typeof(ExecutableBoundedTerminalManeuverPlan))
            {
                Visit(typeof(ExecutableManeuverCheckpoint));
                return;
            }

            if (type.IsArray) { Visit(type.GetElementType()!); return; }
            Type? dictionary = type.GetInterfaces().Append(type).FirstOrDefault(candidate =>
                candidate.IsGenericType && (candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                    || candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)));
            if (dictionary is not null)
            {
                foreach (Type argument in dictionary.GetGenericArguments()) Visit(argument);
                return;
            }

            Type? sequence = type.GetInterfaces().Append(type).FirstOrDefault(candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (sequence is not null) { Visit(sequence.GetGenericArguments()[0]); return; }
            if (scalars.Contains(type)) { Visit(((PropertyInfo)scalars[type]!).PropertyType); return; }
            if (enums.Contains(type)) return;
            if (unions.Contains(type))
            {
                foreach (var entry in ((string Token, Type Type)[])unions[type]!) Visit(entry.Type);
                return;
            }

            Assert.True(objects.TryGetValue(type, out PropertyInfo[]? properties), $"Missing wire contract: {type}");
            foreach (PropertyInfo property in properties!) Visit(property.PropertyType);
        }
    }

    [Fact]
    public void StoredOwnerPropertiesAreEncodedOrExplicitlyReconstructed()
    {
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);
        foreach ((Type type, PropertyInfo[] stored) in Objects(codec))
        {
            foreach (PropertyInfo property in type.GetProperties(Members))
            {
                FieldInfo? backing = property.DeclaringType!.GetField($"<{property.Name}>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (backing is null || stored.Any(member => member.Name == property.Name)) continue;
                Assert.True(IsReconstructed(type, property), $"Unaccounted retained field: {type}.{property.Name}");
            }
        }
    }

    [Fact]
    public void ClosedUnionsAndEnumsRetainEveryDeclaredVariantWithUnambiguousTokens()
    {
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);
        foreach (DictionaryEntry entry in Registry(codec, "_unions"))
        {
            var parent = (Type)entry.Key;
            var cases = ((string Token, Type Type)[])entry.Value!;
            Type[] declared = parent.Assembly.GetTypes().Where(type => !type.IsAbstract && parent.IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
            Assert.Equal(declared, cases.Select(value => value.Type).OrderBy(type => type.FullName, StringComparer.Ordinal));
            AssertTokens(cases.Select(value => value.Token));
        }

        foreach (DictionaryEntry entry in Registry(codec, "_enums"))
        {
            var type = (Type)entry.Key;
            var values = ((string Token, object Value)[])entry.Value!;
            Assert.Equal(Enum.GetValues(type).Cast<object>(), values.Select(value => value.Value));
            AssertTokens(values.Select(value => value.Token));
        }
    }

    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static IDictionary Registry(CheckpointValueCodec codec, string name) =>
        (IDictionary)typeof(CheckpointValueCodec).GetField(name, Members)!.GetValue(codec)!;

    private static Dictionary<Type, PropertyInfo[]> Objects(CheckpointValueCodec codec)
    {
        IDictionary contracts = Registry(codec, "_objects");
        return contracts.Keys.Cast<Type>().ToDictionary(type => type,
            type => (PropertyInfo[])contracts[type]!.GetType().GetProperty("Properties")!.GetValue(contracts[type])!);
    }

    /// <summary>Matches only the primitive and content factory types actually admitted by the production value codec.</summary>
    private static bool IsBuiltIn(Type type) =>
        type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong) || type == typeof(short) || type == typeof(ushort)
        || type == typeof(byte) || type == typeof(sbyte) || type == typeof(UInt128) || type == typeof(Int128)
        || type == typeof(BigInteger) || type == typeof(QualifiedContentKey) || type == typeof(PackageId)
        || type == typeof(ContentKind) || type == typeof(LocalContentId);

    /// <summary>Documents derived aliases and constructor-computed values so new retained fields cannot silently disappear.</summary>
    private static bool IsReconstructed(Type type, PropertyInfo property) =>
        // The closed command token selects each constructor's fixed public command Kind.
        property.DeclaringType == typeof(GameplayCommand) && property.Name == "Kind"
        // Cargo subtype constructors initialize the shared definition alias from their sole Key field.
        || property.DeclaringType == typeof(CargoDispositionSelection) && property.Name == "DefinitionKey"
        // Exact endpoints and initial speed are computed from the retained start, rate, and target speed.
        || type == typeof(DecelerationManeuverSegment) && property.Name is "EndsAt" or "InitialSpeedMillimetersPerSecond"
        // Candidate identity and plan duration are deterministic reductions of their fully retained constructor inputs.
        || type == typeof(InteractionCandidate) && property.Name == "Key"
        || type == typeof(TravelPlan) && property.Name == "TotalDuration"
        // Object collection uses its exact observed object as its target; maneuver generation comes from its event.
        || type == typeof(ShipActionIntent.CollectObject) && property.Name == "LastObservedObjectEntityId"
        || type == typeof(SpatialMovementEvent.Maneuver) && property.Name == "Generation"
        // Unclaimed custody deliberately has no controlling principal.
        || type == typeof(InventoryCustody.Unclaimed) && property.Name == "ControllingPrincipalId";

    /// <summary>Ensures the fixed vocabulary is lowercase ASCII and has no ambiguous duplicate token.</summary>
    private static void AssertTokens(IEnumerable<string> values)
    {
        string[] tokens = values.ToArray();
        Assert.Equal(tokens.Length, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, token => Assert.Matches("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", token));
    }

}
