using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GroupMoveCommandTests
{
    [Fact]
    public void MoveCanonicalizesMembersAndCreatesIndependentFormationOrders()
    {
        GameSession session = CreateFourShipSession();

        GameplayCommandRecord record = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipGroupCommand(
                [new ShipId(4), new ShipId(1), new ShipId(3), new ShipId(2)],
                GameSessionTestFixture.Position(1_000, 2_000)));

        Assert.Equal(CommandResultStatus.Accepted, record.Result.Status);
        Assert.Equal(
            [new ShipId(1), new ShipId(2), new ShipId(3), new ShipId(4)],
            Assert.IsType<MoveShipGroupCommand>(record.Envelope.Command).ShipIds);
        Assert.Equal(
            GameSessionTestFixture.Position(1_100, 2_000),
            Ship(session, 1).Motion?.Destination);
        Assert.Equal(
            GameSessionTestFixture.Position(1_000, 1_900),
            Ship(session, 2).Motion?.Destination);
        Assert.Equal(
            GameSessionTestFixture.Position(900, 2_000),
            Ship(session, 3).Motion?.Destination);
        Assert.Equal(
            GameSessionTestFixture.Position(1_000, 2_100),
            Ship(session, 4).Motion?.Destination);
        Assert.All(
            session.CaptureSnapshot().Ships,
            ship => Assert.Equal(ShipOrderStatus.Active, ship.CurrentOrder?.Status));
    }

    [Fact]
    public void MoveRejectsEveryMemberWhenOneMemberIsNotControlledByTheSource()
    {
        var otherController = new ActorController(
            ActorControllerKind.Player,
            new CommandSourceId("other-player"));
        GameSession session = CreateFourShipSession(otherController);

        GameplayCommandRecord record = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipGroupCommand(
                [new ShipId(1), new ShipId(2), new ShipId(3), new ShipId(4)],
                GameSessionTestFixture.Position(1_000, 2_000)));

        Assert.Equal(CommandResultStatus.Rejected, record.Result.Status);
        Assert.Equal(CommandRejectionCodes.InvalidSource, record.Result.RejectionCode);
        Assert.All(
            session.CaptureSnapshot().Ships,
            ship =>
            {
                Assert.Null(ship.CurrentOrder);
                Assert.Null(ship.Motion);
            });
    }

    [Fact]
    public void CancelCancelsCurrentOrdersAndTreatsIdleMembersAsNoOps()
    {
        GameSession session = CreateFourShipSession();
        foreach (uint id in new uint[] { 1, 3 })
        {
            GameplayCommandRecord move = session.SubmitCommand(
                GameSessionTestFixture.Player,
                new MoveShipCommand(
                    new ShipId(id),
                    GameSessionTestFixture.Destination(id * 100, 0),
                    OrderPlacement.ReplaceAll));
            Assert.Equal(CommandResultStatus.Accepted, move.Result.Status);
        }

        GameplayCommandRecord cancellation = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new CancelShipGroupCommand(
                [new ShipId(4), new ShipId(1), new ShipId(3), new ShipId(2)]));

        Assert.Equal(CommandResultStatus.Accepted, cancellation.Result.Status);
        Assert.Equal(ShipOrderStatus.Cancelled, Ship(session, 1).CurrentOrder?.Status);
        Assert.Equal(ShipOrderStatus.Cancelled, Ship(session, 3).CurrentOrder?.Status);
        Assert.Null(Ship(session, 2).CurrentOrder);
        Assert.Null(Ship(session, 4).CurrentOrder);
        Assert.All(session.CaptureSnapshot().Ships, ship => Assert.Null(ship.Motion));
    }

    [Fact]
    public void CancelRejectsEveryMemberWhenOneMemberIsNotControlledByTheSource()
    {
        var otherController = new ActorController(
            ActorControllerKind.Player,
            new CommandSourceId("other-player"));
        GameSession session = CreateFourShipSession(otherController);
        GameplayCommandRecord move = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                new ShipId(1),
                GameSessionTestFixture.Destination(100, 0),
                OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, move.Result.Status);

        GameplayCommandRecord cancellation = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new CancelShipGroupCommand(
                [new ShipId(1), new ShipId(2), new ShipId(3), new ShipId(4)]));

        Assert.Equal(CommandResultStatus.Rejected, cancellation.Result.Status);
        Assert.Equal(CommandRejectionCodes.InvalidSource, cancellation.Result.RejectionCode);
        Assert.Equal(ShipOrderStatus.Active, Ship(session, 1).CurrentOrder?.Status);
        Assert.NotNull(Ship(session, 1).Motion);
    }

    private static GameSession CreateFourShipSession(
        ActorController? thirdShipController = null)
    {
        InitialShipSetup[] ships = Enumerable.Range(1, 4)
            .Select(id => new InitialShipSetup(
                new EntityId((ulong)id),
                new ShipId((uint)id),
                new InventoryId((uint)id),
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(0, 0),
                id == 3
                    ? thirdShipController ?? GameSessionTestFixture.PlayerController
                    : GameSessionTestFixture.PlayerController))
            .ToArray();
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            ships,
            GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed,
            factRetentionCapacity: 256);

        return new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new GameSessionTestFixture.FixedTravelTimeEstimator()));
    }

    private static GameShipSnapshot Ship(GameSession session, uint id) =>
        Assert.Single(session.CaptureSnapshot().Ships, ship => ship.Id == new ShipId(id));
}
