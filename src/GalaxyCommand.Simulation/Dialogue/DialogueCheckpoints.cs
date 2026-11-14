using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record DialogueDefinitionCompatibility(QualifiedContentKey Key, string Fingerprint);
internal sealed record DialogueOwnerCheckpoint(int BehaviorVersion, IdSequenceCheckpoint Sequences,
    IReadOnlyList<DialogueDefinitionCompatibility> Definitions, IReadOnlyList<DialogueConversationState> Conversations,
    IReadOnlyList<DialogueUseReceipt> Uses, IReadOnlyList<DialogueMemoryReceipt> Memories);

/// <summary>Gameplay-only compatibility. Locale resources, prose and apparent speakers never affect saved authority.</summary>
internal static class DialogueDefinitionFingerprint
{
    /// <summary>Hashes explicit stable fields and canonical model order; no reflection or collection hash becomes compatibility.</summary>
    internal static string Compute(DialogueDefinition definition)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);
        writer.Write("dialogue-gameplay-v1");
        writer.Write(definition.Key.ToString());
        writer.Write(definition.InitialNodeId);
        writer.Write((int)definition.Repeatability);
        writer.Write(definition.Roles.Count);
        foreach (DialogueParticipantRole role in definition.Roles)
        {
            writer.Write(role.Id);
            writer.Write(role.AcceptedKinds.Count);
            foreach (DialogueParticipantKind kind in role.AcceptedKinds) writer.Write((int)kind);
        }
        writer.Write(definition.Memories.Count);
        foreach (DialogueMemoryDeclaration memory in definition.Memories) { writer.Write(memory.Id); writer.Write((int)memory.Scope); }
        Conditions(writer, definition.StartConditions);
        writer.Write(definition.Nodes.Count);
        foreach (DialogueNode node in definition.Nodes)
        {
            writer.Write(node.Id);
            writer.Write(node.ResponseRequired);
            if (node.AutomaticTransition is { } automatic) Transition(writer, automatic);
            writer.Write(node.Choices.Count);
            foreach (DialogueChoice choice in node.Choices)
            {
                writer.Write(choice.Id); writer.Write(choice.Order); writer.Write((int)choice.Repeatability);
                Transition(writer, choice.Transition);
                Conditions(writer, choice.OfferConditions); Conditions(writer, choice.AdmissionConditions);
                writer.Write(choice.MemoryIds.Count);
                foreach (string memory in choice.MemoryIds) writer.Write(memory);
                writer.Write(choice.Move is not null);
                if (choice.Move is { } move) { writer.Write(move.ActorRoleId); writer.Write(move.DestinationRoleId); writer.Write((int)move.Placement); }
            }
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    /// <summary>Writes closed condition tokens and typed values in declared evaluation order.</summary>
    private static void Conditions(BinaryWriter writer, ReadOnlyCollection<DialogueCondition> conditions)
    {
        writer.Write(conditions.Count);
        foreach (DialogueCondition condition in conditions)
            switch (condition)
            {
                case DialogueCondition.Receipt receipt:
                    writer.Write("receipt"); writer.Write(receipt.MemoryId); writer.Write((int)receipt.Scope); writer.Write(receipt.Exists); break;
                case DialogueCondition.Standing standing:
                    writer.Write("standing"); writer.Write(standing.AssessingRoleId); writer.Write(standing.SubjectRoleId); writer.Write(standing.MinimumValue); break;
                case DialogueCondition.Detected detected:
                    writer.Write("detected"); writer.Write(detected.RoleId); break;
                case DialogueCondition.Discovered discovered:
                    writer.Write("discovered"); writer.Write(discovered.RoleId); break;
                default: throw new InvalidOperationException("Unsupported validated dialogue condition.");
            }
    }

    /// <summary>Writes technical transition identity without presentation data.</summary>
    private static void Transition(BinaryWriter writer, DialogueTransition transition)
    {
        switch (transition)
        {
            case DialogueTransition.Node node: writer.Write("node"); writer.Write(node.NodeId); break;
            case DialogueTransition.Complete outcome: writer.Write("complete"); writer.Write(outcome.OutcomeId); break;
            default: throw new InvalidOperationException("Unsupported validated dialogue transition.");
        }
    }
}
