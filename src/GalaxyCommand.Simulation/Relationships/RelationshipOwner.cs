using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

internal sealed class RelationshipOwner
{
    private readonly IReadOnlyList<PrincipalDefinition> _principals;
    private readonly HashSet<PrincipalId> _principalIds;
    private readonly Dictionary<(PrincipalId Assessing, PrincipalId Subject), StandingValue>
        _standingOverrides;
    private readonly StandingPolicy _standingPolicy;
    private readonly Dictionary<(PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>
        _diplomaticConditions;
    private readonly Dictionary<RelationshipGrantId, RelationshipGrantState> _grants;
    private readonly Dictionary<StandingChangeBatchId, CommittedStandingBatch>
        _committedStandingBatches = [];
    private readonly Dictionary<RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>
        _committedPolicyBatches = [];

    /// <summary>
    /// Copies canonical setup state into the authoritative runtime owner.
    /// </summary>
    internal RelationshipOwner(RelationshipSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _principals = setup.Principals;
        _principalIds = setup.Principals
            .Select(principal => principal.Id)
            .ToHashSet();
        _standingPolicy = setup.StandingPolicy;
        _standingOverrides = setup.Standings.ToDictionary(
            standing => (
                standing.AssessingPrincipalId,
                standing.SubjectPrincipalId),
            standing => standing.Value);
        _diplomaticConditions = setup.DiplomaticConditions.ToDictionary(
            value => (value.LowerPrincipalId, value.UpperPrincipalId),
            value => value.Condition);
        _grants = setup.Grants.ToDictionary(
            grant => grant.Id,
            grant => new RelationshipGrantState(
                grant.Id,
                grant.IssuerPrincipalId,
                grant.HolderPrincipalId,
                grant.Kind,
                grant.MinimumStandingBand,
                IsIssued: true));
        PlayerPrincipalId = setup.PlayerPrincipalId;
    }

    /// <summary>
    /// Directly constructs a fully validated restored owner without replaying
    /// setup or committed relationship deliveries.
    /// </summary>
    private RelationshipOwner(
        IReadOnlyList<PrincipalDefinition> principals,
        StandingPolicy standingPolicy,
        Dictionary<(PrincipalId Assessing, PrincipalId Subject), StandingValue> standings,
        Dictionary<(PrincipalId Lower, PrincipalId Upper), DiplomaticCondition> diplomacy,
        Dictionary<RelationshipGrantId, RelationshipGrantState> grants,
        Dictionary<StandingChangeBatchId, CommittedStandingBatch> standingReceipts,
        Dictionary<RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>
            policyReceipts,
        PrincipalId playerPrincipalId)
    {
        _principals = principals;
        _principalIds = principals.Select(principal => principal.Id).ToHashSet();
        _standingPolicy = standingPolicy;
        _standingOverrides = standings;
        _diplomaticConditions = diplomacy;
        _grants = grants;
        _committedStandingBatches = standingReceipts;
        _committedPolicyBatches = policyReceipts;
        PlayerPrincipalId = playerPrincipalId;
    }

    internal PrincipalId PlayerPrincipalId { get; }

    /// <summary>
    /// Captures complete relationship truth and durable delivery receipts in
    /// stable identity order, excluding derived bands and projections.
    /// </summary>
    internal RelationshipCheckpoint CaptureCheckpoint()
    {
        RelationshipSnapshot snapshot = CaptureSnapshot();
        return new RelationshipCheckpoint(
            PlayerPrincipalId,
            new RelationshipStandingPolicyCheckpoint(
                _standingPolicy.Id.Value,
                _standingPolicy.Minimum,
                _standingPolicy.Maximum,
                _standingPolicy.Initial,
                _standingPolicy.AdversarialThreshold,
                _standingPolicy.NeutralThreshold,
                _standingPolicy.FavorableThreshold,
                _standingPolicy.AlliedThreshold),
            _principals.Select(principal => new RelationshipPrincipalCheckpoint(
                principal.Id,
                principal.ContentId.Value,
                principal.Name)),
            snapshot.Standings.Select(standing => new RelationshipStandingCheckpoint(
                standing.AssessingPrincipalId,
                standing.SubjectPrincipalId,
                standing.Value)),
            snapshot.DiplomaticConditions.Select(value =>
                new RelationshipDiplomacyCheckpoint(
                    value.LowerPrincipalId,
                    value.UpperPrincipalId,
                    value.Condition)),
            _grants.Values
                .OrderBy(grant => grant.Id.Value)
                .Select(grant => new RelationshipGrantCheckpoint(
                    grant.Id,
                    grant.IssuerPrincipalId,
                    grant.HolderPrincipalId,
                    grant.Kind.Value,
                    grant.MinimumStandingBand,
                    grant.IsIssued)),
            _committedStandingBatches
                .OrderBy(value => value.Key.SourceKind)
                .ThenBy(value => value.Key.Value)
                .Select(value => new StandingBatchReceiptCheckpoint(
                    value.Key,
                    value.Value.Proposals,
                    value.Value.Result)),
            _committedPolicyBatches
                .OrderBy(value => value.Key.SourceKind)
                .ThenBy(value => value.Key.Value)
                .Select(value => new PolicyBatchReceiptCheckpoint(
                    value.Key,
                    value.Value.Proposals,
                    value.Value.Result)));
    }

    /// <summary>
    /// Validates and directly restores complete relationship truth and
    /// idempotency receipts without emitting facts or replaying any batch.
    /// </summary>
    internal static CheckpointResult<RelationshipOwner> RestoreCheckpoint(
        RelationshipCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        CheckpointResult<StandingPolicy> policyResult = RestoreStandingPolicy(
            checkpoint.StandingPolicy);
        if (!policyResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(policyResult.Failure!);
        }

        CheckpointResult<IReadOnlyList<PrincipalDefinition>> principalResult =
            RestorePrincipals(checkpoint);
        if (!principalResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(principalResult.Failure!);
        }

        IReadOnlyList<PrincipalDefinition> principals = principalResult.Value!;
        var principalIds = principals.Select(principal => principal.Id).ToHashSet();
        if (checkpoint.PlayerPrincipalId.Value == 0
            || !principalIds.Contains(checkpoint.PlayerPrincipalId))
        {
            return Rejected(
                "$.checkpoint.relationships.playerPrincipalId",
                "The player principal must name one registered principal.");
        }

        CheckpointResult<Dictionary<
            (PrincipalId Assessing, PrincipalId Subject), StandingValue>> standingResult =
            RestoreStandings(checkpoint, principalIds, policyResult.Value!);
        if (!standingResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(standingResult.Failure!);
        }

        CheckpointResult<Dictionary<
            (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>> diplomacyResult =
            RestoreDiplomacy(checkpoint, principalIds);
        if (!diplomacyResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(diplomacyResult.Failure!);
        }

        CheckpointResult<Dictionary<RelationshipGrantId, RelationshipGrantState>> grantResult =
            RestoreGrants(checkpoint, principalIds);
        if (!grantResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(grantResult.Failure!);
        }

        CheckpointResult<Dictionary<StandingChangeBatchId, CommittedStandingBatch>>
            standingReceiptResult = RestoreStandingReceipts(
                checkpoint,
                principalIds,
                policyResult.Value!);
        if (!standingReceiptResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(
                standingReceiptResult.Failure!);
        }

        CheckpointResult<Dictionary<
            RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>>
            policyReceiptResult = RestorePolicyReceipts(
                checkpoint,
                principalIds,
                grantResult.Value!);
        if (!policyReceiptResult.IsSuccess)
        {
            return CheckpointResult<RelationshipOwner>.Rejected(
                policyReceiptResult.Failure!);
        }

        return CheckpointResult<RelationshipOwner>.Success(new RelationshipOwner(
            principals,
            policyResult.Value!,
            standingResult.Value!,
            diplomacyResult.Value!,
            grantResult.Value!,
            standingReceiptResult.Value!,
            policyReceiptResult.Value!,
            checkpoint.PlayerPrincipalId));
    }

    /// <summary>
    /// Validates and reduces a complete batch without mutating relationship state.
    /// </summary>
    internal StandingChangePreparation PrepareStandingChanges(StandingChangeBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        StandingChangeProposal[] ordered = batch.Proposals
            .OrderBy(proposal => proposal.AssessingPrincipalId.Value)
            .ThenBy(proposal => proposal.SubjectPrincipalId.Value)
            .ThenBy(proposal => proposal.Contribution.Id.Value)
            .ToArray();
        if (_committedStandingBatches.TryGetValue(batch.Id, out CommittedStandingBatch? prior))
        {
            return prior.Proposals.SequenceEqual(ordered)
                ? new StandingChangePreparation.Resolved(prior.Result)
                : ResolvedRejection(
                    batch.Id,
                    StandingChangeRejectionReason.BatchIdentityConflict);
        }

        var contributionKeys = new HashSet<(
            PrincipalId Assessing,
            PrincipalId Subject,
            StandingChangeContributionId Contribution)>();
        foreach (StandingChangeProposal proposal in ordered)
        {
            if (!_principalIds.Contains(proposal.AssessingPrincipalId)
                || !_principalIds.Contains(proposal.SubjectPrincipalId))
            {
                return ResolvedRejection(
                    batch.Id,
                    StandingChangeRejectionReason.UnknownPrincipal);
            }

            if (!contributionKeys.Add((
                    proposal.AssessingPrincipalId,
                    proposal.SubjectPrincipalId,
                    proposal.Contribution.Id)))
            {
                return ResolvedRejection(
                    batch.Id,
                    StandingChangeRejectionReason.DuplicateContribution);
            }
        }

        var outcomes = new List<StandingChangeOutcome>();
        foreach (IGrouping<(PrincipalId AssessingPrincipalId, PrincipalId SubjectPrincipalId),
                     StandingChangeProposal> group in ordered.GroupBy(proposal => (
                         proposal.AssessingPrincipalId,
                         proposal.SubjectPrincipalId)))
        {
            StandingChangeContribution[] contributions = group
                .Select(proposal => proposal.Contribution)
                .ToArray();
            long combinedDelta = 0;
            try
            {
                foreach (StandingChangeContribution contribution in contributions)
                {
                    combinedDelta = checked(combinedDelta + contribution.Delta);
                }
            }
            catch (OverflowException)
            {
                return ResolvedRejection(
                    batch.Id,
                    StandingChangeRejectionReason.DeltaOverflow);
            }

            StandingValue priorValue = GetStanding(
                group.Key.AssessingPrincipalId,
                group.Key.SubjectPrincipalId);
            long unboundedResult;
            try
            {
                unboundedResult = checked(priorValue.Value + combinedDelta);
            }
            catch (OverflowException)
            {
                return ResolvedRejection(
                    batch.Id,
                    StandingChangeRejectionReason.DeltaOverflow);
            }

            var resultingValue = new StandingValue(Math.Clamp(
                unboundedResult,
                _standingPolicy.Minimum.Value,
                _standingPolicy.Maximum.Value));
            outcomes.Add(new StandingChangeOutcome(
                group.Key.AssessingPrincipalId,
                group.Key.SubjectPrincipalId,
                priorValue,
                _standingPolicy.GetBand(priorValue),
                combinedDelta,
                resultingValue,
                _standingPolicy.GetBand(resultingValue),
                contributions));
        }

        var result = new StandingChangeBatchResult.Applied(
            batch.Id,
            new ReadOnlyCollection<StandingChangeOutcome>(outcomes));
        return new StandingChangePreparation.Prepared(new PreparedStandingChange(
            batch.Id,
            ordered,
            result,
            outcomes.Where(outcome => outcome.Changed).ToArray()));
    }

    /// <summary>
    /// Applies an already validated standing preparation through operations that
    /// cannot reject and records its idempotent receipt.
    /// </summary>
    internal StandingChangeBatchResult ApplyStandingChanges(
        PreparedStandingChange prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        foreach (StandingChangeOutcome outcome in prepared.ChangedOutcomes)
        {
            _standingOverrides[(
                outcome.AssessingPrincipalId,
                outcome.SubjectPrincipalId)] = outcome.ResultingValue;
        }

        _committedStandingBatches.Add(
            prepared.BatchId,
            new CommittedStandingBatch(prepared.Proposals, prepared.Result));
        return prepared.Result;
    }

    /// <summary>
    /// Validates and prepares diplomacy and grant changes without mutation.
    /// </summary>
    internal RelationshipPolicyChangePreparation PreparePolicyChanges(
        RelationshipPolicyChangeBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        RelationshipPolicyChangeProposal[] ordered = batch.Proposals
            .OrderBy(ProposalPrimaryIdentity)
            .ThenBy(ProposalSecondaryIdentity)
            .ThenBy(ProposalKindOrder)
            .ThenBy(ProposalGrantIdentity)
            .ToArray();
        if (_committedPolicyBatches.TryGetValue(
                batch.Id,
                out CommittedRelationshipPolicyBatch? prior))
        {
            return prior.Proposals.SequenceEqual(ordered)
                ? new RelationshipPolicyChangePreparation.Resolved(prior.Result)
                : PolicyRejection(
                    batch.Id,
                    RelationshipPolicyChangeRejectionReason.BatchIdentityConflict);
        }

        var diplomaticKeys = new HashSet<(PrincipalId Lower, PrincipalId Upper)>();
        var grantIds = new HashSet<RelationshipGrantId>();
        var diplomaticOutcomes = new List<DiplomaticConditionChangeOutcome>();
        var grantOutcomes = new List<RelationshipGrantChangeOutcome>();
        foreach (RelationshipPolicyChangeProposal proposal in ordered)
        {
            switch (proposal)
            {
                case SetDiplomaticConditionProposal diplomatic:
                    if (!PrincipalsExist(
                            diplomatic.LowerPrincipalId,
                            diplomatic.UpperPrincipalId))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.UnknownPrincipal);
                    }

                    if (!diplomaticKeys.Add((
                            diplomatic.LowerPrincipalId,
                            diplomatic.UpperPrincipalId)))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason
                                .DuplicateDiplomaticAssignment);
                    }

                    diplomaticOutcomes.Add(new DiplomaticConditionChangeOutcome(
                        diplomatic.LowerPrincipalId,
                        diplomatic.UpperPrincipalId,
                        GetDiplomaticCondition(
                            diplomatic.LowerPrincipalId,
                            diplomatic.UpperPrincipalId),
                        diplomatic.Condition,
                        diplomatic.Reason));
                    break;

                case IssueRelationshipGrantProposal issue:
                    if (!PrincipalsExist(issue.IssuerPrincipalId, issue.HolderPrincipalId))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.UnknownPrincipal);
                    }

                    if (!grantIds.Add(issue.Id))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.DuplicateGrantAssignment);
                    }

                    if (_grants.ContainsKey(issue.Id))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.GrantIdentityAlreadyExists);
                    }

                    if (GetStandingBand(issue.IssuerPrincipalId, issue.HolderPrincipalId)
                        < issue.MinimumStandingBand)
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason
                                .StandingRequirementNotMet);
                    }

                    grantOutcomes.Add(new RelationshipGrantChangeOutcome(
                        issue.Id,
                        issue.IssuerPrincipalId,
                        issue.HolderPrincipalId,
                        issue.Kind,
                        issue.MinimumStandingBand,
                        priorIssued: false,
                        resultingIssued: true,
                        issue.Reason));
                    break;

                case RevokeRelationshipGrantProposal revoke:
                    if (!grantIds.Add(revoke.Id))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.DuplicateGrantAssignment);
                    }

                    if (!_grants.TryGetValue(revoke.Id, out RelationshipGrantState? grant))
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.UnknownGrant);
                    }

                    if (!grant.IsIssued)
                    {
                        return PolicyRejection(
                            batch.Id,
                            RelationshipPolicyChangeRejectionReason.GrantAlreadyRevoked);
                    }

                    grantOutcomes.Add(new RelationshipGrantChangeOutcome(
                        grant.Id,
                        grant.IssuerPrincipalId,
                        grant.HolderPrincipalId,
                        grant.Kind,
                        grant.MinimumStandingBand,
                        priorIssued: true,
                        resultingIssued: false,
                        revoke.Reason));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported relationship policy proposal {proposal.GetType().Name}.");
            }
        }

        var result = new RelationshipPolicyChangeBatchResult.Applied(
            batch.Id,
            GameSnapshotCollection.Copy(diplomaticOutcomes),
            GameSnapshotCollection.Copy(grantOutcomes));
        return new RelationshipPolicyChangePreparation.Prepared(
            new PreparedRelationshipPolicyChange(batch.Id, ordered, result));
    }

    /// <summary>
    /// Applies an already validated diplomacy and grant preparation and records
    /// its idempotent receipt.
    /// </summary>
    internal RelationshipPolicyChangeBatchResult ApplyPolicyChanges(
        PreparedRelationshipPolicyChange prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        foreach (DiplomaticConditionChangeOutcome outcome in
                 prepared.Result.DiplomaticOutcomes.Where(value => value.Changed))
        {
            var key = (outcome.LowerPrincipalId, outcome.UpperPrincipalId);
            if (outcome.ResultingCondition == DiplomaticCondition.Peace)
            {
                _diplomaticConditions.Remove(key);
            }
            else
            {
                _diplomaticConditions[key] = outcome.ResultingCondition;
            }
        }

        foreach (RelationshipGrantChangeOutcome outcome in prepared.Result.GrantOutcomes)
        {
            _grants[outcome.Id] = new RelationshipGrantState(
                outcome.Id,
                outcome.IssuerPrincipalId,
                outcome.HolderPrincipalId,
                outcome.Kind,
                outcome.MinimumStandingBand,
                outcome.ResultingIssued);
        }

        _committedPolicyBatches.Add(
            prepared.BatchId,
            new CommittedRelationshipPolicyBatch(prepared.Proposals, prepared.Result));
        return prepared.Result;
    }

    /// <summary>
    /// Returns the mutual condition for a registered, distinct principal pair.
    /// </summary>
    internal DiplomaticCondition GetDiplomaticCondition(
        PrincipalId firstPrincipalId,
        PrincipalId secondPrincipalId)
    {
        ValidateKnownDistinctPrincipals(firstPrincipalId, secondPrincipalId);
        (PrincipalId lower, PrincipalId upper) = firstPrincipalId.Value < secondPrincipalId.Value
            ? (firstPrincipalId, secondPrincipalId)
            : (secondPrincipalId, firstPrincipalId);
        return _diplomaticConditions.GetValueOrDefault(
            (lower, upper),
            DiplomaticCondition.Peace);
    }

    /// <summary>
    /// Reports whether any matching issued grant currently satisfies its
    /// issuer-to-holder standing requirement.
    /// </summary>
    internal bool HasEffectiveGrant(
        PrincipalId issuerPrincipalId,
        PrincipalId holderPrincipalId,
        RelationshipGrantKind kind)
    {
        ValidateKnownDistinctPrincipals(issuerPrincipalId, holderPrincipalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind.Value);
        return _grants.Values.Any(grant =>
            grant.IssuerPrincipalId == issuerPrincipalId
            && grant.HolderPrincipalId == holderPrincipalId
            && grant.Kind == kind
            && IsEffective(grant));
    }

    /// <summary>
    /// Resolves the complete directional matrix in stable principal order.
    /// </summary>
    internal RelationshipSnapshot CaptureSnapshot()
    {
        var standings = new List<StandingSnapshot>(
            checked(_principals.Count * Math.Max(0, _principals.Count - 1)));
        var diplomaticConditions = new List<DiplomaticConditionSnapshot>(
            checked(_principals.Count * Math.Max(0, _principals.Count - 1) / 2));
        foreach (PrincipalDefinition assessing in _principals)
        {
            foreach (PrincipalDefinition subject in _principals)
            {
                if (assessing.Id == subject.Id)
                {
                    continue;
                }

                StandingValue value = GetStanding(assessing.Id, subject.Id);
                standings.Add(new StandingSnapshot(
                    assessing.Id,
                    subject.Id,
                    value,
                    _standingPolicy.GetBand(value)));
                if (assessing.Id.Value < subject.Id.Value)
                {
                    diplomaticConditions.Add(new DiplomaticConditionSnapshot(
                        assessing.Id,
                        subject.Id,
                        GetDiplomaticCondition(assessing.Id, subject.Id)));
                }
            }
        }

        return new RelationshipSnapshot(
            PlayerPrincipalId,
            _standingPolicy.Id,
            GameSnapshotCollection.Copy(_principals.Select(principal =>
                new PrincipalSnapshot(
                    principal.Id,
                    principal.ContentId,
                    principal.Name))),
            GameSnapshotCollection.Copy(standings),
            GameSnapshotCollection.Copy(diplomaticConditions),
            GameSnapshotCollection.Copy(_grants.Values
                .OrderBy(grant => grant.Id.Value)
                .Select(grant => new RelationshipGrantSnapshot(
                    grant.Id,
                    grant.IssuerPrincipalId,
                    grant.HolderPrincipalId,
                    grant.Kind,
                    grant.MinimumStandingBand,
                    grant.IsIssued,
                    IsEffective(grant)))));
    }

    /// <summary>
    /// Reconstructs the exact standing policy after validating its raw saved
    /// identity, bounds, initial value, and ordered thresholds.
    /// </summary>
    private static CheckpointResult<StandingPolicy> RestoreStandingPolicy(
        RelationshipStandingPolicyCheckpoint? checkpoint)
    {
        const string path = "$.checkpoint.relationships.standingPolicy";
        if (checkpoint is null || string.IsNullOrWhiteSpace(checkpoint.Id))
        {
            return RejectedPolicy(path, "The standing policy identity is required.");
        }

        if (checkpoint.Minimum.Value >= checkpoint.AdversarialThreshold.Value
            || checkpoint.AdversarialThreshold.Value >= checkpoint.NeutralThreshold.Value
            || checkpoint.NeutralThreshold.Value >= checkpoint.FavorableThreshold.Value
            || checkpoint.FavorableThreshold.Value >= checkpoint.AlliedThreshold.Value
            || checkpoint.AlliedThreshold.Value > checkpoint.Maximum.Value)
        {
            return RejectedPolicy(path, "Standing bounds and thresholds are not ordered.");
        }

        if (!IsWithinPolicy(
                checkpoint.Initial,
                checkpoint.Minimum,
                checkpoint.Maximum))
        {
            return RejectedPolicy(
                $"{path}.initial",
                "The initial standing is outside the policy bounds.");
        }

        return CheckpointResult<StandingPolicy>.Success(new StandingPolicy(
            new StandingPolicyId(checkpoint.Id),
            checkpoint.Minimum,
            checkpoint.Maximum,
            checkpoint.Initial,
            checkpoint.AdversarialThreshold,
            checkpoint.NeutralThreshold,
            checkpoint.FavorableThreshold,
            checkpoint.AlliedThreshold));
    }

    /// <summary>
    /// Restores unique runtime and content principal identities in canonical
    /// order without resolving display metadata from an implicit catalog.
    /// </summary>
    private static CheckpointResult<IReadOnlyList<PrincipalDefinition>> RestorePrincipals(
        RelationshipCheckpoint checkpoint)
    {
        const string path = "$.checkpoint.relationships.principals";
        var ids = new HashSet<PrincipalId>();
        var contentIds = new HashSet<string>(StringComparer.Ordinal);
        var principals = new List<PrincipalDefinition>(checkpoint.Principals.Count);
        for (int index = 0; index < checkpoint.Principals.Count; index++)
        {
            RelationshipPrincipalCheckpoint? principal = checkpoint.Principals[index];
            if (principal is null || principal.Id.Value == 0)
            {
                return RejectedPrincipals(
                    $"{path}[{index}]",
                    "A principal checkpoint with a nonzero identity is required.");
            }

            if (!ids.Add(principal.Id))
            {
                return RejectedPrincipals(
                    $"{path}[{index}].id",
                    "The principal identity is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(principal.ContentId)
                || !contentIds.Add(principal.ContentId))
            {
                return RejectedPrincipals(
                    $"{path}[{index}].contentId",
                    "The principal content identity is missing or duplicated.");
            }

            if (string.IsNullOrWhiteSpace(principal.Name))
            {
                return RejectedPrincipals(
                    $"{path}[{index}].name",
                    "The principal display name is required.");
            }

            principals.Add(new PrincipalDefinition(
                principal.Id,
                new PrincipalContentId(principal.ContentId),
                principal.Name));
        }

        principals.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
        return CheckpointResult<IReadOnlyList<PrincipalDefinition>>.Success(
            new ReadOnlyCollection<PrincipalDefinition>(principals));
    }

    /// <summary>
    /// Requires one exact in-range standing value for every directional pair,
    /// preventing omitted external data from silently taking the policy default.
    /// </summary>
    private static CheckpointResult<Dictionary<
        (PrincipalId Assessing, PrincipalId Subject), StandingValue>> RestoreStandings(
        RelationshipCheckpoint checkpoint,
        HashSet<PrincipalId> principalIds,
        StandingPolicy policy)
    {
        const string path = "$.checkpoint.relationships.standings";
        int expectedCount = checked(principalIds.Count * Math.Max(0, principalIds.Count - 1));
        if (checkpoint.Standings.Count != expectedCount)
        {
            return RejectedStandings(
                path,
                "The complete directional standing matrix is required.");
        }

        var standings = new Dictionary<
            (PrincipalId Assessing, PrincipalId Subject), StandingValue>();
        for (int index = 0; index < checkpoint.Standings.Count; index++)
        {
            RelationshipStandingCheckpoint? standing = checkpoint.Standings[index];
            if (standing is null
                || standing.AssessingPrincipalId == standing.SubjectPrincipalId
                || !principalIds.Contains(standing.AssessingPrincipalId)
                || !principalIds.Contains(standing.SubjectPrincipalId))
            {
                return RejectedStandings(
                    $"{path}[{index}]",
                    "A standing entry must reference two distinct registered principals.");
            }

            if (!IsWithinPolicy(standing.Value, policy.Minimum, policy.Maximum))
            {
                return RejectedStandings(
                    $"{path}[{index}].value",
                    "The standing value is outside the restored policy bounds.");
            }

            if (!standings.TryAdd(
                    (standing.AssessingPrincipalId, standing.SubjectPrincipalId),
                    standing.Value))
            {
                return RejectedStandings(
                    $"{path}[{index}]",
                    "The directional standing pair is duplicated.");
            }
        }

        return CheckpointResult<Dictionary<
            (PrincipalId Assessing, PrincipalId Subject), StandingValue>>.Success(standings);
    }

    /// <summary>
    /// Requires one defined condition for every canonical unordered pair so a
    /// hand edit cannot accidentally replace missing diplomacy with peace.
    /// </summary>
    private static CheckpointResult<Dictionary<
        (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>> RestoreDiplomacy(
        RelationshipCheckpoint checkpoint,
        HashSet<PrincipalId> principalIds)
    {
        const string path = "$.checkpoint.relationships.diplomaticConditions";
        int expectedCount = checked(
            principalIds.Count * Math.Max(0, principalIds.Count - 1) / 2);
        if (checkpoint.DiplomaticConditions.Count != expectedCount)
        {
            return RejectedDiplomacy(path, "The complete diplomatic pair matrix is required.");
        }

        var diplomacy = new Dictionary<
            (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>();
        for (int index = 0; index < checkpoint.DiplomaticConditions.Count; index++)
        {
            RelationshipDiplomacyCheckpoint? value =
                checkpoint.DiplomaticConditions[index];
            if (value is null
                || value.LowerPrincipalId.Value >= value.UpperPrincipalId.Value
                || !principalIds.Contains(value.LowerPrincipalId)
                || !principalIds.Contains(value.UpperPrincipalId)
                || !Enum.IsDefined(value.Condition))
            {
                return RejectedDiplomacy(
                    $"{path}[{index}]",
                    "Diplomacy must name a defined condition for a canonical principal pair.");
            }

            if (!diplomacy.TryAdd(
                    (value.LowerPrincipalId, value.UpperPrincipalId),
                    value.Condition))
            {
                return RejectedDiplomacy(
                    $"{path}[{index}]",
                    "The diplomatic principal pair is duplicated.");
            }
        }

        return CheckpointResult<Dictionary<
            (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>>.Success(diplomacy);
    }

    /// <summary>
    /// Restores issued and revoked grants as persistent state while leaving
    /// effectiveness derived from the restored standing matrix.
    /// </summary>
    private static CheckpointResult<Dictionary<RelationshipGrantId, RelationshipGrantState>>
        RestoreGrants(
            RelationshipCheckpoint checkpoint,
            HashSet<PrincipalId> principalIds)
    {
        const string path = "$.checkpoint.relationships.grants";
        var grants = new Dictionary<RelationshipGrantId, RelationshipGrantState>();
        for (int index = 0; index < checkpoint.Grants.Count; index++)
        {
            RelationshipGrantCheckpoint? grant = checkpoint.Grants[index];
            if (grant is null
                || grant.Id.Value == 0
                || grant.IssuerPrincipalId == grant.HolderPrincipalId
                || !principalIds.Contains(grant.IssuerPrincipalId)
                || !principalIds.Contains(grant.HolderPrincipalId)
                || string.IsNullOrWhiteSpace(grant.Kind)
                || !Enum.IsDefined(grant.MinimumStandingBand))
            {
                return RejectedGrants(
                    $"{path}[{index}]",
                    "A grant has invalid identity, endpoints, kind, or standing band.");
            }

            var state = new RelationshipGrantState(
                grant.Id,
                grant.IssuerPrincipalId,
                grant.HolderPrincipalId,
                new RelationshipGrantKind(grant.Kind),
                grant.MinimumStandingBand,
                grant.IsIssued);
            if (!grants.TryAdd(grant.Id, state))
            {
                return RejectedGrants(
                    $"{path}[{index}].id",
                    "The relationship grant identity is duplicated.");
            }
        }

        return CheckpointResult<Dictionary<RelationshipGrantId, RelationshipGrantState>>
            .Success(grants);
    }

    /// <summary>
    /// Restores standing delivery receipts only when their canonical proposals
    /// and saved outcomes independently prove the same checked reduction.
    /// </summary>
    private static CheckpointResult<Dictionary<StandingChangeBatchId, CommittedStandingBatch>>
        RestoreStandingReceipts(
            RelationshipCheckpoint checkpoint,
            HashSet<PrincipalId> principalIds,
            StandingPolicy policy)
    {
        const string path = "$.checkpoint.relationships.standingReceipts";
        var receipts = new Dictionary<StandingChangeBatchId, CommittedStandingBatch>();
        for (int index = 0; index < checkpoint.StandingReceipts.Count; index++)
        {
            StandingBatchReceiptCheckpoint? receipt = checkpoint.StandingReceipts[index];
            string receiptPath = $"{path}[{index}]";
            if (receipt is null
                || receipt.BatchId.Value == 0
                || !Enum.IsDefined(receipt.BatchId.SourceKind)
                || receipt.Proposals is null
                || receipt.Proposals.Count == 0
                || receipt.Result is null
                || receipt.Result.BatchId != receipt.BatchId
                || receipt.Result.Outcomes is null)
            {
                return RejectedStandingReceipts(
                    receiptPath,
                    "A standing receipt is missing required identity, proposals, or result.");
            }

            StandingChangeProposal[] proposals = receipt.Proposals
                .OfType<StandingChangeProposal>()
                .ToArray();
            if (proposals.Length != receipt.Proposals.Count
                || proposals.Any(proposal =>
                    !principalIds.Contains(proposal.AssessingPrincipalId)
                    || !principalIds.Contains(proposal.SubjectPrincipalId)
                    || proposal.AssessingPrincipalId == proposal.SubjectPrincipalId
                    || proposal.Contribution.Id.Value == 0
                    || !Enum.IsDefined(proposal.Contribution.Reason)))
            {
                return RejectedStandingReceipts(
                    $"{receiptPath}.proposals",
                    "Standing receipt proposals are missing or structurally invalid.");
            }

            StandingChangeProposal[] canonical = proposals
                .OrderBy(proposal => proposal.AssessingPrincipalId.Value)
                .ThenBy(proposal => proposal.SubjectPrincipalId.Value)
                .ThenBy(proposal => proposal.Contribution.Id.Value)
                .ToArray();
            if (!proposals.SequenceEqual(canonical))
            {
                return RejectedStandingReceipts(
                    $"{receiptPath}.proposals",
                    "Standing receipt proposals are not in canonical order.");
            }

            var contributionIds = new HashSet<(
                PrincipalId Assessing,
                PrincipalId Subject,
                StandingChangeContributionId Contribution)>();
            if (proposals.Any(proposal => !contributionIds.Add((
                    proposal.AssessingPrincipalId,
                    proposal.SubjectPrincipalId,
                    proposal.Contribution.Id))))
            {
                return RejectedStandingReceipts(
                    $"{receiptPath}.proposals",
                    "A standing receipt contribution identity is duplicated.");
            }

            IGrouping<(PrincipalId Assessing, PrincipalId Subject), StandingChangeProposal>[]
                groups = proposals.GroupBy(proposal => (
                    proposal.AssessingPrincipalId,
                    proposal.SubjectPrincipalId)).ToArray();
            if (receipt.Result.Outcomes.Count != groups.Length)
            {
                return RejectedStandingReceipts(
                    $"{receiptPath}.result.outcomes",
                    "Standing receipt outcomes do not match proposal groups.");
            }

            for (int outcomeIndex = 0; outcomeIndex < groups.Length; outcomeIndex++)
            {
                IGrouping<(PrincipalId Assessing, PrincipalId Subject),
                    StandingChangeProposal> group = groups[outcomeIndex];
                StandingChangeOutcome? outcome = receipt.Result.Outcomes[outcomeIndex];
                if (!IsValidStandingOutcome(outcome, group, policy))
                {
                    return RejectedStandingReceipts(
                        $"{receiptPath}.result.outcomes[{outcomeIndex}]",
                        "The standing outcome disagrees with its proposals or policy.");
                }
            }

            var restoredResult = new StandingChangeBatchResult.Applied(
                receipt.BatchId,
                GameSnapshotCollection.Copy(receipt.Result.Outcomes));
            if (!receipts.TryAdd(
                    receipt.BatchId,
                    new CommittedStandingBatch(proposals, restoredResult)))
            {
                return RejectedStandingReceipts(
                    $"{receiptPath}.batchId",
                    "The standing batch identity is duplicated.");
            }
        }

        return CheckpointResult<Dictionary<StandingChangeBatchId, CommittedStandingBatch>>
            .Success(receipts);
    }

    /// <summary>
    /// Verifies one saved standing outcome against its exact ordered
    /// contributions, checked sum, clamping, and policy-derived bands.
    /// </summary>
    private static bool IsValidStandingOutcome(
        StandingChangeOutcome? outcome,
        IGrouping<(PrincipalId Assessing, PrincipalId Subject), StandingChangeProposal> group,
        StandingPolicy policy)
    {
        if (outcome is null
            || outcome.AssessingPrincipalId != group.Key.Assessing
            || outcome.SubjectPrincipalId != group.Key.Subject
            || outcome.Contributions is null)
        {
            return false;
        }

        StandingChangeContribution[] contributions = group
            .Select(proposal => proposal.Contribution)
            .ToArray();
        if (!outcome.Contributions.SequenceEqual(contributions)
            || !IsWithinPolicy(outcome.PriorValue, policy.Minimum, policy.Maximum)
            || !IsWithinPolicy(outcome.ResultingValue, policy.Minimum, policy.Maximum)
            || outcome.PriorBand != policy.GetBand(outcome.PriorValue)
            || outcome.ResultingBand != policy.GetBand(outcome.ResultingValue))
        {
            return false;
        }

        try
        {
            long delta = 0;
            foreach (StandingChangeContribution contribution in contributions)
            {
                delta = checked(delta + contribution.Delta);
            }

            long unbounded = checked(outcome.PriorValue.Value + delta);
            return outcome.CombinedDelta == delta
                && outcome.ResultingValue.Value == Math.Clamp(
                    unbounded,
                    policy.Minimum.Value,
                    policy.Maximum.Value);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Restores diplomacy and grant delivery receipts after proving canonical
    /// proposal order and exact correspondence with their saved outcomes.
    /// </summary>
    private static CheckpointResult<Dictionary<
        RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>>
        RestorePolicyReceipts(
            RelationshipCheckpoint checkpoint,
            HashSet<PrincipalId> principalIds,
            Dictionary<RelationshipGrantId, RelationshipGrantState> grants)
    {
        const string path = "$.checkpoint.relationships.policyReceipts";
        var receipts = new Dictionary<
            RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>();
        var issuedGrantIds = new HashSet<RelationshipGrantId>();
        var revokedGrantIds = new HashSet<RelationshipGrantId>();
        for (int index = 0; index < checkpoint.PolicyReceipts.Count; index++)
        {
            PolicyBatchReceiptCheckpoint? receipt = checkpoint.PolicyReceipts[index];
            string receiptPath = $"{path}[{index}]";
            if (receipt is null
                || receipt.BatchId.Value == 0
                || !Enum.IsDefined(receipt.BatchId.SourceKind)
                || receipt.Proposals is null
                || receipt.Proposals.Count == 0
                || receipt.Result is null
                || receipt.Result.BatchId != receipt.BatchId
                || receipt.Result.DiplomaticOutcomes is null
                || receipt.Result.GrantOutcomes is null)
            {
                return RejectedPolicyReceipts(
                    receiptPath,
                    "A policy receipt is missing required identity, proposals, or result.");
            }

            RelationshipPolicyChangeProposal[] proposals = receipt.Proposals
                .OfType<RelationshipPolicyChangeProposal>()
                .ToArray();
            if (proposals.Length != receipt.Proposals.Count
                || proposals.Any(proposal => !IsValidPolicyProposal(proposal, principalIds)))
            {
                return RejectedPolicyReceipts(
                    $"{receiptPath}.proposals",
                    "Policy receipt proposals are missing or structurally invalid.");
            }

            RelationshipPolicyChangeProposal[] canonical = proposals
                .OrderBy(ProposalPrimaryIdentity)
                .ThenBy(ProposalSecondaryIdentity)
                .ThenBy(ProposalKindOrder)
                .ThenBy(ProposalGrantIdentity)
                .ToArray();
            if (!proposals.SequenceEqual(canonical))
            {
                return RejectedPolicyReceipts(
                    $"{receiptPath}.proposals",
                    "Policy receipt proposals are not in canonical order.");
            }

            if (!HasUniquePolicyAssignments(proposals)
                || !PolicyOutcomesMatch(
                    proposals,
                    receipt.Result,
                    principalIds,
                    grants))
            {
                return RejectedPolicyReceipts(
                    $"{receiptPath}.result",
                    "Policy receipt outcomes disagree with their proposals or restored grants.");
            }

            foreach (RelationshipGrantChangeOutcome outcome in receipt.Result.GrantOutcomes)
            {
                HashSet<RelationshipGrantId> identities = outcome.ResultingIssued
                    ? issuedGrantIds
                    : revokedGrantIds;
                if (!identities.Add(outcome.Id))
                {
                    return RejectedPolicyReceipts(
                        $"{receiptPath}.result",
                        "A grant transition is committed by more than one receipt.");
                }
            }

            var restoredResult = new RelationshipPolicyChangeBatchResult.Applied(
                receipt.BatchId,
                GameSnapshotCollection.Copy(receipt.Result.DiplomaticOutcomes),
                GameSnapshotCollection.Copy(receipt.Result.GrantOutcomes));
            if (!receipts.TryAdd(
                    receipt.BatchId,
                    new CommittedRelationshipPolicyBatch(proposals, restoredResult)))
            {
                return RejectedPolicyReceipts(
                    $"{receiptPath}.batchId",
                    "The policy batch identity is duplicated.");
            }
        }
        foreach (RelationshipGrantState grant in grants.Values)
        {
            bool wasRevokedByReceipt = revokedGrantIds.Contains(grant.Id);
            if (grant.IsIssued == wasRevokedByReceipt)
            {
                return RejectedPolicyReceipts(
                    path,
                    "Grant state disagrees with its committed issuance and revocation receipts.");
            }
        }

        return CheckpointResult<Dictionary<
            RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>>
            .Success(receipts);
    }

    /// <summary>
    /// Validates closed policy proposal variants without depending on their
    /// constructors having run during external decoding.
    /// </summary>
    private static bool IsValidPolicyProposal(
        RelationshipPolicyChangeProposal proposal,
        HashSet<PrincipalId> principalIds) =>
        proposal switch
        {
            SetDiplomaticConditionProposal value =>
                value.LowerPrincipalId.Value < value.UpperPrincipalId.Value
                && principalIds.Contains(value.LowerPrincipalId)
                && principalIds.Contains(value.UpperPrincipalId)
                && Enum.IsDefined(value.Condition)
                && Enum.IsDefined(value.Reason),
            IssueRelationshipGrantProposal value =>
                value.Id.Value != 0
                && value.IssuerPrincipalId != value.HolderPrincipalId
                && principalIds.Contains(value.IssuerPrincipalId)
                && principalIds.Contains(value.HolderPrincipalId)
                && !string.IsNullOrWhiteSpace(value.Kind.Value)
                && Enum.IsDefined(value.MinimumStandingBand)
                && Enum.IsDefined(value.Reason),
            RevokeRelationshipGrantProposal value =>
                value.Id.Value != 0 && Enum.IsDefined(value.Reason),
            _ => false,
        };

    /// <summary>
    /// Enforces the same one-assignment-per-pair and per-grant invariant used
    /// by live policy batch preparation.
    /// </summary>
    private static bool HasUniquePolicyAssignments(
        IEnumerable<RelationshipPolicyChangeProposal> proposals)
    {
        var diplomaticPairs = new HashSet<(PrincipalId Lower, PrincipalId Upper)>();
        var grantIds = new HashSet<RelationshipGrantId>();
        foreach (RelationshipPolicyChangeProposal proposal in proposals)
        {
            switch (proposal)
            {
                case SetDiplomaticConditionProposal value
                    when !diplomaticPairs.Add((
                        value.LowerPrincipalId,
                        value.UpperPrincipalId)):
                case IssueRelationshipGrantProposal issue
                    when !grantIds.Add(issue.Id):
                case RevokeRelationshipGrantProposal revoke
                    when !grantIds.Add(revoke.Id):
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Matches each saved result list to its proposal subset and verifies that
    /// grant metadata remains anchored by the restored authoritative grant.
    /// </summary>
    private static bool PolicyOutcomesMatch(
        IReadOnlyList<RelationshipPolicyChangeProposal> proposals,
        RelationshipPolicyChangeBatchResult.Applied result,
        HashSet<PrincipalId> principalIds,
        Dictionary<RelationshipGrantId, RelationshipGrantState> grants)
    {
        SetDiplomaticConditionProposal[] diplomatic = proposals
            .OfType<SetDiplomaticConditionProposal>()
            .ToArray();
        RelationshipPolicyChangeProposal[] grantProposals = proposals
            .Where(proposal => proposal is IssueRelationshipGrantProposal
                or RevokeRelationshipGrantProposal)
            .ToArray();
        if (result.DiplomaticOutcomes.Count != diplomatic.Length
            || result.GrantOutcomes.Count != grantProposals.Length)
        {
            return false;
        }

        for (int index = 0; index < diplomatic.Length; index++)
        {
            SetDiplomaticConditionProposal proposal = diplomatic[index];
            DiplomaticConditionChangeOutcome? outcome = result.DiplomaticOutcomes[index];
            if (outcome is null
                || outcome.LowerPrincipalId != proposal.LowerPrincipalId
                || outcome.UpperPrincipalId != proposal.UpperPrincipalId
                || outcome.ResultingCondition != proposal.Condition
                || outcome.Reason != proposal.Reason
                || !principalIds.Contains(outcome.LowerPrincipalId)
                || !principalIds.Contains(outcome.UpperPrincipalId)
                || !Enum.IsDefined(outcome.PriorCondition)
                || !Enum.IsDefined(outcome.ResultingCondition)
                || !Enum.IsDefined(outcome.Reason))
            {
                return false;
            }
        }

        for (int index = 0; index < grantProposals.Length; index++)
        {
            RelationshipPolicyChangeProposal proposal = grantProposals[index];
            RelationshipGrantChangeOutcome? outcome = result.GrantOutcomes[index];
            if (outcome is null
                || !grants.TryGetValue(outcome.Id, out RelationshipGrantState? grant)
                || grant.IssuerPrincipalId != outcome.IssuerPrincipalId
                || grant.HolderPrincipalId != outcome.HolderPrincipalId
                || grant.Kind != outcome.Kind
                || grant.MinimumStandingBand != outcome.MinimumStandingBand
                || outcome.PriorIssued == outcome.ResultingIssued
                || !Enum.IsDefined(outcome.MinimumStandingBand)
                || !Enum.IsDefined(outcome.Reason))
            {
                return false;
            }

            bool matches = proposal switch
            {
                IssueRelationshipGrantProposal issue =>
                    outcome.Id == issue.Id
                    && outcome.IssuerPrincipalId == issue.IssuerPrincipalId
                    && outcome.HolderPrincipalId == issue.HolderPrincipalId
                    && outcome.Kind == issue.Kind
                    && outcome.MinimumStandingBand == issue.MinimumStandingBand
                    && !outcome.PriorIssued
                    && outcome.ResultingIssued
                    && outcome.Reason == issue.Reason,
                RevokeRelationshipGrantProposal revoke =>
                    outcome.Id == revoke.Id
                    && outcome.PriorIssued
                    && !outcome.ResultingIssued
                    && outcome.Reason == revoke.Reason,
                _ => false,
            };
            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWithinPolicy(
        StandingValue value,
        StandingValue minimum,
        StandingValue maximum) =>
        value.Value >= minimum.Value && value.Value <= maximum.Value;

    private static CheckpointResult<T> RejectedCheckpoint<T>(
        string path,
        string message)
        where T : class =>
        CheckpointResult<T>.Rejected(new CheckpointValidationFailure(path, message));

    private static CheckpointResult<RelationshipOwner> Rejected(
        string path,
        string message) =>
        RejectedCheckpoint<RelationshipOwner>(path, message);

    private static CheckpointResult<StandingPolicy> RejectedPolicy(
        string path,
        string message) =>
        RejectedCheckpoint<StandingPolicy>(path, message);

    private static CheckpointResult<IReadOnlyList<PrincipalDefinition>> RejectedPrincipals(
        string path,
        string message) =>
        RejectedCheckpoint<IReadOnlyList<PrincipalDefinition>>(path, message);

    private static CheckpointResult<Dictionary<
        (PrincipalId Assessing, PrincipalId Subject), StandingValue>> RejectedStandings(
        string path,
        string message) =>
        RejectedCheckpoint<Dictionary<
            (PrincipalId Assessing, PrincipalId Subject), StandingValue>>(path, message);

    private static CheckpointResult<Dictionary<
        (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>> RejectedDiplomacy(
        string path,
        string message) =>
        RejectedCheckpoint<Dictionary<
            (PrincipalId Lower, PrincipalId Upper), DiplomaticCondition>>(path, message);

    private static CheckpointResult<Dictionary<
        RelationshipGrantId, RelationshipGrantState>> RejectedGrants(
        string path,
        string message) =>
        RejectedCheckpoint<Dictionary<RelationshipGrantId, RelationshipGrantState>>(
            path,
            message);

    private static CheckpointResult<Dictionary<
        StandingChangeBatchId, CommittedStandingBatch>> RejectedStandingReceipts(
        string path,
        string message) =>
        RejectedCheckpoint<Dictionary<StandingChangeBatchId, CommittedStandingBatch>>(
            path,
            message);

    private static CheckpointResult<Dictionary<
        RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>>
        RejectedPolicyReceipts(
            string path,
            string message) =>
        RejectedCheckpoint<Dictionary<
            RelationshipPolicyChangeBatchId, CommittedRelationshipPolicyBatch>>(
                path,
                message);

    private StandingValue GetStanding(
        PrincipalId assessingPrincipalId,
        PrincipalId subjectPrincipalId) =>
        _standingOverrides.GetValueOrDefault(
            (assessingPrincipalId, subjectPrincipalId),
            _standingPolicy.Initial);

    private StandingBand GetStandingBand(
        PrincipalId assessingPrincipalId,
        PrincipalId subjectPrincipalId) =>
        _standingPolicy.GetBand(GetStanding(assessingPrincipalId, subjectPrincipalId));

    /// <summary>
    /// Combines persistent issuance with the current directional standing band.
    /// </summary>
    private bool IsEffective(RelationshipGrantState grant) =>
        grant.IsIssued
        && GetStandingBand(grant.IssuerPrincipalId, grant.HolderPrincipalId)
            >= grant.MinimumStandingBand;

    /// <summary>
    /// Reports whether both relationship endpoints are registered.
    /// </summary>
    private bool PrincipalsExist(PrincipalId first, PrincipalId second) =>
        _principalIds.Contains(first) && _principalIds.Contains(second);

    /// <summary>
    /// Enforces the shared endpoint contract for public relationship queries.
    /// </summary>
    private void ValidateKnownDistinctPrincipals(PrincipalId first, PrincipalId second)
    {
        ArgumentOutOfRangeException.ThrowIfZero(first.Value);
        ArgumentOutOfRangeException.ThrowIfZero(second.Value);
        if (first == second)
        {
            throw new ArgumentException("A relationship query requires distinct principals.");
        }

        if (!PrincipalsExist(first, second))
        {
            throw new ArgumentException("Relationship query references an unknown principal.");
        }
    }

    /// <summary>
    /// Resolves the first stable proposal ordering identity.
    /// </summary>
    private static ulong ProposalPrimaryIdentity(RelationshipPolicyChangeProposal proposal) =>
        proposal switch
        {
            SetDiplomaticConditionProposal value => value.LowerPrincipalId.Value,
            IssueRelationshipGrantProposal value => value.IssuerPrincipalId.Value,
            RevokeRelationshipGrantProposal => ulong.MaxValue,
            _ => throw new InvalidOperationException("Unsupported relationship policy proposal."),
        };

    /// <summary>
    /// Resolves the second stable proposal ordering identity.
    /// </summary>
    private static ulong ProposalSecondaryIdentity(RelationshipPolicyChangeProposal proposal) =>
        proposal switch
        {
            SetDiplomaticConditionProposal value => value.UpperPrincipalId.Value,
            IssueRelationshipGrantProposal value => value.HolderPrincipalId.Value,
            RevokeRelationshipGrantProposal => ulong.MaxValue,
            _ => throw new InvalidOperationException("Unsupported relationship policy proposal."),
        };

    /// <summary>
    /// Orders closed proposal variants independently of runtime type metadata.
    /// </summary>
    private static int ProposalKindOrder(RelationshipPolicyChangeProposal proposal) =>
        proposal switch
        {
            SetDiplomaticConditionProposal => 0,
            IssueRelationshipGrantProposal => 1,
            RevokeRelationshipGrantProposal => 2,
            _ => throw new InvalidOperationException("Unsupported relationship policy proposal."),
        };

    /// <summary>
    /// Resolves the stable grant tie-breaker for proposal ordering.
    /// </summary>
    private static ulong ProposalGrantIdentity(RelationshipPolicyChangeProposal proposal) =>
        proposal switch
        {
            IssueRelationshipGrantProposal value => value.Id.Value,
            RevokeRelationshipGrantProposal value => value.Id.Value,
            _ => 0,
        };

    private static StandingChangePreparation.Resolved ResolvedRejection(
        StandingChangeBatchId batchId,
        StandingChangeRejectionReason reason) =>
        new(new StandingChangeBatchResult.Rejected(batchId, reason));

    private static RelationshipPolicyChangePreparation.Resolved PolicyRejection(
        RelationshipPolicyChangeBatchId batchId,
        RelationshipPolicyChangeRejectionReason reason) =>
        new(new RelationshipPolicyChangeBatchResult.Rejected(batchId, reason));
}

internal abstract record StandingChangePreparation
{
    private StandingChangePreparation()
    {
    }

    internal sealed record Resolved(StandingChangeBatchResult Result)
        : StandingChangePreparation;

    internal sealed record Prepared(PreparedStandingChange Value)
        : StandingChangePreparation;
}

internal sealed record PreparedStandingChange(
    StandingChangeBatchId BatchId,
    IReadOnlyList<StandingChangeProposal> Proposals,
    StandingChangeBatchResult.Applied Result,
    IReadOnlyList<StandingChangeOutcome> ChangedOutcomes);

internal sealed record CommittedStandingBatch(
    IReadOnlyList<StandingChangeProposal> Proposals,
    StandingChangeBatchResult.Applied Result);

internal abstract record RelationshipPolicyChangePreparation
{
    private RelationshipPolicyChangePreparation()
    {
    }

    internal sealed record Resolved(RelationshipPolicyChangeBatchResult Result)
        : RelationshipPolicyChangePreparation;

    internal sealed record Prepared(PreparedRelationshipPolicyChange Value)
        : RelationshipPolicyChangePreparation;
}

internal sealed record PreparedRelationshipPolicyChange(
    RelationshipPolicyChangeBatchId BatchId,
    IReadOnlyList<RelationshipPolicyChangeProposal> Proposals,
    RelationshipPolicyChangeBatchResult.Applied Result);

internal sealed record CommittedRelationshipPolicyBatch(
    IReadOnlyList<RelationshipPolicyChangeProposal> Proposals,
    RelationshipPolicyChangeBatchResult.Applied Result);

internal sealed record RelationshipGrantState(
    RelationshipGrantId Id,
    PrincipalId IssuerPrincipalId,
    PrincipalId HolderPrincipalId,
    RelationshipGrantKind Kind,
    StandingBand MinimumStandingBand,
    bool IsIssued);
