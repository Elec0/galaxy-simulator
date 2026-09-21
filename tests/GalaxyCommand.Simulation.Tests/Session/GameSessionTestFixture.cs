using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

internal static class GameSessionTestFixture
{
    internal static SystemId System { get; } = new(1);

    internal static ShipId Ship { get; } = new(1);

    internal static EntityId Entity { get; } = new(1);

    internal static InventoryId CargoInventory { get; } = new(1);

    internal static PrincipalId Principal { get; } = new(1);

    internal static RandomRootSeed RootSeed { get; } =
        RandomRootSeed.FromBytes(new byte[RandomRootSeed.ByteCount]);

    internal static StandingPolicy StandingPolicy { get; } = new(
        new StandingPolicyId("test-standing"),
        new StandingValue(-100),
        new StandingValue(100),
        new StandingValue(0),
        new StandingValue(-50),
        new StandingValue(0),
        new StandingValue(50),
        new StandingValue(90));

    internal static RelationshipSetup Relationships { get; } = new(
        [new PrincipalDefinition(Principal, new PrincipalContentId("test-player"), "Test Player")],
        Principal,
        StandingPolicy,
        []);

    internal static ShipManeuverCapability ManeuverCapability { get; } = new(
        baseMassKilograms: 10_000,
        ManeuverAcceleration.ParseMetersPerSecondSquared("10"),
        customPassiveDeceleration: null,
        ManeuverSpeed.ParseMetersPerSecond("300"),
        ManeuverSpeed.ParseMetersPerSecond("1000"),
        ManeuverTurnRate.ParseDegreesPerSecond("45"),
        new SimulationDuration(10_000));

    internal static ShipDesign Design { get; } = new(
        new ConstructionDesignId(1),
        "Test Ship",
        new ConstructionRecipe([], new Work(1)),
        new Quantity(10),
        ManeuverCapability);

    internal static CommandSource Player { get; } = new(
        CommandSourceKind.Player,
        new CommandSourceId("test-player"));

    internal static ActorController PlayerController { get; } = new(
        ActorControllerKind.Player,
        Player.Id);

    internal static GameSession Create(
        ActorController? baseController = null,
        ISpatialNavigationPlanner? navigation = null,
        int factRetentionCapacity = 256,
        RandomRootSeed? randomRootSeed = null,
        ShipHeading? heading = null)
    {
        var setup = new GameSessionSetup(
            [new StarSystem(System, "Test System")],
            [
                new InitialShipSetup(
                    Entity,
                    Ship,
                    CargoInventory,
                    Principal,
                    Design,
                    Position(0, 0),
                    baseController ?? PlayerController,
                    heading),
            ],
            Relationships,
            randomRootSeed ?? RootSeed,
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

    /// <summary>
    /// Drains exactly the movement active at entry, including every internal
    /// analytic boundary, without consuming a subsequently started leg.
    /// </summary>
    internal static void AdvanceCurrentMovement(
        GameSession session,
        ShipId? shipId = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ShipId selected = shipId ?? Ship;
        GameShipSnapshot ship = session.CaptureSnapshot().Ships.Single(
            candidate => candidate.Id == selected);
        switch (ship.SpatialState)
        {
            case ShipSpatialSnapshotState.AnalyticManeuver analytic:
                MotionId motionId = analytic.Maneuver.MotionId;
                int remainingBoundaryLimit = 32;
                while (session.CaptureSnapshot().Ships.Single(
                           candidate => candidate.Id == selected).Maneuver
                       is { MotionId: var currentMotionId } maneuver
                       && currentMotionId == motionId)
                {
                    if (remainingBoundaryLimit-- == 0)
                    {
                        throw new InvalidOperationException(
                            $"Maneuver {motionId} exceeded the test boundary limit.");
                    }

                    session.AdvanceTo(maneuver.NextBoundary!.Timestamp);
                }

                break;
            case ShipSpatialSnapshotState.LocalMotion local:
                session.AdvanceTo(local.Motion.ArrivesAt);
                break;
            case ShipSpatialSnapshotState.ConnectorTransit transit:
                session.AdvanceTo(transit.Transit.ArrivesAt);
                break;
            default:
                throw new InvalidOperationException(
                    $"Ship {selected} has no active movement.");
        }
    }

    /// <summary>
    /// Advances bounded movement legs until the selected ship's current order
    /// reaches a terminal status, with a guard against accidental cycles.
    /// </summary>
    internal static void AdvanceUntilOrderTerminal(
        GameSession session,
        ShipId? shipId = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ShipId selected = shipId ?? Ship;
        int remainingMovementLimit = 32;
        while (session.CaptureSnapshot().Ships.Single(
                   candidate => candidate.Id == selected).CurrentOrder
               is { Status: ShipOrderStatus.Active or ShipOrderStatus.Waiting })
        {
            if (remainingMovementLimit-- == 0)
            {
                throw new InvalidOperationException(
                    $"Ship {selected} exceeded the test movement limit.");
            }

            AdvanceCurrentMovement(session, selected);
        }
    }

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
