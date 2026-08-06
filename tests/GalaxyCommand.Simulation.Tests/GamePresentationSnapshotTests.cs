using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GamePresentationSnapshotTests
{
    [Fact]
    public void RequestCanonicalizesSelectedShipOrderingAndResolvesFocusedDetails()
    {
        ShipId secondShip = new(2);
        GameSession session = CreateTwoShipSession();

        GamePresentationSnapshot presentation = session.CapturePresentation(
            new GamePresentationRequest(
                [secondShip, GameSessionTestFixture.Ship],
                secondShip,
                factCursor: null,
                maximumFactCount: 10));

        Assert.Equal(
            [GameSessionTestFixture.Ship, secondShip],
            presentation.Selection.RequestedShipIds);
        Assert.Equal(
            [GameSessionTestFixture.Ship, secondShip],
            presentation.Selection.ResolvedShips.Select(ship => ship.Id));
        Assert.Empty(presentation.Selection.UnresolvedShipIds);
        GameShipSnapshot focused = Assert.IsType<GameShipSnapshot>(
            presentation.Selection.FocusedShip);
        Assert.Equal(secondShip, focused.Id);
        Assert.Equal(GameSessionTestFixture.PlayerController, focused.Control.BaseController);
        Assert.Null(focused.CurrentOrder);
    }

    [Fact]
    public void RequestRejectsDuplicateShipsAndFocusOutsideSelection()
    {
        Assert.Throws<ArgumentException>(() =>
            new GamePresentationRequest(
                [GameSessionTestFixture.Ship, GameSessionTestFixture.Ship],
                GameSessionTestFixture.Ship,
                factCursor: null,
                maximumFactCount: 1));
        Assert.Throws<ArgumentException>(() =>
            new GamePresentationRequest(
                [GameSessionTestFixture.Ship],
                new ShipId(2),
                factCursor: null,
                maximumFactCount: 1));
    }

    [Fact]
    public void MissingSelectedShipRemainsExplicitAndDoesNotResolveFocus()
    {
        ShipId missingShip = new(99);
        GameSession session = GameSessionTestFixture.Create();

        GamePresentationSnapshot presentation = session.CapturePresentation(
            new GamePresentationRequest(
                [missingShip],
                missingShip,
                factCursor: null,
                maximumFactCount: 10));

        Assert.Empty(presentation.Selection.ResolvedShips);
        Assert.Equal([missingShip], presentation.Selection.UnresolvedShipIds);
        Assert.Null(presentation.Selection.FocusedShip);
    }

    [Fact]
    public void SelectedFactProjectionPreservesFactSequenceAcrossShips()
    {
        ShipId secondShip = new(2);
        GameSession session = CreateTwoShipSession();
        SubmitMove(session, GameSessionTestFixture.Ship, 100, 0);
        SubmitMove(session, secondShip, 200, 0);

        GamePresentationSnapshot presentation = session.CapturePresentation(
            new GamePresentationRequest(
                [secondShip, GameSessionTestFixture.Ship],
                GameSessionTestFixture.Ship,
                factCursor: null,
                maximumFactCount: 10));

        Assert.Equal(
            [2UL, 3UL, 5UL, 6UL],
            presentation.SelectedShipFacts.Select(fact => fact.Sequence.Value));
        Assert.Equal(
            [GameSessionTestFixture.Ship, GameSessionTestFixture.Ship, secondShip, secondShip],
            presentation.SelectedShipFacts.Select(GetReferencedShipId));
        Assert.Equal(
            [1UL, 2UL, 3UL, 4UL, 5UL, 6UL],
            presentation.Facts.Facts.Select(fact => fact.Sequence.Value));
    }

    [Fact]
    public void PresentationPropagatesFactLimitAndCursorGap()
    {
        GameSession session = GameSessionTestFixture.Create(
            factRetentionCapacity: 3);
        SubmitMove(session, GameSessionTestFixture.Ship, 100, 0);
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new UnsupportedTestCommand());
        session.SubmitCommand(
            GameSessionTestFixture.Player,
            new UnsupportedTestCommand());

        GamePresentationSnapshot presentation = session.CapturePresentation(
            new GamePresentationRequest(
                Array.Empty<ShipId>(),
                focusedShipId: null,
                factCursor: null,
                maximumFactCount: 2));

        Assert.True(presentation.Facts.CursorGap);
        Assert.Equal(
            [3UL, 4UL],
            presentation.Facts.Facts.Select(fact => fact.Sequence.Value));
        Assert.Empty(presentation.SelectedShipFacts);
    }

    private static GameSession CreateTwoShipSession()
    {
        ShipId secondShip = new(2);
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [
                new InitialShipSetup(
                    GameSessionTestFixture.Entity,
                    GameSessionTestFixture.Ship,
                    GameSessionTestFixture.CargoInventory,
                    GameSessionTestFixture.Organization,
                    GameSessionTestFixture.Design,
                    GameSessionTestFixture.Position(0, 0),
                    GameSessionTestFixture.PlayerController),
                new InitialShipSetup(
                    new EntityId(2),
                    secondShip,
                    new InventoryId(2),
                    GameSessionTestFixture.Organization,
                    GameSessionTestFixture.Design,
                    GameSessionTestFixture.Position(10, 0),
                    GameSessionTestFixture.PlayerController),
            ],
            factRetentionCapacity: 256);
        return new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new GameSessionTestFixture.FixedTravelTimeEstimator()));
    }

    private static void SubmitMove(
        GameSession session,
        ShipId shipId,
        long x,
        long y)
    {
        GameplayCommandRecord record = session.SubmitCommand(
            GameSessionTestFixture.Player,
            new MoveShipCommand(
                shipId,
                GameSessionTestFixture.Destination(x, y),
                OrderPlacement.ReplaceAll));
        Assert.Equal(CommandResultStatus.Accepted, record.Result.Status);
    }

    private static ShipId GetReferencedShipId(GameFactEnvelope envelope) =>
        envelope.Fact switch
        {
            ShipOrderTransitionFact transition => transition.ShipId,
            ShipLocalMotionStartedFact started => started.ShipId,
            _ => throw new InvalidOperationException(
                $"Unexpected selected fact {envelope.Fact.GetType().Name}."),
        };

    private sealed record UnsupportedTestCommand : GameplayCommand
    {
        internal UnsupportedTestCommand()
            : base("test.unsupported-presentation-command")
        {
        }
    }
}
