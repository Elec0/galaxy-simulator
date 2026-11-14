using System.Text.Json;
using System.Text.Json.Nodes;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class DialogueCheckpointCodecTests
{
    private static readonly QualifiedContentKey Definition = QualifiedContentKey.Create("test.dialogue", "dialogue", "greeting");
    private static readonly DialogueConversationId Conversation = new(7);

    [Fact]
    public void ParticipantBindingsRoundTripEveryDomainAndRetainCanonicalRoleOrder()
    {
        CheckpointValueCodec codec = Codec();
        DialogueParticipantBindings bindings = Bindings();

        DialogueParticipantBindings restored = codec.Decode<DialogueParticipantBindings>(codec.Encode(bindings));

        Assert.Equal(bindings, restored);
        Assert.IsType<DialogueParticipant.Ship>(restored.Items[0].Participant);
        Assert.IsType<DialogueParticipant.Principal>(restored.Items[1].Participant);
        Assert.IsType<DialogueParticipant.Station>(restored.Items[2].Participant);
        Assert.Equal(codec.Encode(bindings).GetRawText(), codec.Encode(restored).GetRawText());
    }

    [Fact]
    public void DialogueCommandsFactsAndBoundaryCauseUseClosedLosslessWireContracts()
    {
        CheckpointValueCodec codec = Codec();
        DialogueParticipantBindings bindings = Bindings();
        GameplayCommand[] commands =
        [
            new StartDialogueCommand(Definition, bindings.Items),
            new SelectDialogueChoiceCommand(Conversation, "accept"),
            new SuspendDialogueCommand(Conversation),
            new ResumeDialogueCommand(Conversation),
        ];
        GameFact[] facts =
        [
            new DialogueStartedFact(Conversation, Definition, bindings, ["entry"]),
            new DialogueChoiceCommittedFact(Conversation, Definition, bindings, "accept", "followup", null, ["followup"]),
            new DialogueSuspendedFact(Conversation, Definition, bindings),
            new DialogueResumedFact(Conversation, Definition, bindings),
            new DialogueEndedFact(Conversation, Definition, bindings, DialogueEndReason.Completed, "accepted", ["entry", "followup"]),
        ];

        foreach (GameplayCommand command in commands)
        {
            JsonElement encoded = codec.Encode(command);
            GameplayCommand restored = codec.Decode<GameplayCommand>(encoded);
            Assert.Equal(command.GetType(), restored.GetType());
            Assert.Equal(encoded.GetRawText(), codec.Encode(restored).GetRawText());
        }
        foreach (GameFact fact in facts)
        {
            JsonElement encoded = codec.Encode(fact);
            GameFact restored = codec.Decode<GameFact>(encoded);
            Assert.Equal(fact.GetType(), restored.GetType());
            Assert.Equal(encoded.GetRawText(), codec.Encode(restored).GetRawText());
        }
        GameFactCause cause = new DialogueBoundaryFactCause(new SimulationTime(123));
        Assert.Equal(cause, codec.Decode<GameFactCause>(codec.Encode(cause)));
    }

    [Fact]
    public void StructuralUseAndMemoryReceiptsRetainConsequenceAndScope()
    {
        CheckpointValueCodec codec = Codec();
        DialogueParticipantBindings bindings = Bindings();
        var use = new DialogueUseReceipt(Conversation, Definition, bindings, DialogueUseKind.Choice, "accept",
            new MoveShipCommand(new ShipId(1), new NavigationDestination.Entity(new EntityId(2)), OrderPlacement.Append));
        var globalMemory = new DialogueMemoryReceipt(Definition, "heard", DialogueMemoryScope.Session, null);
        var boundMemory = new DialogueMemoryReceipt(Definition, "heard", DialogueMemoryScope.ParticipantBinding, bindings);
        var state = new DialogueConversationState(Conversation, Definition, bindings, "followup",
            DialogueConversationStatus.Suspended, ["decline", "accept"], ["entry", "followup"]);

        Assert.Equal(use, codec.Decode<DialogueUseReceipt>(codec.Encode(use)));
        Assert.Equal(globalMemory, codec.Decode<DialogueMemoryReceipt>(codec.Encode(globalMemory)));
        Assert.Equal(boundMemory, codec.Decode<DialogueMemoryReceipt>(codec.Encode(boundMemory)));
        JsonElement encodedState = codec.Encode(state);
        DialogueConversationState restored = codec.Decode<DialogueConversationState>(encodedState);
        Assert.Collection(restored.OfferedChoiceIds, id => Assert.Equal("decline", id), id => Assert.Equal("accept", id));
        Assert.Equal(encodedState.GetRawText(), codec.Encode(restored).GetRawText());
    }

    [Fact]
    public void InvalidParticipantTokensIdsAndDuplicateRolesRejectDuringDecode()
    {
        CheckpointValueCodec codec = Codec();
        JsonNode encoded = JsonNode.Parse(codec.Encode(Bindings()).GetRawText())!;
        JsonNode original = encoded.DeepClone();
        encoded["items"]![0]!["participant"]!["kind"] = "facility";
        Assert.Throws<CheckpointJsonException>(() => codec.Decode<DialogueParticipantBindings>(JsonSerializer.SerializeToElement(encoded)));
        encoded = original.DeepClone();
        encoded["items"]![0]!["participant"]!["id"] = "0";
        Assert.Throws<CheckpointJsonException>(() => codec.Decode<DialogueParticipantBindings>(JsonSerializer.SerializeToElement(encoded)));
        encoded = original.DeepClone();
        encoded["items"]![1]!["roleId"] = "actor";
        Assert.Throws<CheckpointJsonException>(() => codec.Decode<DialogueParticipantBindings>(JsonSerializer.SerializeToElement(encoded)));
    }

    [Fact]
    public void OwnerCheckpointRetainsAllocatorCompatibilityAndLiveStateWithoutEmbeddingDefinitions()
    {
        CheckpointValueCodec codec = Codec();
        var checkpoint = new DialogueOwnerCheckpoint(1, new IdSequenceCheckpoint(8),
            [new DialogueDefinitionCompatibility(Definition, "gameplay-fingerprint")],
            [new DialogueConversationState(Conversation, Definition, Bindings(), "entry",
                DialogueConversationStatus.Active, ["accept"], ["entry"])], [], []);

        JsonElement encoded = codec.Encode(checkpoint);
        DialogueOwnerCheckpoint restored = codec.Decode<DialogueOwnerCheckpoint>(encoded);

        Assert.Equal(8UL, restored.Sequences.NextValue);
        Assert.Equal("gameplay-fingerprint", Assert.Single(restored.Definitions).Fingerprint);
        Assert.Equal(Conversation, Assert.Single(restored.Conversations).Id);
        Assert.Equal(encoded.GetRawText(), codec.Encode(restored).GetRawText());
        Assert.False(encoded.GetProperty("definitions")[0].TryGetProperty("nodes", out _));
    }

    private static CheckpointValueCodec Codec()
    {
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);
        return codec;
    }

    private static DialogueParticipantBindings Bindings() => new(
        [new("respondent", [DialogueParticipantKind.Station]), new("actor", [DialogueParticipantKind.Ship]), new("player", [DialogueParticipantKind.Principal])],
        [new("respondent", new DialogueParticipant.Station(new StationId(1))), new("player", new DialogueParticipant.Principal(new PrincipalId(1))),
            new("actor", new DialogueParticipant.Ship(new ShipId(1)))]);
}
