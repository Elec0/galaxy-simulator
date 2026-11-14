using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class DialogueConditionSessionTests
{
    private static readonly PrincipalId Other = new(2);
    private static readonly CommandSource OtherSource = new(CommandSourceKind.Autonomous, new("dialogue-other"));
    private static readonly QualifiedContentKey StandingKey = QualifiedContentKey.Create("test.dialogue", "dialogue", "standing");
    private static readonly QualifiedContentKey DetectedKey = QualifiedContentKey.Create("test.dialogue", "dialogue", "detected");
    private static readonly QualifiedContentKey DiscoveredKey = QualifiedContentKey.Create("test.dialogue", "dialogue", "discovered");
    private static readonly QualifiedContentKey StationKey = QualifiedContentKey.Create("test.dialogue", "station", "respondent");

    [Fact]
    public void StandingUsesExplicitPrincipalDirectionAndRevalidatesInclusiveThreshold()
    {
        var condition = new DialogueCondition.Standing("assessing", "subject", 5);
        DialogueDefinition definition = Definition(StandingKey,
            [new("assessing", [DialogueParticipantKind.Principal]), new("subject", [DialogueParticipantKind.Principal])], condition);
        DialogueParticipantBinding[] bindings = [new("assessing", new DialogueParticipant.Principal(new(1))),
            new("subject", new DialogueParticipant.Principal(Other))];
        using GameSession session = Create([definition]);

        AssertRejectedStart(session, StandingKey, bindings);
        ChangeStanding(session, 1, Other, new(1), 50);
        AssertRejectedStart(session, StandingKey, bindings);
        ChangeStanding(session, 2, new(1), Other, 5);
        DialogueConversationId conversation = Start(session, StandingKey, bindings);
        Assert.Equal(new DialogueConversationId(1), conversation);
        Assert.True(Assert.Single(Assert.Single(session.CaptureDialogue()).Choices).IsAvailable);

        ChangeStanding(session, 3, new(1), Other, -1);
        DialogueConversationSnapshot before = Assert.Single(session.CaptureDialogue());
        Assert.Equal("accept", Assert.Single(before.Choices).Id);
        Assert.False(before.Choices[0].IsAvailable);
        AssertRejectedChoice(session, conversation);
        Assert.Equal(before.NodeId, Assert.Single(session.CaptureDialogue()).NodeId);
        Assert.Empty(session.ReadFactsAfter(null, 256).Facts.Select(item => item.Fact).OfType<DialogueChoiceCommittedFact>());

        ChangeStanding(session, 4, new(1), Other, 1);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new SelectDialogueChoiceCommand(conversation, "accept")).Result.Status);
        Assert.Empty(session.CaptureDialogue());
    }

    [Fact]
    public void ShipDetectionFollowsCurrentOwnedSensorCoverageAndSelectionRevalidation()
    {
        DialogueDefinition definition = Definition(DetectedKey, [new("respondent", [DialogueParticipantKind.Ship])],
            new DialogueCondition.Detected("respondent"));
        DialogueParticipantBinding[] bindings = [new("respondent", new DialogueParticipant.Ship(new(2)))];
        using GameSession session = Create([definition]);
        Assert.DoesNotContain(Presentation(session).World.Ships, ship => ship.Id == new ShipId(2));
        AssertRejectedStart(session, DetectedKey, bindings);

        MovePlayer(session, 950);
        Assert.Contains(Presentation(session).World.Ships, ship => ship.Id == new ShipId(2));
        DialogueConversationId conversation = Start(session, DetectedKey, bindings);
        MovePlayer(session, 0);
        Assert.DoesNotContain(Presentation(session).World.Ships, ship => ship.Id == new ShipId(2));
        DialogueConversationSnapshot remaining = Assert.Single(session.CaptureDialogue());
        Assert.Equal(conversation, remaining.Id);
        Assert.False(Assert.Single(remaining.Choices).IsAvailable);
        AssertRejectedChoice(session, conversation);
        Assert.Single(session.CaptureDialogue());
    }

    [Fact]
    public void StationDiscoverySurvivesCoverageLossWhileDetectionBecomesUnavailable()
    {
        DialogueParticipantRole[] roles = [new("respondent", [DialogueParticipantKind.Station])];
        DialogueDefinition detected = Definition(DetectedKey, roles, new DialogueCondition.Detected("respondent"));
        DialogueDefinition discovered = Definition(DiscoveredKey, roles, new DialogueCondition.Discovered("respondent"));
        using GameSession session = Create([detected, discovered]);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(OtherSource,
            new CreateStationSiteCommand(Other, new(2), StationKey, GameSessionTestFixture.Position(1_000, 0), new(0))).Result.Status);
        StationSnapshot station = Assert.Single(session.CaptureSnapshot().Stations);
        DialogueParticipantBinding[] bindings = [new("respondent", new DialogueParticipant.Station(station.Id))];
        Assert.Empty(Presentation(session).World.Stations);
        AssertRejectedStart(session, DetectedKey, bindings);
        AssertRejectedStart(session, DiscoveredKey, bindings);

        MovePlayer(session, 950);
        Assert.Equal(SensorObservationState.Live, Assert.Single(Presentation(session).World.Stations).State);
        DialogueConversationId currentConversation = Start(session, DetectedKey, bindings);
        DialogueConversationId rememberedConversation = Start(session, DiscoveredKey, bindings);
        MovePlayer(session, 0);
        ObservedStationSnapshot stale = Assert.Single(Presentation(session).World.Stations);
        Assert.Equal(SensorObservationState.Stale, stale.State);
        Assert.Equal(station.Id, stale.View.Id);
        Assert.False(Assert.Single(session.CaptureDialogue().Single(item => item.Id == currentConversation).Choices).IsAvailable);
        Assert.True(Assert.Single(session.CaptureDialogue().Single(item => item.Id == rememberedConversation).Choices).IsAvailable);
        AssertRejectedChoice(session, currentConversation);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new SelectDialogueChoiceCommand(rememberedConversation, "accept")).Result.Status);
        Assert.Equal(currentConversation, Assert.Single(session.CaptureDialogue()).Id);
    }

    [Theory]
    [InlineData(DialogueParticipantKind.Ship)]
    [InlineData(DialogueParticipantKind.Station)]
    [InlineData(DialogueParticipantKind.Principal)]
    public void MissingTypedParticipantOwnerRejectsBeforeConversationAllocation(DialogueParticipantKind kind)
    {
        DialogueDefinition definition = Definition(DetectedKey, [new("respondent", [kind])]);
        DialogueParticipant missing = kind switch
        {
            DialogueParticipantKind.Ship => new DialogueParticipant.Ship(new(99)),
            DialogueParticipantKind.Station => new DialogueParticipant.Station(new(99)),
            DialogueParticipantKind.Principal => new DialogueParticipant.Principal(new(99)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        using GameSession session = Create([definition]);
        GameplayCommandRecord rejected = session.SubmitCommand(GameSessionTestFixture.Player,
            new StartDialogueCommand(DetectedKey, [new("respondent", missing)]));
        Assert.Equal(CommandResultStatus.Rejected, rejected.Result.Status);
        Assert.Equal(new CommandRejectionCode("dialogue-participant-unavailable"), rejected.Result.RejectionCode);
        Assert.Empty(session.CaptureDialogue());
        Assert.Empty(session.ReadFactsAfter(null, 256).Facts.Select(item => item.Fact).OfType<DialogueStartedFact>());
    }

    private static DialogueDefinition Definition(QualifiedContentKey key, IEnumerable<DialogueParticipantRole> roles,
        DialogueCondition? condition = null)
    {
        DialogueCondition[] conditions = condition is null ? [] : [condition];
        return new(key, roles, [new("entry", [], [new("accept", 0, DialogueSessionTests.Text("Accept"),
            new DialogueTransition.Complete("done"), DialogueRepeatability.Repeatable, admissionConditions: conditions)])],
            "entry", DialogueRepeatability.Repeatable, conditions);
    }

    private static DialogueConversationId Start(GameSession session, QualifiedContentKey key,
        IEnumerable<DialogueParticipantBinding> bindings)
    {
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new StartDialogueCommand(key, bindings)).Result.Status);
        return session.CaptureDialogue().MaxBy(item => item.Id.Value)!.Id;
    }

    private static void AssertRejectedStart(GameSession session, QualifiedContentKey key,
        IEnumerable<DialogueParticipantBinding> bindings) => Assert.Equal(CommandResultStatus.Rejected,
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(key, bindings)).Result.Status);

    private static void AssertRejectedChoice(GameSession session, DialogueConversationId id) => Assert.Equal(CommandResultStatus.Rejected,
        session.SubmitCommand(GameSessionTestFixture.Player, new SelectDialogueChoiceCommand(id, "accept")).Result.Status);

    private static void ChangeStanding(GameSession session, ulong sequence, PrincipalId assessing, PrincipalId subject, long delta) =>
        Assert.IsType<StandingChangeBatchResult.Applied>(session.CommitStandingChanges(new(
            new(StandingChangeSourceKind.Explicit, sequence), [new(assessing, subject, new(new(1), delta, StandingChangeReason.Explicit))])));

    private static GamePresentationSnapshot Presentation(GameSession session) => session.CapturePresentation(
        new(GameSessionTestFixture.Principal, [], null, null, 256));

    private static void MovePlayer(GameSession session, long x)
    {
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(x, 0), OrderPlacement.ReplaceAll)).Result.Status);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
    }

    private static GameSession Create(IEnumerable<DialogueDefinition> definitions)
    {
        var playerDesign = new ShipDesign(new(1), "Player", new([], new(1)), new(10),
            GameSessionTestFixture.ManeuverCapability, sensorRadiusUnits: 100, pickupRangeUnits: 100);
        var otherDesign = new ShipDesign(new(2), "Other", new([], new(1)), new(10),
            GameSessionTestFixture.ManeuverCapability, sensorRadiusUnits: 0, pickupRangeUnits: 100,
            stationConstructionCapability: new(100, new(1)));
        var station = new StationDefinition(StationKey, "Respondent", 0, new(0), new(0), new([], new(1)), 0, [], new(1), []);
        var relationships = new RelationshipSetup([new(new(1), new("player"), "Player"), new(Other, new("other"), "Other")],
            new(1), GameSessionTestFixture.StandingPolicy, []);
        var setup = new GameSessionSetup([new StarSystem(GameSessionTestFixture.System, "System")],
            [new(new(1), new(1), new(1), new(1), playerDesign, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController),
             new(new(2), new(2), new(2), Other, otherDesign, GameSessionTestFixture.Position(1_000, 0), new(ActorControllerKind.Autonomous, OtherSource.Id))],
            new ConnectorTopology([], []), [], null, relationships, GameSessionTestFixture.RootSeed, 256, GameSessionTestFixture.Player,
            stations: new(new([station], []), new MaterialInventoryCompatibilityMap([]), StationTerritoryPolicy.Absent, [new(OtherSource, Other)]),
            dialogueDefinitions: new(definitions));
        return new(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
    }
}
