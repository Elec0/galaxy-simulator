namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    /// <summary>
    /// Opaque same-command preparation. It is bound to its runtime and completed
    /// timestamp, consumed once, and never retained in a conversation or checkpoint.
    /// </summary>
    internal sealed class PreparedDialogueMove
    {
        internal PreparedDialogueMove(ActorOrderRuntimeCoordinator owner,
            SimulationTime preparedAt, MoveOrderProposal? proposal,
            CommandResult result, bool needsEconomicWake)
        {
            Owner = owner;
            PreparedAt = preparedAt;
            Proposal = proposal;
            Result = result;
            NeedsEconomicWake = needsEconomicWake;
        }

        internal ActorOrderRuntimeCoordinator Owner { get; }
        internal SimulationTime PreparedAt { get; }
        internal MoveOrderProposal? Proposal { get; }
        internal bool NeedsEconomicWake { get; }
        internal bool WasCommitted { get; set; }
        internal CommandResult Result { get; }
    }

    /// <summary>
    /// Validates an ordinary move against the current stable state without
    /// accruing stations, allocating sequences, or publishing effects. An accepted
    /// token must be committed inside the same admitted dialogue command before
    /// any other authoritative mutation; it is not a deferred work item.
    /// </summary>
    internal PreparedDialogueMove PrepareDialogueMove(CommandSource source, MoveShipCommand command)
    {
        ThrowIfUnhealthy();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(command);
        bool needsEconomicWake = CommandNeedsEconomicWake(command);
        if (needsEconomicWake && !CanRequestEconomicReevaluation())
            return new PreparedDialogueMove(this, CurrentTime, null,
                CommandResult.Rejected(new("economic-wake-capacity-exhausted"),
                    "The economic reevaluation agenda identity is exhausted."), needsEconomicWake);

        MoveOrderEvaluation evaluated = EvaluateMove(source, command);
        return new PreparedDialogueMove(this, CurrentTime, evaluated.Proposal,
            evaluated.Rejection ?? CommandResult.Accepted(), needsEconomicWake);
    }

    /// <summary>
    /// Applies an immediately prepared move and ordinary owner reconciliation,
    /// buffering facts for the original dialogue command cause. No command is
    /// submitted recursively. A failed prepared commit poisons the aggregate.
    /// </summary>
    internal IReadOnlyList<GameFactProposal> CommitDialogueMove(PreparedDialogueMove prepared)
    {
        ThrowIfUnhealthy();
        ArgumentNullException.ThrowIfNull(prepared);
        try
        {
            if (!ReferenceEquals(prepared.Owner, this) || prepared.WasCommitted
                || prepared.PreparedAt != CurrentTime || prepared.Proposal is null
                || prepared.Result.Status != CommandResultStatus.Accepted)
                throw new InvalidOperationException("The dialogue move preparation is not an unused accepted token for this boundary.");

            prepared.WasCommitted = true;
            _stations?.AccrueTo(CurrentTime);
            var transitions = new List<ShipOrderTransition>();
            var facts = new List<GameFactProposal>();
            CommitMove(prepared.Proposal, transitions, facts);
            AddOrderTransitionProposals(transitions, facts);
            if (prepared.NeedsEconomicWake) RequestEconomicReevaluation();

            // Keep the ordinary move's dependent action-owner cleanup. These
            // effects belong to the same atomic admission as its order change.
            foreach (ShipId ship in facts.Select(proposal => proposal.Fact).OfType<ShipOrderTransitionFact>()
                .Select(fact => fact.ShipId).Distinct().OrderBy(ship => ship.Value))
            {
                SynchronizeSensorInterest(ship);
                SynchronizeCargoInterest(ship);
            }
            FlushSensorReevaluations();
            FlushCargoReevaluations();
            _stations?.Reconcile(CurrentTime);
            return facts.AsReadOnly();
        }
        catch
        {
            _isPoisoned = true;
            throw;
        }
    }

    /// <summary>Blocks future authority and checkpoints if the coordinated dialogue publication fails after move commit.</summary>
    internal void PoisonDialogueCommit() => _isPoisoned = true;
}
