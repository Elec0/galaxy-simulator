using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

internal static class GameSessionTestFixture
{
    internal static SystemId System { get; } = new(1);

    internal static ShipId Ship { get; } = new(1);

    internal static EntityId Entity { get; } = new(1);

    internal static InventoryId CargoInventory { get; } = new(1);

    internal static OrganizationId Organization { get; } = new(1);

    internal static ShipDesign Design { get; } = new(
        new ConstructionDesignId(1),
        "Test Ship",
        new ConstructionRecipe([], new Work(1)),
        new Quantity(10));

    internal static CommandSource Player { get; } = new(
        CommandSourceKind.Player,
        new CommandSourceId("test-player"));

    internal static ActorController PlayerController { get; } = new(
        ActorControllerKind.Player,
        Player.Id);

    internal static GameSession Create(
        ActorController? baseController = null,
        ISpatialNavigationPlanner? navigation = null,
        int factRetentionCapacity = 256)
    {
        var setup = new GameSessionSetup(
            [new StarSystem(System, "Test System")],
            [
                new InitialShipSetup(
                    Entity,
                    Ship,
                    CargoInventory,
                    Organization,
                    Design,
                    Position(0, 0),
                    baseController ?? PlayerController),
            ],
            factRetentionCapacity);
        return new GameSession(
            setup,
            navigation
                ?? new DirectLocalNavigationPlanner(new FixedTravelTimeEstimator()));
    }

    internal static NavigationDestination Destination(long x, long y) =>
        new NavigationDestination.Position(Position(x, y));

    internal static SystemPosition Position(long x, long y) =>
        new(
            System,
            new SpatialPosition(
                new SpatialCoordinate(x),
                new SpatialCoordinate(y)));

    internal sealed class FixedTravelTimeEstimator : ILocalTravelTimeEstimator
    {
        public SimulationDuration Estimate(
            ShipId actorId,
            SystemPosition origin,
            SystemPosition destination) =>
            origin == destination
                ? SimulationDuration.Zero
                : new SimulationDuration(100);
    }
}
