using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PlayerFactProjectorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySpatialFactUsesTheApprovedOwnedOrExternalPayload(bool owned)
    {
        using GameSession session = GameSessionFogOfWarTests.Create(5_000);
        GameSnapshot world = session.CaptureSnapshot();
        PlayerFactProjector projector = Projector(world);
        ShipId ship = new(owned ? 1UL : 2UL);
        EntityId entity = new(ship.Value);
        PrincipalId principal = new(ship.Value);
        SystemPosition position = world.Ships.Single(value => value.Id == ship).Position!.Value;
        var order = new ShipOrderId(1);
        var motion = new LocalMotionSnapshot(new MotionId(1), new EventGeneration(0), position,
            GameSessionTestFixture.Position(100_000, 0), SimulationTime.Zero, new SimulationTime(1000), null);
        var transit = new ConnectorTransitSnapshot(new ConnectorTransitId(1), new EventGeneration(0),
            new TransitConnectionId(1), position, position, SimulationTime.Zero, new SimulationTime(10), null);
        (GameFact Fact, ObservedShipChangeKind? Kind)[] cases =
        [
            (new ShipLocalMotionStartedFact(ship, motion, order), ObservedShipChangeKind.MotionChanged),
            (new ShipLocalMotionEndedFact(ship, motion, position, SimulationTime.Zero, LocalMotionEndReason.ReplacedByCommand, order), ObservedShipChangeKind.MotionChanged),
            (new ShipCruiseEnteredFact(ship, new MotionId(1), position, ShipVelocity.Zero, SimulationTime.Zero, order), ObservedShipChangeKind.MotionChanged),
            (new ShipCruiseDroppedOutFact(ship, new MotionId(1), position, ShipVelocity.Zero, SimulationTime.Zero, order), ObservedShipChangeKind.MotionChanged),
            (new ShipConnectorTransitStartedFact(ship, transit, order), ObservedShipChangeKind.Departed),
            (new ShipConnectorTransitCompletedFact(ship, transit, new SimulationTime(10), order), ObservedShipChangeKind.Arrived),
            (new EntityMaterializedFact(entity, EntityKind.Ship, ship, EntityMaterializationSourceKind.Construction,
                principal, new ConstructionDesignId(1), position), ObservedShipChangeKind.Appeared),
            (new EntityRemovedFact(entity, EntityKind.Ship, ship, EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo), ObservedShipChangeKind.Disappeared),
            (new ShipWaypointArrivedFact(ship, new MotionId(1), GameSessionTestFixture.Position(100_000, 0), position, SimulationTime.Zero, order), null),
            (new ShipOrderTransitionFact(ship, order, GameSessionTestFixture.Player,
                GameSessionTestFixture.Destination(100_000, 0), null, ShipOrderStatus.Active, ShipOrderReason.MovingToDestination), null),
        ];
        foreach (var candidate in cases)
        {
            GameFact? result = projector.Project(candidate.Fact);
            if (owned) Assert.Same(candidate.Fact, result);
            else if (candidate.Kind is null) Assert.Null(result);
            else
            {
                ObservedShipChangeFact notice = Assert.IsType<ObservedShipChangeFact>(result);
                Assert.Equal(ship, notice.ShipId);
                Assert.Equal(position, notice.Position);
                Assert.Equal(candidate.Kind, notice.Kind);
                if (notice.Kind != ObservedShipChangeKind.Appeared)
                {
                    Assert.Null(notice.EntityId);
                    Assert.Null(notice.PrincipalId);
                    Assert.Null(notice.DesignId);
                }
                else
                {
                    Assert.Equal(entity, notice.EntityId);
                    Assert.Equal(principal, notice.PrincipalId);
                    Assert.Equal(new ConstructionDesignId(1), notice.DesignId);
                }
            }
        }
    }

    [Theory]
    [InlineData(CommandSourceKind.Player, "test-player", true)]
    [InlineData(CommandSourceKind.Player, "other-player", false)]
    [InlineData(CommandSourceKind.Autonomous, "test-player", false)]
    [InlineData(CommandSourceKind.Script, "test-player", false)]
    [InlineData(CommandSourceKind.Dialogue, "test-player", false)]
    public void CommandReceiptsRequireTheExactConfiguredSource(CommandSourceKind kind, string id, bool disclosed)
    {
        using GameSession session = GameSessionFogOfWarTests.Create(5_000);
        PlayerFactProjector projector = Projector(session.CaptureSnapshot());
        var source = new CommandSource(kind, new CommandSourceId(id));
        GameFact[] facts = [new CommandAcceptedFact(new CommandSequence(1), source, "test"),
            new CommandRejectedFact(new CommandSequence(2), source, "test", CommandRejectionCodes.InvalidIntent)];
        foreach (GameFact fact in facts)
        {
            GameFact? projected = projector.Project(fact);
            Assert.Equal(disclosed, projected is not null);
            if (disclosed)
            {
                PlayerCommandOutcomeFact notice = Assert.IsType<PlayerCommandOutcomeFact>(projected);
                Assert.Equal("test", notice.CommandKind);
                Assert.Null(notice.GetType().GetProperty("Source"));
                Assert.Equal(fact is CommandAcceptedFact ? CommandResultStatus.Accepted : CommandResultStatus.Rejected, notice.Status);
                Assert.Equal(fact is CommandRejectedFact ? CommandRejectionCodes.InvalidIntent : null, notice.RejectionCode);
            }
        }
    }

    private static PlayerFactProjector Projector(GameSnapshot world) => new(GameSessionTestFixture.Player, world,
        world.Ships.ToDictionary(ship => ship.Id),
        new PlayerCoverageBoundary(world.Time,
            [(PreparedInteractionPoint.Capture(InteractionPhaseCurve.Fixed(GameSessionTestFixture.Position(0, 0),
                world.Time, world.Time), world.Time), 10_000)]));
}
