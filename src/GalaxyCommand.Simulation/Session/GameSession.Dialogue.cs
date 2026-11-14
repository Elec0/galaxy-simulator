namespace GalaxyCommand.Simulation;

public sealed partial class GameSession
{
    private readonly DialogueOwner _dialogue;

    public DialogueDefinitionCatalog DialogueDefinitions { get; private init; } = new([]);

    /// <summary>Reads unresolved conversations and current advisory choice availability without changing recorded offers.</summary>
    public IReadOnlyList<DialogueConversationSnapshot> CaptureDialogue()
    {
        EnsureHealthy();
        if (!_dialogue.HasLiveConversations) return [];
        DialogueReadContext context = CaptureDialogueReadContext();
        return Array.AsReadOnly(_dialogue.CaptureRecordedConversations().Select(snapshot => snapshot with
        {
            Choices = Array.AsReadOnly(snapshot.Choices.Select(choice =>
            {
                PreparedDialogueChange selection = _dialogue.PrepareSelection(snapshot.Id, choice.Id, context);
                CommandResult availability = selection.Result;
                if (availability.Status == CommandResultStatus.Accepted && selection.Move is { } move)
                    availability = _runtime.PrepareDialogueMove(_playerCommandSource, move).Result;
                return choice with { IsAvailable = availability.Status == CommandResultStatus.Accepted,
                    UnavailableReason = availability.RejectionCode };
            }).ToArray()),
        }).ToArray());
    }

    /// <summary>Captures authoritative existence, relationships and the approved narrow player observation evidence once.</summary>
    private DialogueReadContext CaptureDialogueReadContext() => CaptureDialogueReadContext(_runtime, _stationObservations.Values);

    /// <summary>Uses the same immutable existence and observation inputs during isolated restore and live admission.</summary>
    private static DialogueReadContext CaptureDialogueReadContext(ActorOrderRuntimeCoordinator runtime,
        IEnumerable<ObservedStationSnapshot> stationObservations, GameSessionCheckpoint? checkpoint = null)
    {
        GameSnapshot world = runtime.CaptureSnapshot();
        PrincipalId player = world.Relationships.PlayerPrincipalId;
        ShipSensorCoverageSnapshot ships = runtime.CaptureShipCoverage(player);
        PlayerCoverageBoundary coverage = runtime.CaptureCoverageBoundary(player);
        StationId[] detected = world.Stations.Where(station => station.PrincipalId == player || coverage.Contains(station.Position))
            .Select(station => station.Id).ToArray();
        StationId[] discovered = stationObservations.Select(station => station.View.Id)
            .Concat(world.Stations.Where(station => station.PrincipalId == player).Select(station => station.Id)).Distinct().ToArray();
        return new(world, ships.VisibleShipIds, detected, discovered,
            checkpoint?.Lifecycle.RemovalReceipts.OfType<EntityRemovalReceiptCheckpoint>().Where(receipt => receipt.Request is not null)
                .ToDictionary(receipt => receipt.ShipId, receipt => receipt.Request!.EntityId),
            checkpoint?.Stations?.Removals.ToDictionary(receipt => receipt.Station.Id, receipt => receipt.Station.EntityId));
    }

    /// <summary>
    /// Coordinates one dialogue revision and at most one prepared ordinary move.
    /// Both publish under the already admitted envelope, without reentrant submission.
    /// </summary>
    private GameplayCommandHandlingResult HandleDialogue(GameplayCommandEnvelope envelope)
    {
        PreparedDialogueChange prepared = _dialogue.Prepare(envelope, _playerCommandSource, CaptureDialogueReadContext());
        if (prepared.Result.Status != CommandResultStatus.Accepted) return new(prepared.Result);
        ActorOrderRuntimeCoordinator.PreparedDialogueMove? move = prepared.Move is { } intent
            ? _runtime.PrepareDialogueMove(envelope.Source, intent) : null;
        if (move is { Result.Status: CommandResultStatus.Rejected }) return new(move.Result);
        try
        {
            var proposals = new List<GameFactProposal>();
            if (move is not null) proposals.AddRange(_runtime.CommitDialogueMove(move));
            proposals.AddRange(_dialogue.Commit(prepared));
            return new(CommandResult.Accepted(), proposals);
        }
        catch
        {
            // The other owner may already have committed. Recovery must restore
            // the aggregate rather than retrying only the dialogue half.
            _dialogue.Poison();
            _runtime.PoisonDialogueCommit();
            throw;
        }
    }

    /// <summary>Ends missing-participant conversations before disclosure at each completed timestamp.</summary>
    private void CompleteDialogueBoundary()
    {
        if (!_dialogue.HasLiveConversations) return;
        try
        {
            IReadOnlyList<GameFactProposal> proposals = _dialogue.InvalidateMissing(CaptureDialogueReadContext());
            if (proposals.Count != 0)
                _facts.Commit(CurrentTime, new DialogueBoundaryFactCause(CurrentTime), proposals);
        }
        catch
        {
            _dialogue.Poison();
            _runtime.PoisonDialogueCommit();
            throw;
        }
    }

    /// <summary>Compares saved dialogue paths by value rather than collection reference identity.</summary>
    private static bool SameDialogueFactPayload(DialogueFact left, DialogueFact right) => left.ConversationId == right.ConversationId
        && left.DefinitionKey == right.DefinitionKey && left.Bindings is not null && right.Bindings is not null
        && left.Bindings.Equals(right.Bindings) && (left, right) switch
        {
            (DialogueStartedFact a, DialogueStartedFact b) => SameDialoguePath(a.EnteredNodeIds, b.EnteredNodeIds),
            (DialogueChoiceCommittedFact a, DialogueChoiceCommittedFact b) => a.ChoiceId == b.ChoiceId
                && a.ResultingNodeId == b.ResultingNodeId && a.OutcomeId == b.OutcomeId && SameDialoguePath(a.EnteredNodeIds, b.EnteredNodeIds),
            (DialogueEndedFact a, DialogueEndedFact b) => a.Reason == b.Reason && a.OutcomeId == b.OutcomeId
                && SameDialoguePath(a.EnteredNodeIds, b.EnteredNodeIds),
            (DialogueSuspendedFact, DialogueSuspendedFact) or (DialogueResumedFact, DialogueResumedFact) => true,
            _ => false,
        };

    /// <summary>Malformed saved paths reject rather than reaching graph validation through a null collection.</summary>
    private static bool SameDialoguePath(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        left is not null && right is not null && left.SequenceEqual(right);

    /// <summary>Checks dialogue's closed immediate-cause policy against completed boundary time and admitted command allocation.</summary>
    private static bool IsValidDialogueCause(GameFactEnvelope envelope, CommandAdmissionCheckpoint admission)
    {
        if (envelope.Fact is DialogueEndedFact { Reason: DialogueEndReason.ParticipantUnavailable })
            return envelope.Cause is DialogueBoundaryFactCause boundary && boundary.Timestamp == envelope.Timestamp;
        if (envelope.Fact is DialogueFact)
            return envelope.Cause is CommandFactCause command && (admission.Sequences.NextValue is not { } next || command.Sequence.Value < next);
        return envelope.Cause is not DialogueBoundaryFactCause;
    }
}
