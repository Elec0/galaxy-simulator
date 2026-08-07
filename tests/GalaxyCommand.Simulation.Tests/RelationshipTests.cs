using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class RelationshipTests
{
    private static readonly PrincipalId PlayerPrincipal = new(1);
    private static readonly PrincipalId RegionalPrincipal = new(2);

    [Theory]
    [InlineData(-100, StandingBand.Hostile)]
    [InlineData(-50, StandingBand.Adversarial)]
    [InlineData(0, StandingBand.Neutral)]
    [InlineData(50, StandingBand.Favorable)]
    [InlineData(90, StandingBand.Allied)]
    [InlineData(100, StandingBand.Allied)]
    public void StandingPolicyMapsExactValuesToAcceptedBands(
        long value,
        StandingBand expected)
    {
        Assert.Equal(expected, Policy().GetBand(new StandingValue(value)));
    }

    [Fact]
    public void StandingPolicyRejectsInvalidBoundsAndOutOfRangeValues()
    {
        Assert.Throws<ArgumentException>(() => new StandingPolicy(
            new StandingPolicyId("invalid"),
            new StandingValue(-100),
            new StandingValue(100),
            new StandingValue(0),
            new StandingValue(-50),
            new StandingValue(0),
            new StandingValue(90),
            new StandingValue(50)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Policy().GetBand(new StandingValue(101)));
    }

    [Fact]
    public void RelationshipSetupRejectsPrincipalIdentityCollisionsAndBlankNames()
    {
        Assert.Throws<ArgumentException>(() =>
            new PrincipalDefinition(
                PlayerPrincipal,
                new PrincipalContentId("player"),
                " "));
        Assert.Throws<ArgumentException>(() => CreateRelationshipSetup(
            [
                Definition(PlayerPrincipal, "player", "Player"),
                Definition(PlayerPrincipal, "regional", "Regional"),
            ],
            []));
        Assert.Throws<ArgumentException>(() => CreateRelationshipSetup(
            [
                Definition(PlayerPrincipal, "shared", "Player"),
                Definition(RegionalPrincipal, "shared", "Regional"),
            ],
            []));
    }

    [Fact]
    public void RelationshipSetupRejectsUnknownAndDuplicateStandingReferences()
    {
        PrincipalDefinition[] principals =
        [
            Definition(PlayerPrincipal, "player", "Player"),
            Definition(RegionalPrincipal, "regional", "Regional"),
        ];
        var standing = new InitialStandingSetup(
            RegionalPrincipal,
            PlayerPrincipal,
            new StandingValue(25));

        Assert.Throws<ArgumentException>(() => CreateRelationshipSetup(
            principals,
            [
                new InitialStandingSetup(
                    new PrincipalId(99),
                    PlayerPrincipal,
                    new StandingValue(0)),
            ]));
        Assert.Throws<ArgumentException>(() => CreateRelationshipSetup(
            principals,
            [standing, standing]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateRelationshipSetup(
            principals,
            [
                new InitialStandingSetup(
                    RegionalPrincipal,
                    PlayerPrincipal,
                    new StandingValue(101)),
            ]));
    }

    [Fact]
    public void GameSessionSetupRejectsAssetOwnedByUnknownPrincipal()
    {
        var ship = new InitialShipSetup(
            GameSessionTestFixture.Entity,
            GameSessionTestFixture.Ship,
            GameSessionTestFixture.CargoInventory,
            new PrincipalId(99),
            GameSessionTestFixture.Design,
            GameSessionTestFixture.Position(0, 0),
            GameSessionTestFixture.PlayerController);

        Assert.Throws<ArgumentException>(() => new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [ship],
            GameSessionTestFixture.Relationships,
            factRetentionCapacity: 256));
    }

    [Fact]
    public void SnapshotResolvesDirectionalDefaultsAndPreservesCanonicalOrdering()
    {
        RelationshipSetup relationships = CreateRelationshipSetup(
            [
                Definition(RegionalPrincipal, "regional", "Regional"),
                Definition(PlayerPrincipal, "player", "Player"),
            ],
            [
                new InitialStandingSetup(
                    RegionalPrincipal,
                    PlayerPrincipal,
                    new StandingValue(75)),
            ]);
        var session = new GameSession(
            new GameSessionSetup(
                [new StarSystem(GameSessionTestFixture.System, "Test System")],
                [],
                relationships,
                factRetentionCapacity: 256),
            new DirectLocalNavigationPlanner(
                new GameSessionTestFixture.FixedTravelTimeEstimator()));

        RelationshipSnapshot snapshot = session.CaptureSnapshot().Relationships;

        Assert.Equal(PlayerPrincipal, snapshot.PlayerPrincipalId);
        Assert.Equal(new StandingPolicyId("test-standing"), snapshot.StandingPolicyId);
        Assert.Equal(
            [PlayerPrincipal, RegionalPrincipal],
            snapshot.Principals.Select(principal => principal.Id));
        Assert.Collection(
            snapshot.Standings,
            standing =>
            {
                Assert.Equal(PlayerPrincipal, standing.AssessingPrincipalId);
                Assert.Equal(RegionalPrincipal, standing.SubjectPrincipalId);
                Assert.Equal(new StandingValue(0), standing.Value);
                Assert.Equal(StandingBand.Neutral, standing.Band);
            },
            standing =>
            {
                Assert.Equal(RegionalPrincipal, standing.AssessingPrincipalId);
                Assert.Equal(PlayerPrincipal, standing.SubjectPrincipalId);
                Assert.Equal(new StandingValue(75), standing.Value);
                Assert.Equal(StandingBand.Favorable, standing.Band);
            });
    }

    [Fact]
    public void RelationshipSnapshotCollectionsCannotBeModified()
    {
        RelationshipSnapshot snapshot = GameSessionTestFixture.Create()
            .CaptureSnapshot()
            .Relationships;
        var principals = Assert.IsAssignableFrom<IList<PrincipalSnapshot>>(
            snapshot.Principals);
        var standings = Assert.IsAssignableFrom<IList<StandingSnapshot>>(
            snapshot.Standings);

        Assert.Throws<NotSupportedException>(() => principals.Add(
            new PrincipalSnapshot(
                new PrincipalId(99),
                new PrincipalContentId("injected"),
                "Injected")));
        Assert.Throws<NotSupportedException>(() => standings.Add(
            new StandingSnapshot(
                PlayerPrincipal,
                RegionalPrincipal,
                new StandingValue(0),
                StandingBand.Neutral)));
    }

    private static PrincipalDefinition Definition(
        PrincipalId id,
        string contentId,
        string name) =>
        new(id, new PrincipalContentId(contentId), name);

    private static RelationshipSetup CreateRelationshipSetup(
        IEnumerable<PrincipalDefinition> principals,
        IEnumerable<InitialStandingSetup> standings) =>
        new(principals, PlayerPrincipal, Policy(), standings);

    private static StandingPolicy Policy() =>
        new(
            new StandingPolicyId("test-standing"),
            new StandingValue(-100),
            new StandingValue(100),
            new StandingValue(0),
            new StandingValue(-50),
            new StandingValue(0),
            new StandingValue(50),
            new StandingValue(90));
}
