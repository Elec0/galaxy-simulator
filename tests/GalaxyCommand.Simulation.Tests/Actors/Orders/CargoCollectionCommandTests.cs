using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoCollectionCommandTests
{
    private static readonly QualifiedContentKey Ore = QualifiedContentKey.Create("test.commands", "commodity", "ore");
    private static readonly QualifiedContentKey Ice = QualifiedContentKey.Create("test.commands", "commodity", "ice");

    [Fact]
    public void SharedActionIntentDoesNotRequireASensorDefinition()
    {
        Assert.Null(typeof(ShipActionIntent).GetProperty("DefinitionKey"));
    }

    [Fact]
    public void SensorActionsRetainTheirDefinitionOnTheSensorSubtype()
    {
        Type sensor = Assert.IsAssignableFrom<Type>(typeof(ShipActionIntent).GetNestedType("Sensor"));
        Assert.True(sensor.IsAssignableFrom(typeof(ShipActionIntent.DeploySensor)));
        Assert.True(sensor.IsAssignableFrom(typeof(ShipActionIntent.PickupSensor)));
        Assert.NotNull(sensor.GetProperty("DefinitionKey"));
    }

    [Fact]
    public void ObjectAndFieldCommandsCaptureTheirExactTargetsAndPlacement()
    {
        var objectCommand = new CollectCargoObjectCommand(new(1), new(9), OrderPlacement.Append);
        Assert.Equal(new ShipId(1), objectCommand.ShipId);
        Assert.Equal(new EntityId(9), objectCommand.TargetEntityId);
        Assert.Equal(OrderPlacement.Append, objectCommand.Placement);
        var priorities = new[] { Ore, Ice };
        var fieldCommand = new CollectCargoFieldCommand(new(1), new(7), priorities, OrderPlacement.ReplaceAll);
        priorities[0] = Ice;
        Assert.Equal(new DebrisFieldId(7), fieldCommand.TargetFieldId);
        Assert.Equal(new[] { Ore, Ice }, fieldCommand.Priorities);
        Assert.Equal(OrderPlacement.ReplaceAll, fieldCommand.Placement);
        Assert.Empty(new CollectCargoFieldCommand(new(1), new(7), [], OrderPlacement.Append).Priorities);
    }

    [Fact]
    public void CargoIntentsRetainOriginalCommandInventoryAndObservedAnchorWithoutDefinitionSentinels()
    {
        SystemPosition anchor = GameSessionTestFixture.Position(40, -10);
        ShipActionIntent.Cargo action = new ShipActionIntent.CollectObject(new(3), new(2), new(9), anchor);
        Assert.Equal(new CommandSequence(3), action.OriginCommandSequence);
        Assert.Equal(new InventoryId(2), action.CargoInventoryId);
        Assert.Equal(new EntityId(9), action.LastObservedObjectEntityId);
        Assert.Equal(anchor, action.LastObservedPosition);
        Assert.Equal(ShipOrderKind.CollectCargoObject, action.Kind);
        Assert.Equal(new EntityId(9), Assert.IsType<ShipActionIntent.CollectObject>(action).TargetEntityId);

        var priorities = new[] { Ore, Ice };
        var field = new ShipActionIntent.CollectField(new(3), new(2), new(7), new(9), anchor, priorities);
        priorities[0] = Ice;
        Assert.Equal(new DebrisFieldId(7), field.TargetFieldId);
        Assert.Equal(new EntityId(9), field.LastObservedObjectEntityId);
        Assert.Equal(anchor, field.LastObservedPosition);
        Assert.Equal(new[] { Ore, Ice }, field.Priorities);
        Assert.Equal(ShipOrderKind.CollectCargoField, field.Kind);
    }

    [Fact]
    public void CommandsRejectInvalidIdentitiesPlacementAndPriorityStructureWithoutContentLookups()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectCargoObjectCommand(default, new(9), OrderPlacement.Append));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectCargoObjectCommand(new(1), default, OrderPlacement.Append));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectCargoObjectCommand(new(1), new(9), (OrderPlacement)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectCargoFieldCommand(new(1), default, [], OrderPlacement.Append));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectCargoFieldCommand(default, new(7), [], OrderPlacement.Append));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectCargoFieldCommand(new(1), new(7), [], (OrderPlacement)99));
        Assert.Throws<ArgumentException>(() => new CollectCargoFieldCommand(new(1), new(7), [Ore, Ore], OrderPlacement.Append));
        Assert.Throws<ArgumentException>(() => new CollectCargoFieldCommand(new(1), new(7), [null!], OrderPlacement.Append));
        Assert.Throws<ArgumentNullException>(() => new CollectCargoFieldCommand(new(1), new(7), null!, OrderPlacement.Append));
    }

    [Fact]
    public void IntentsRejectInvalidObservedAnchorsAndDuplicatePriorities()
    {
        SystemPosition anchor = GameSessionTestFixture.Position(40, -10);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipActionIntent.CollectObject(default, new(2), new(9), anchor));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipActionIntent.CollectObject(new(3), default, new(9), anchor));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipActionIntent.CollectObject(new(3), new(2), default, anchor));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipActionIntent.CollectObject(new(3), new(2), new(9), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipActionIntent.CollectObject(new(3), new(2), new(9), GameSessionTestFixture.Position(long.MaxValue, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipActionIntent.CollectField(new(3), new(2), default, new(9), anchor, []));
        Assert.Throws<ArgumentException>(() => new ShipActionIntent.CollectField(new(3), new(2), new(7), new(9), anchor, [Ore, Ore]));
    }
}
