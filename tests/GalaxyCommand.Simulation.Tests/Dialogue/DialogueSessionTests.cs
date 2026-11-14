using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class DialogueSessionTests
{
    internal static readonly QualifiedContentKey Key = QualifiedContentKey.Create("test.dialogue", "dialogue", "session");
    private static readonly string[] FabricatedPath = ["fabricated-node"];
    private static readonly DialogueParticipantBinding[] Bindings =
        [new("actor", new DialogueParticipant.Ship(new ShipId(1))), new("target", new DialogueParticipant.Ship(new ShipId(2)))];

    [Fact]
    public void StartSuspendResumeAndSelectRetainIdentityAndOfferedOrder()
    {
        using GameSession session = Create(Definition());
        GameplayCommandRecord start = session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        Assert.Equal(CommandResultStatus.Accepted, start.Result.Status);
        DialogueConversationSnapshot first = Assert.Single(session.CaptureDialogue());
        Assert.Equal(new DialogueConversationId(1), first.Id);
        Assert.Equal("entry", first.NodeId);
        Assert.Equal("accept", Assert.Single(first.Choices).Id);
        Assert.True(first.ResponseRequired);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new SuspendDialogueCommand(first.Id)).Result.Status);
        Assert.Equal(DialogueConversationStatus.Suspended, Assert.Single(session.CaptureDialogue()).Status);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(GameSessionTestFixture.Player,
            new SelectDialogueChoiceCommand(first.Id, "accept")).Result.Status);
        session.SubmitCommand(GameSessionTestFixture.Player, new ResumeDialogueCommand(first.Id));
        session.SubmitCommand(GameSessionTestFixture.Player, new SelectDialogueChoiceCommand(first.Id, "accept"));
        Assert.Empty(session.CaptureDialogue());
        Assert.Single(session.ReadFactsAfter(null, 100).Facts.Select(item => item.Fact).OfType<DialogueChoiceCommittedFact>());
        Assert.Single(session.ReadFactsAfter(null, 100).Facts.Select(item => item.Fact).OfType<DialogueEndedFact>());
    }

    [Fact]
    public void InvalidNullSourceDoesNotPoisonOrConsumeDialogueAdmission()
    {
        using GameSession session = Create(Definition());
        Assert.Throws<ArgumentNullException>(() => session.SubmitCommand(null!, new StartDialogueCommand(Key, Bindings)));
        Assert.True(session.IsHealthy);
        Assert.Empty(session.CommandRecords);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new StartDialogueCommand(Key, Bindings)).Result.Status);
        Assert.Equal(new DialogueConversationId(1), Assert.Single(session.CaptureDialogue()).Id);
    }

    [Fact]
    public void MoveConsequenceAndDialogueCommitShareOneCommandSequence()
    {
        using GameSession session = Create(Definition(move: true));
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        var id = Assert.Single(session.CaptureDialogue()).Id;
        GameplayCommandRecord selected = session.SubmitCommand(GameSessionTestFixture.Player, new SelectDialogueChoiceCommand(id, "accept"));
        Assert.Equal(CommandResultStatus.Accepted, selected.Result.Status);
        Assert.Equal(2, session.CommandRecords.Count);
        Assert.Empty(session.CaptureDialogue());
        Assert.NotNull(session.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).CurrentOrder);
        GameFactEnvelope choice = Assert.Single(session.ReadFactsAfter(null, 100).Facts, item => item.Fact is DialogueChoiceCommittedFact);
        Assert.Equal(selected.Envelope.Sequence, Assert.IsType<CommandFactCause>(choice.Cause).Sequence);
        Assert.Contains(session.ReadFactsAfter(null, 100).Facts, item => item.Fact is ShipOrderTransitionFact && item.Cause == choice.Cause);
    }

    [Fact]
    public void RejectedMoveLeavesConversationAndMemoryUnchanged()
    {
        using GameSession session = Create(Definition(move: true), autonomousActor: true);
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        DialogueConversationSnapshot before = Assert.Single(session.CaptureDialogue());
        Assert.False(Assert.Single(before.Choices).IsAvailable);
        GameplayCommandRecord rejected = session.SubmitCommand(GameSessionTestFixture.Player, new SelectDialogueChoiceCommand(before.Id, "accept"));
        Assert.Equal(CommandResultStatus.Rejected, rejected.Result.Status);
        Assert.Equal(rejected.Result.RejectionCode, Assert.Single(before.Choices).UnavailableReason);
        Assert.Equal(before.NodeId, Assert.Single(session.CaptureDialogue()).NodeId);
        Assert.Null(session.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).CurrentOrder);
        Assert.Empty(session.CaptureCheckpoint().Value!.Dialogue!.Memories);
        Assert.Empty(session.ReadFactsAfter(null, 100).Facts.Select(item => item.Fact).OfType<DialogueChoiceCommittedFact>());
    }

    [Fact]
    public void EmptyOfferEntryRejectsWithoutAllocatingAConversation()
    {
        var choice = Choice(offer: [new DialogueCondition.Receipt("heard", DialogueMemoryScope.Session, true)]);
        using GameSession session = Create(Definition(choice: choice));
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(GameSessionTestFixture.Player,
            new StartDialogueCommand(Key, Bindings)).Result.Status);
        Assert.Empty(session.CaptureDialogue());
        Assert.Empty(session.ReadFactsAfter(null, 100).Facts.Select(item => item.Fact).OfType<DialogueStartedFact>());
    }

    [Fact]
    public void ParticipantRemovalEndsSuspendedConversationAtTheCompletedBoundary()
    {
        using GameSession session = Create(Definition());
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        DialogueConversationId id = Assert.Single(session.CaptureDialogue()).Id;
        session.SubmitCommand(GameSessionTestFixture.Player, new SuspendDialogueCommand(id));
        session.RemoveEntity(new EntityRemovalRequest(new EntityId(2), EntityRemovalReason.Despawned, EntityCargoDisposition.DiscardCargo));
        Assert.Empty(session.CaptureDialogue());
        Assert.Contains(session.ReadFactsAfter(null, 100).Facts, item => item.Fact is DialogueEndedFact { Reason: DialogueEndReason.ParticipantUnavailable });
    }

    [Fact]
    public void RecordedOffersDoNotChangeButSelectionRevalidatesCurrentMemory()
    {
        using GameSession session = Create(Definition(choice: Choice(admission:
            [new DialogueCondition.Receipt("heard", DialogueMemoryScope.Session, false)])));
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings.Reverse()));
        DialogueConversationId[] ids = session.CaptureDialogue().Select(item => item.Id).ToArray();
        session.SubmitCommand(GameSessionTestFixture.Player, new SelectDialogueChoiceCommand(ids[0], "accept"));
        DialogueConversationSnapshot remaining = Assert.Single(session.CaptureDialogue());
        Assert.Equal("accept", Assert.Single(remaining.Choices).Id);
        Assert.False(remaining.Choices[0].IsAvailable);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(GameSessionTestFixture.Player,
            new SelectDialogueChoiceCommand(ids[1], "accept")).Result.Status);
        Assert.Equal(ids[1], Assert.Single(session.CaptureDialogue()).Id);
    }

    [Theory]
    [InlineData(DialogueRepeatability.OncePerSession)]
    [InlineData(DialogueRepeatability.OncePerParticipantBinding)]
    public void RepeatabilityUsesCommittedStructuralBindings(DialogueRepeatability repeatability)
    {
        using GameSession session = Create(Definition(repeatability: repeatability));
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(GameSessionTestFixture.Player,
            new StartDialogueCommand(Key, Bindings.Reverse())).Result.Status);
        Assert.Single(session.CaptureDialogue());
        if (repeatability == DialogueRepeatability.OncePerParticipantBinding)
        {
            Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
                new StartDialogueCommand(Key, [new("actor", new DialogueParticipant.Ship(new ShipId(2))),
                    new("target", new DialogueParticipant.Ship(new ShipId(1)))])).Result.Status);
            Assert.Equal(2, session.CaptureDialogue().Count);
        }
    }

    [Fact]
    public void EmptyResultingOffersRejectBeforeMoveOrMemoryCommit()
    {
        DialogueDefinition definition = new(Key,
            [new("actor", [DialogueParticipantKind.Ship]), new("target", [DialogueParticipantKind.Ship])],
            [new("entry", [], [Choice(move: true, transition: new DialogueTransition.Node("next"))]),
             new("next", [], [new DialogueChoice("next-choice", 0, Text("Next"), new DialogueTransition.Complete("done"),
                DialogueRepeatability.Repeatable, offerConditions: [new DialogueCondition.Receipt("heard", DialogueMemoryScope.Session, false)])])],
            "entry", DialogueRepeatability.Repeatable, memories: [new("heard", DialogueMemoryScope.Session)]);
        using GameSession session = Create(definition);
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        DialogueConversationId id = Assert.Single(session.CaptureDialogue()).Id;
        Assert.False(Assert.Single(Assert.Single(session.CaptureDialogue()).Choices).IsAvailable);
        Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(GameSessionTestFixture.Player,
            new SelectDialogueChoiceCommand(id, "accept")).Result.Status);
        Assert.Equal("entry", Assert.Single(session.CaptureDialogue()).NodeId);
        Assert.Null(session.CaptureSnapshot().Ships.Single(ship => ship.Id == new ShipId(1)).CurrentOrder);
        Assert.Empty(session.CaptureCheckpoint().Value!.Dialogue!.Memories);
        Assert.Empty(session.ReadFactsAfter(null, 100).Facts.Select(item => item.Fact).OfType<DialogueChoiceCommittedFact>());
    }

    [Fact]
    public void InformationalDialogueCompletesWithoutAResponseAndRetainsAuthoredPathInFacts()
    {
        DialogueDefinition definition = new(Key, [], [new("entry", [new("notice", 0, Text("Notice"))], [],
            new DialogueTransition.Node("end")), new("end", [], [], new DialogueTransition.Complete("informed"))],
            "entry", DialogueRepeatability.Repeatable);
        using GameSession session = Create(definition);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new StartDialogueCommand(Key, [])).Result.Status);
        Assert.Empty(session.CaptureDialogue());
        DialogueStartedFact started = Assert.Single(session.ReadFactsAfter(null, 100).Facts.Select(item => item.Fact).OfType<DialogueStartedFact>());
        Assert.Equal("entry", started.EnteredNodeIds[0]);
        Assert.Equal("end", started.EnteredNodeIds[1]);
        Assert.Contains(session.ReadFactsAfter(null, 100).Facts, item => item.Fact is DialogueEndedFact { OutcomeId: "informed" });
    }

    [Fact]
    public void CheckpointRestoresSuspendedConversationAndRepeatabilityWithoutReplay()
    {
        DialogueDefinition definition = Definition(repeatability: DialogueRepeatability.OncePerParticipantBinding);
        using GameSession session = Create(definition);
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        DialogueConversationId id = Assert.Single(session.CaptureDialogue()).Id;
        session.SubmitCommand(GameSessionTestFixture.Player, new SuspendDialogueCommand(id));
        CheckpointResult<GameSessionCheckpoint> capture = session.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.Message);
        GameSessionCheckpoint checkpoint = capture.Value!;

        Assert.False(GameSession.RestoreCheckpoint(checkpoint).IsSuccess);
        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint, new DialogueDefinitionCatalog([definition]));
        Assert.True(result.IsSuccess, result.Failure?.Message);
        using GameSession restored = result.Value!;
        Assert.Equal(id, Assert.Single(restored.CaptureDialogue()).Id);
        Assert.Equal(DialogueConversationStatus.Suspended, Assert.Single(restored.CaptureDialogue()).Status);
        Assert.Equal(session.ReadFactsAfter(null, 100).Facts.Count, restored.ReadFactsAfter(null, 100).Facts.Count);
        Assert.Equal(CommandResultStatus.Rejected, restored.SubmitCommand(GameSessionTestFixture.Player,
            new StartDialogueCommand(Key, Bindings.Reverse())).Result.Status);
        restored.SubmitCommand(GameSessionTestFixture.Player, new ResumeDialogueCommand(id));
        Assert.Equal(CommandResultStatus.Accepted, restored.SubmitCommand(GameSessionTestFixture.Player,
            new SelectDialogueChoiceCommand(id, "accept")).Result.Status);
    }

    [Fact]
    public void RestoreRejectsGameplayDefinitionDriftButAllowsPresentationChanges()
    {
        using GameSession session = Create(Definition());
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        CheckpointResult<GameSessionCheckpoint> capture = session.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.Message);
        GameSessionCheckpoint checkpoint = capture.Value!;
        DialogueDefinition changed = Definition(choice: Choice(transition: new DialogueTransition.Complete("changed")));
        Assert.False(GameSession.RestoreCheckpoint(checkpoint, new DialogueDefinitionCatalog([changed])).IsSuccess);
        DialogueDefinition original = Definition();
        DialogueDefinition translated = new(Key, original.Roles,
            [new("entry", [new("other-segment", 0, Text("Translated presentation"), Text("Captain"))],
                [new("accept", 0, Text("Other label"), new DialogueTransition.Complete("done"), DialogueRepeatability.Repeatable,
                    memoryIds: ["heard"])])], "entry", DialogueRepeatability.Repeatable, memories: original.Memories);
        Assert.True(GameSession.RestoreCheckpoint(checkpoint, new DialogueDefinitionCatalog([translated])).IsSuccess);
    }

    [Fact]
    public void HistoricalBindingsMustHaveActuallyExistedInTheirOwningDomain()
    {
        DialogueDefinition definition = Definition();
        using GameSession session = Create(definition);
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        session.SubmitCommand(GameSessionTestFixture.Player, new SelectDialogueChoiceCommand(new DialogueConversationId(1), "accept"));
        DialogueOwnerCheckpoint checkpoint = session.CaptureCheckpoint().Value!.Dialogue!;
        var fabricated = new DialogueParticipantBindings(definition.Roles,
            [new("actor", new DialogueParticipant.Ship(new ShipId(999))), Bindings[1]]);
        checkpoint = checkpoint with { Uses = checkpoint.Uses.Select(use => use with { Bindings = fabricated }).ToArray() };
        Assert.False(DialogueOwner.RestoreCheckpoint(checkpoint, new DialogueDefinitionCatalog([definition]),
            new DialogueReadContext(session.CaptureSnapshot(), [], [], [])).IsSuccess);
    }

    [Fact]
    public void CurrentCheckpointCannotAssumeAnAbsentDialogueOwnerWasEmpty()
    {
        DialogueDefinition definition = Definition();
        using GameSession session = Create(definition);
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value! with { Dialogue = null };
        Assert.False(GameSession.RestoreCheckpoint(checkpoint, new DialogueDefinitionCatalog([definition])).IsSuccess);
    }

    [Fact]
    public void ParticipantLossFactMustRetainItsActualCompletedBoundaryCause()
    {
        DialogueDefinition definition = Definition();
        using GameSession session = Create(definition);
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        session.RemoveEntity(new EntityRemovalRequest(new EntityId(2), EntityRemovalReason.Despawned, EntityCargoDisposition.DiscardCargo));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        GameFactEnvelope?[] facts = checkpoint.Facts.RetainedFacts.Select(envelope => envelope!.Fact is DialogueEndedFact
            ? new GameFactEnvelope(envelope.Sequence, envelope.Timestamp, new DialogueBoundaryFactCause(new SimulationTime(1)), envelope.Fact)
                { PlayerDisclosure = envelope.PlayerDisclosure } : envelope).ToArray();
        checkpoint = checkpoint with { Facts = new(checkpoint.Facts.Capacity, checkpoint.Facts.Sequences, facts) };
        Assert.False(GameSession.RestoreCheckpoint(checkpoint, new DialogueDefinitionCatalog([definition])).IsSuccess);
    }

    [Fact]
    public void RemovedMoveDestinationStillValidatesItsHistoricalEntityLink()
    {
        DialogueDefinition definition = Definition(move: true);
        using GameSession session = Create(definition);
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        session.SubmitCommand(GameSessionTestFixture.Player, new SelectDialogueChoiceCommand(new DialogueConversationId(1), "accept"));
        session.RemoveEntity(new EntityRemovalRequest(new EntityId(2), EntityRemovalReason.Despawned, EntityCargoDisposition.DiscardCargo));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        var definitions = new DialogueDefinitionCatalog([definition]);
        Assert.True(GameSession.RestoreCheckpoint(checkpoint, definitions).IsSuccess);
        checkpoint = checkpoint with { Dialogue = checkpoint.Dialogue! with { Uses = checkpoint.Dialogue.Uses.Select(use =>
            use.Kind == DialogueUseKind.Choice ? use with { Consequence = new MoveShipCommand(new ShipId(1),
                new NavigationDestination.Entity(new EntityId(999)), OrderPlacement.ReplaceAll) } : use).ToArray() } };
        Assert.False(GameSession.RestoreCheckpoint(checkpoint, definitions).IsSuccess);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreRejectsTamperedDialogueFactPathsEvenWhenDisclosureMatches(bool nullPath)
    {
        DialogueDefinition definition = Definition();
        using GameSession session = Create(definition);
        session.SubmitCommand(GameSessionTestFixture.Player, new StartDialogueCommand(Key, Bindings));
        GameSessionCheckpoint checkpoint = session.CaptureCheckpoint().Value!;
        var changedFacts = checkpoint.Facts.RetainedFacts.Select(envelope =>
        {
            if (envelope!.Fact is not DialogueStartedFact started) return envelope;
            var changed = started with { EnteredNodeIds = nullPath ? null! : FabricatedPath };
            return new GameFactEnvelope(envelope.Sequence, envelope.Timestamp, envelope.Cause, changed)
                { PlayerDisclosure = envelope.PlayerDisclosure! with { Fact = changed } };
        }).ToArray();
        checkpoint = checkpoint with { Facts = new(checkpoint.Facts.Capacity, checkpoint.Facts.Sequences, changedFacts) };
        Assert.False(GameSession.RestoreCheckpoint(checkpoint, new DialogueDefinitionCatalog([definition])).IsSuccess);
    }

    internal static DialogueDefinition Definition(bool move = false, DialogueChoice? choice = null,
        DialogueRepeatability repeatability = DialogueRepeatability.Repeatable) => new(Key,
        [new("actor", [DialogueParticipantKind.Ship]), new("target", [DialogueParticipantKind.Ship])],
        [new("entry", [new("line", 0, Text("Hello"))], [choice ?? Choice(move: move)])], "entry", repeatability,
        memories: [new("heard", DialogueMemoryScope.Session)]);

    internal static DialogueChoice Choice(bool move = false, IEnumerable<DialogueCondition>? offer = null,
        IEnumerable<DialogueCondition>? admission = null, DialogueTransition? transition = null) => new("accept", 0,
        Text("Accept"), transition ?? new DialogueTransition.Complete("done"), DialogueRepeatability.Repeatable,
        offerConditions: offer, admissionConditions: admission, memoryIds: ["heard"],
        move: move ? new("actor", "target", OrderPlacement.ReplaceAll) : null);

    internal static DialogueText Text(string fallback) => new("test.dialogue.text", fallback, DialogueMarkup.PlainText);

    internal static GameSession Create(DialogueDefinition definition, bool autonomousActor = false)
    {
        var setup = new GameSessionSetup([new StarSystem(new SystemId(1), "Test")],
            [new InitialShipSetup(new EntityId(1), new ShipId(1), new InventoryId(1), new PrincipalId(1), GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(0, 0), autonomousActor ? new ActorController(ActorControllerKind.Autonomous, new CommandSourceId("npc")) : GameSessionTestFixture.PlayerController),
             new InitialShipSetup(new EntityId(2), new ShipId(2), new InventoryId(2), new PrincipalId(1), GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(100, 0), GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []), [], null, GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, 256,
            GameSessionTestFixture.Player, dialogueDefinitions: new DialogueDefinitionCatalog([definition]));
        return new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(100)));
    }
}
