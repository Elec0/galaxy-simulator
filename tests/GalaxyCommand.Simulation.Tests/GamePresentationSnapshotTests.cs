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
                GameSessionTestFixture.Principal,
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
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GamePresentationRequest(
                default,
                [],
                focusedShipId: null,
                factCursor: null,
                maximumFactCount: 1));
        Assert.Throws<ArgumentException>(() =>
            new GamePresentationRequest(
                GameSessionTestFixture.Principal,
                [GameSessionTestFixture.Ship, GameSessionTestFixture.Ship],
                GameSessionTestFixture.Ship,
                factCursor: null,
                maximumFactCount: 1));
        Assert.Throws<ArgumentException>(() =>
            new GamePresentationRequest(
                GameSessionTestFixture.Principal,
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
                GameSessionTestFixture.Principal,
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
                GameSessionTestFixture.Principal,
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
        Assert.Equal(new GameFactSequence(6), presentation.NextFactCursor);
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
                GameSessionTestFixture.Principal,
                Array.Empty<ShipId>(),
                focusedShipId: null,
                factCursor: null,
                maximumFactCount: 2));

        Assert.True(presentation.Facts.CursorGap);
        Assert.Equal(
            [3UL, 4UL],
            presentation.Facts.Facts.Select(fact => fact.Sequence.Value));
        Assert.Empty(presentation.SelectedShipFacts);
        Assert.Equal(new GameFactSequence(4), presentation.NextFactCursor);
    }

    [Fact]
    public void RelationshipPresentationExposesOnlyObserverScopedPrivateState()
    {
        PrincipalId observer = GameSessionTestFixture.Principal;
        PrincipalId regional = new(2);
        PrincipalId remote = new(3);
        GameSession session = CreateRelationshipPresentationSession(
            observer,
            regional,
            remote);
        session.CommitStandingChanges(new StandingChangeBatch(
            new StandingChangeBatchId(StandingChangeSourceKind.Explicit, 100),
            [
                new StandingChangeProposal(
                    remote,
                    observer,
                    new StandingChangeContribution(
                        new StandingChangeContributionId(1),
                        -40,
                        StandingChangeReason.Explicit)),
            ]));
        session.CommitRelationshipPolicyChanges(new RelationshipPolicyChangeBatch(
            new RelationshipPolicyChangeBatchId(
                RelationshipPolicyChangeSourceKind.Explicit,
                101),
            [
                new RevokeRelationshipGrantProposal(
                    new RelationshipGrantId(3),
                    RelationshipPolicyChangeReason.Explicit),
            ]));
        session.CommitRelationshipPolicyChanges(new RelationshipPolicyChangeBatch(
            new RelationshipPolicyChangeBatchId(
                RelationshipPolicyChangeSourceKind.Explicit,
                100),
            [
                new RevokeRelationshipGrantProposal(
                    new RelationshipGrantId(5),
                    RelationshipPolicyChangeReason.Explicit),
            ]));
        session.CommitStandingChanges(new StandingChangeBatch(
            new StandingChangeBatchId(StandingChangeSourceKind.Explicit, 101),
            [
                new StandingChangeProposal(
                    observer,
                    regional,
                    new StandingChangeContribution(
                        new StandingChangeContributionId(1),
                        -5,
                        StandingChangeReason.Explicit)),
            ]));

        GamePresentationSnapshot presentation = session.CapturePresentation(
            new GamePresentationRequest(
                observer,
                [],
                focusedShipId: null,
                factCursor: null,
                maximumFactCount: 10));

        RelationshipPresentationSnapshot relationships = presentation.Relationships;
        Assert.Equal(observer, relationships.ObserverPrincipalId);
        Assert.Equal(
            [(observer, "Observer"), (regional, "Regional"), (remote, "Remote")],
            relationships.Principals.Select(principal => (principal.Id, principal.Name)));
        Assert.Equal(
            [
                (observer, regional, DiplomaticCondition.Peace),
                (observer, remote, DiplomaticCondition.War),
                (regional, remote, DiplomaticCondition.War),
            ],
            relationships.DiplomaticConditions.Select(value => (
                value.LowerPrincipalId,
                value.UpperPrincipalId,
                value.Condition)));
        Assert.Equal(
            [
                (regional, new StandingValue(-60), StandingBand.Hostile),
                (remote, new StandingValue(55), StandingBand.Favorable),
            ],
            relationships.IncomingStandings.Select(value => (
                value.AssessingPrincipalId,
                value.Value,
                value.Band)));
        Assert.Equal(
            [(1UL, true), (2UL, false)],
            relationships.GrantsIssuedToObserver.Select(grant => (
                grant.Id.Value,
                grant.IsEffective)));
        Assert.Equal(
            [1UL, 3UL],
            presentation.Facts.Facts.Select(fact => fact.Sequence.Value));
        Assert.Equal(new GameFactSequence(4), presentation.NextFactCursor);
        StandingChangedFact visibleStanding = Assert.IsType<StandingChangedFact>(
            presentation.Facts.Facts[0].Fact);
        Assert.Equal(observer, visibleStanding.SubjectPrincipalId);
        Assert.Null(typeof(GamePresentationWorldSnapshot).GetProperty("Relationships"));

        GamePresentationSnapshot continued = session.CapturePresentation(
            new GamePresentationRequest(
                observer,
                [],
                focusedShipId: null,
                factCursor: presentation.NextFactCursor,
                maximumFactCount: 10));
        Assert.Empty(continued.Facts.Facts);
        Assert.Equal(presentation.NextFactCursor, continued.NextFactCursor);
    }

    [Fact]
    public void RelationshipPresentationRejectsUnknownObserverAndIsImmutable()
    {
        GameSession session = GameSessionTestFixture.Create();

        Assert.Throws<ArgumentException>(() => session.CapturePresentation(
            new GamePresentationRequest(
                new PrincipalId(99),
                [],
                focusedShipId: null,
                factCursor: null,
                maximumFactCount: 10)));

        RelationshipPresentationSnapshot relationships = session.CapturePresentation(
            new GamePresentationRequest(
                GameSessionTestFixture.Principal,
                [],
                focusedShipId: null,
                factCursor: null,
                maximumFactCount: 10)).Relationships;
        var principals = Assert.IsAssignableFrom<
            IList<RelationshipPrincipalPresentationSnapshot>>(relationships.Principals);
        var diplomacy = Assert.IsAssignableFrom<IList<DiplomaticConditionSnapshot>>(
            relationships.DiplomaticConditions);
        var standings = Assert.IsAssignableFrom<IList<IncomingStandingPresentationSnapshot>>(
            relationships.IncomingStandings);
        var grants = Assert.IsAssignableFrom<IList<RelationshipGrantSnapshot>>(
            relationships.GrantsIssuedToObserver);

        Assert.Throws<NotSupportedException>(() => principals.Clear());
        Assert.Throws<NotSupportedException>(() => diplomacy.Clear());
        Assert.Throws<NotSupportedException>(() => standings.Clear());
        Assert.Throws<NotSupportedException>(() => grants.Clear());
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
                    GameSessionTestFixture.Principal,
                    GameSessionTestFixture.Design,
                    GameSessionTestFixture.Position(0, 0),
                    GameSessionTestFixture.PlayerController),
                new InitialShipSetup(
                    new EntityId(2),
                    secondShip,
                    new InventoryId(2),
                    GameSessionTestFixture.Principal,
                    GameSessionTestFixture.Design,
                    GameSessionTestFixture.Position(10, 0),
                    GameSessionTestFixture.PlayerController),
            ],
            GameSessionTestFixture.Relationships,
            factRetentionCapacity: 256);
        return new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new GameSessionTestFixture.FixedTravelTimeEstimator()));
    }

    private static GameSession CreateRelationshipPresentationSession(
        PrincipalId observer,
        PrincipalId regional,
        PrincipalId remote)
    {
        var relationships = new RelationshipSetup(
            [
                new PrincipalDefinition(observer, new PrincipalContentId("observer"), "Observer"),
                new PrincipalDefinition(regional, new PrincipalContentId("regional"), "Regional"),
                new PrincipalDefinition(remote, new PrincipalContentId("remote"), "Remote"),
            ],
            observer,
            GameSessionTestFixture.StandingPolicy,
            [
                new InitialStandingSetup(regional, observer, new StandingValue(-60)),
                new InitialStandingSetup(observer, regional, new StandingValue(95)),
                new InitialStandingSetup(remote, observer, new StandingValue(95)),
                new InitialStandingSetup(observer, remote, new StandingValue(-80)),
            ],
            [
                new InitialDiplomaticConditionSetup(
                    observer,
                    remote,
                    DiplomaticCondition.War),
                new InitialDiplomaticConditionSetup(
                    regional,
                    remote,
                    DiplomaticCondition.War),
            ],
            [
                Grant(1, regional, observer, StandingBand.Hostile),
                Grant(2, remote, observer, StandingBand.Allied),
                Grant(3, observer, regional, StandingBand.Allied),
                Grant(4, regional, remote, StandingBand.Neutral),
                Grant(5, regional, observer, StandingBand.Hostile),
            ]);
        return new GameSession(
            new GameSessionSetup(
                [new StarSystem(GameSessionTestFixture.System, "Test System")],
                [],
                relationships,
                factRetentionCapacity: 256),
            new DirectLocalNavigationPlanner(
                new GameSessionTestFixture.FixedTravelTimeEstimator()));
    }

    private static InitialRelationshipGrantSetup Grant(
        ulong id,
        PrincipalId issuer,
        PrincipalId holder,
        StandingBand minimumStandingBand) =>
        new(
            new RelationshipGrantId(id),
            issuer,
            holder,
            new RelationshipGrantKind("test-access"),
            minimumStandingBand);

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
