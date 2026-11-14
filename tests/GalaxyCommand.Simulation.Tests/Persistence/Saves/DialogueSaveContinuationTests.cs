using System.Text.Json;
using System.Text.Json.Nodes;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class DialogueSaveContinuationTests
{
    private static readonly SaveJsonLimits Limits = new(16_777_216, 128, 65_536, 100_000);
    private static readonly GameSessionSaveMetadata Metadata = new(
        Guid.Parse("d2191c24-360d-4ad2-b9c0-c37ef6a50762"), DateTimeOffset.UnixEpoch, "dialogue-continuation-test");

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    public void SerializedActiveAndSuspendedConversationsContinueIdenticallyAcrossHostLayouts(int workers, int batchSize)
    {
        DialogueDefinition definition = DialogueSessionTests.Definition(choice: DialogueSessionTests.Choice(admission:
            [new DialogueCondition.Receipt("heard", DialogueMemoryScope.Session, false)]));
        using GameSession original = DialogueSessionTests.Create(definition);
        Start(original);
        Start(original);
        Accept(original, new SuspendDialogueCommand(new DialogueConversationId(1)));
        using GameSession restored = RoundTrip(original, definition, new InteractionBatchExecution(workers, batchSize));

        Assert.Collection(restored.CaptureDialogue(),
            first => Assert.Equal(DialogueConversationStatus.Suspended, first.Status),
            second => Assert.Equal(DialogueConversationStatus.Active, second.Status));
        AssertSameAuthority(original, restored);
        foreach (GameSession session in new[] { original, restored })
        {
            Accept(session, new ResumeDialogueCommand(new DialogueConversationId(1)));
            Accept(session, new SelectDialogueChoiceCommand(new DialogueConversationId(1), "accept"));
            DialogueConversationSnapshot pending = Assert.Single(session.CaptureDialogue());
            Assert.Equal(new DialogueConversationId(2), pending.Id);
            Assert.False(Assert.Single(pending.Choices).IsAvailable);
            Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(GameSessionTestFixture.Player,
                new SelectDialogueChoiceCommand(pending.Id, "accept")).Result.Status);
        }
        AssertSameAuthority(original, restored);
    }

    [Theory]
    [InlineData(DialogueMemoryScope.Session)]
    [InlineData(DialogueMemoryScope.ParticipantBinding)]
    public void SerializedCompletedMemoryAndOncePerBindingReceiptsSurviveWithoutReplay(DialogueMemoryScope memoryScope)
    {
        DialogueDefinition template = DialogueSessionTests.Definition(repeatability: DialogueRepeatability.OncePerParticipantBinding);
        DialogueDefinition definition = new(template.Key, template.Roles, template.Nodes, template.InitialNodeId,
            template.Repeatability, memories: [new("heard", memoryScope)]);
        using GameSession original = DialogueSessionTests.Create(definition);
        Start(original);
        Accept(original, new SelectDialogueChoiceCommand(new DialogueConversationId(1), "accept"));
        using GameSession restored = RoundTrip(original, definition);

        Assert.Empty(restored.CaptureDialogue());
        Assert.Empty(restored.CommandRecords);
        AssertSameAuthority(original, restored);
        foreach (GameSession session in new[] { original, restored })
        {
            Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(GameSessionTestFixture.Player,
                new StartDialogueCommand(DialogueSessionTests.Key, Bindings().Reverse())).Result.Status);
            Accept(session, new StartDialogueCommand(DialogueSessionTests.Key, Bindings(swapped: true)));
            Assert.Equal(new DialogueConversationId(2), Assert.Single(session.CaptureDialogue()).Id);
            DialogueMemoryReceipt memory = Assert.Single(session.CaptureCheckpoint().Value!.Dialogue!.Memories);
            Assert.Equal(memoryScope, memory.Scope);
            if (memoryScope == DialogueMemoryScope.Session) Assert.Null(memory.Bindings);
            else Assert.Equal(new DialogueParticipantBindings(definition.Roles, Bindings()), memory.Bindings);
        }
        AssertSameAuthority(original, restored);
    }

    [Fact]
    public void SerializedSelectedMoveIsNotResubmittedAndContinuesExactlyOnce()
    {
        DialogueDefinition definition = DialogueSessionTests.Definition(move: true);
        using GameSession original = DialogueSessionTests.Create(definition);
        Start(original);
        Accept(original, new SelectDialogueChoiceCommand(new DialogueConversationId(1), "accept"));
        GameSessionCheckpoint before = Capture(original);
        using GameSession restored = RoundTrip(original, definition);

        Assert.Empty(restored.CaptureDialogue());
        Assert.Empty(restored.CommandRecords);
        Assert.Equal(before.CommandAdmission.Sequences, Capture(restored).CommandAdmission.Sequences);
        Assert.Equal(before.Orders.OrderIds, Capture(restored).Orders.OrderIds);
        Assert.Equal(original.ReadFactsAfter(null, 256).Facts.Count, restored.ReadFactsAfter(null, 256).Facts.Count);
        AssertSameAuthority(original, restored);
        foreach (GameSession session in new[] { original, restored })
        {
            GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
            Assert.Equal(CommandResultStatus.Rejected, session.SubmitCommand(GameSessionTestFixture.Player,
                new SelectDialogueChoiceCommand(new DialogueConversationId(1), "accept")).Result.Status);
            Assert.Equal(2UL, Capture(session).Orders.OrderIds.NextValue);
            Assert.Single(session.ReadFactsAfter(null, 256).Facts.Select(envelope => envelope.Fact).OfType<DialogueChoiceCommittedFact>());
        }
        AssertSameAuthority(original, restored);
    }

    [Fact]
    public void SerializedDialogueRequiresCurrentGameplayCatalogButNeverEmbedsAuthoredProse()
    {
        DialogueDefinition originalDefinition = DialogueSessionTests.Definition();
        DialogueDefinition authored = ReplacePresentation(originalDefinition, "AUTHORED-PROSE-NOT-SAVED-XYZ");
        using GameSession original = DialogueSessionTests.Create(authored);
        Start(original);
        InertSaveDocument bytesDecoded = ThroughBytes(Encode(original));
        JsonElement dialogue = bytesDecoded.Root.GetProperty("checkpoint").GetProperty("dialogue");

        Assert.DoesNotContain("AUTHORED-PROSE-NOT-SAVED-XYZ", bytesDecoded.Root.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(2, dialogue.GetProperty("definitions")[0].EnumerateObject().Count());
        Assert.False(GameSessionSaveCodec.Restore(bytesDecoded, Limits).IsSuccess);
        DialogueDefinition translated = ReplacePresentation(originalDefinition, "Localized replacement");
        CheckpointResult<GameSession> admission = GameSessionSaveCodec.Restore(bytesDecoded, Limits,
            dialogueDefinitions: new DialogueDefinitionCatalog([translated]));
        Assert.True(admission.IsSuccess, admission.Failure?.Message);
        using GameSession restored = admission.Value!;
        Assert.Equal("Localized replacement", Assert.Single(Assert.Single(restored.CaptureDialogue()).Segments).Text.InvariantFallback);
        AssertSameAuthority(original, restored);

        DialogueDefinition changed = DialogueSessionTests.Definition(choice:
            DialogueSessionTests.Choice(transition: new DialogueTransition.Complete("changed-outcome")));
        CheckpointResult<GameSession> incompatible = GameSessionSaveCodec.Restore(bytesDecoded, Limits,
            dialogueDefinitions: new DialogueDefinitionCatalog([changed]));
        Assert.False(incompatible.IsSuccess);
        Assert.Equal(CheckpointFailureKind.IncompatibleContent, incompatible.Failure!.Kind);
    }

    [Theory]
    [InlineData("allocator")]
    [InlineData("default-id")]
    [InlineData("missing-memory")]
    [InlineData("extra-memory")]
    [InlineData("missing-choice-use")]
    [InlineData("missing-owner-field")]
    [InlineData("null-owner")]
    public void SerializedCorruptionRejectsWithoutPublishingPartialSessionOrChangingOriginal(string corruption)
    {
        DialogueDefinition definition = DialogueSessionTests.Definition();
        using GameSession original = DialogueSessionTests.Create(definition);
        Start(original);
        Accept(original, new SelectDialogueChoiceCommand(new DialogueConversationId(1), "accept"));
        InertSaveDocument valid = Encode(original);
        JsonNode document = JsonNode.Parse(valid.Root.GetRawText())!;
        JsonNode dialogue = document["checkpoint"]!["dialogue"]!;
        switch (corruption)
        {
            case "allocator": dialogue["sequences"]!["nextValue"] = "1"; break;
            case "default-id": dialogue["uses"]![0]!["conversationId"] = "0"; break;
            case "missing-memory": dialogue["memories"]!.AsArray().Clear(); break;
            case "extra-memory":
                JsonNode extra = dialogue["memories"]![0]!.DeepClone();
                extra["memoryId"] = "fabricated";
                dialogue["memories"]!.AsArray().Add(extra);
                break;
            case "missing-choice-use": dialogue["uses"]!.AsArray().RemoveAt(1); break;
            case "missing-owner-field": dialogue.AsObject().Remove("memories"); break;
            case "null-owner": document["checkpoint"]!["dialogue"] = null; break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        InertSaveDocument corrupt = ThroughBytes(new InertSaveDocument(SaveFormat.CurrentSchemaVersion,
            JsonSerializer.SerializeToElement(document)));

        CheckpointResult<GameSession> rejected = GameSessionSaveCodec.Restore(corrupt, Limits,
            dialogueDefinitions: new DialogueDefinitionCatalog([definition]));

        Assert.False(rejected.IsSuccess);
        Assert.Null(rejected.Value);
        Assert.Equal(valid.Root.GetRawText(), Encode(original).Root.GetRawText());
        Assert.True(original.IsHealthy);
    }

    private static DialogueDefinition ReplacePresentation(DialogueDefinition original, string fallback) => new(original.Key,
        original.Roles, [new("entry", [new("line", 0, DialogueSessionTests.Text(fallback), DialogueSessionTests.Text("Captain"))],
            [DialogueSessionTests.Choice()])], original.InitialNodeId, original.Repeatability, memories: original.Memories);

    private static DialogueParticipantBinding[] Bindings(bool swapped = false) =>
        [new("actor", new DialogueParticipant.Ship(new ShipId(swapped ? 2UL : 1UL))),
         new("target", new DialogueParticipant.Ship(new ShipId(swapped ? 1UL : 2UL)))];

    private static void Start(GameSession session) => Accept(session, new StartDialogueCommand(DialogueSessionTests.Key, Bindings()));

    private static void Accept(GameSession session, GameplayCommand command)
    {
        GameplayCommandRecord record = session.SubmitCommand(GameSessionTestFixture.Player, command);
        Assert.Equal(CommandResultStatus.Accepted, record.Result.Status);
    }

    /// <summary>Traverses the production bounded UTF-8 byte codec before isolated aggregate admission.</summary>
    private static InertSaveDocument ThroughBytes(InertSaveDocument document)
    {
        using var bytes = new MemoryStream();
        SaveWriteResult write = SaveJsonCodec.Write(bytes, document, Limits);
        Assert.True(write.IsSuccess, write.Failure?.Message);
        bytes.Position = 0;
        SaveReadResult read = SaveJsonCodec.Read(bytes, Limits, new SaveMigrationRegistry(SaveFormat.CurrentSchemaVersion, []));
        Assert.True(read.IsSuccess, read.Failure?.Message);
        return read.Document!;
    }

    private static GameSession RoundTrip(GameSession session, DialogueDefinition definition, InteractionBatchExecution? execution = null)
    {
        CheckpointResult<GameSession> restored = GameSessionSaveCodec.Restore(ThroughBytes(Encode(session)), Limits,
            interactionExecution: execution, dialogueDefinitions: new DialogueDefinitionCatalog([definition]));
        Assert.True(restored.IsSuccess, restored.Failure?.Path + ": " + restored.Failure?.Message);
        return restored.Value!;
    }

    private static InertSaveDocument Encode(GameSession session)
    {
        CheckpointResult<InertSaveDocument> encoded = GameSessionSaveCodec.Encode(Capture(session), Metadata, Limits);
        Assert.True(encoded.IsSuccess, encoded.Failure?.Path + ": " + encoded.Failure?.Message);
        return encoded.Value!;
    }

    private static GameSessionCheckpoint Capture(GameSession session)
    {
        CheckpointResult<GameSessionCheckpoint> captured = session.CaptureCheckpoint();
        Assert.True(captured.IsSuccess, captured.Failure?.Path + ": " + captured.Failure?.Message);
        return captured.Value!;
    }

    private static void AssertSameAuthority(GameSession expected, GameSession actual) =>
        Assert.Equal(Encode(expected).Root.GetRawText(), Encode(actual).Root.GetRawText());
}
