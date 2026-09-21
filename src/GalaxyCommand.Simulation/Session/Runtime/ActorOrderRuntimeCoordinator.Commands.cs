namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    private GameplayCommandHandlingResult HandleMove(
        CommandSource source,
        MoveShipCommand command)
    {
        MoveOrderEvaluation evaluation = EvaluateMove(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        MoveOrderProposal proposal = evaluation.Proposal
            ?? throw new InvalidOperationException(
                "Accepted move-order evaluation produced no proposal.");
        CommitMove(proposal, transitions, factProposals);
        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    /// <summary>
    /// Commits one group move only after every member has passed the shared
    /// stable-state preflight, preserving all-or-nothing replacement behavior.
    /// </summary>
    private GameplayCommandHandlingResult HandleGroupMove(
        CommandSource source,
        MoveShipGroupCommand command)
    {
        GroupMoveOrderEvaluation evaluation = EvaluateGroupMove(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        IReadOnlyList<MoveOrderProposal> proposals = evaluation.Proposals
            ?? throw new InvalidOperationException(
                "Accepted group move evaluation produced no proposals.");

        // Commit only after every member has read the same state. Evaluating
        // during this loop would turn a rejected one-shot command into a
        // partial replacement of earlier members' work.
        foreach (MoveOrderProposal proposal in proposals)
        {
            CommitMove(proposal, transitions, factProposals);
        }

        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    /// <summary>
    /// Applies an already-preflighted move proposal and buffers its domain
    /// effects. It must not revalidate controller or route state because a
    /// group command commits all members against one earlier stable view.
    /// </summary>
    private void CommitMove(
        MoveOrderProposal proposal,
        List<ShipOrderTransition> transitions,
        List<GameFactProposal> factProposals)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(factProposals);
        ShipOrder order = _orders.Create(
            proposal.Source,
            proposal.Destination,
            proposal.RequestedHeading);
        switch (proposal.Placement)
        {
            case OrderPlacement.ReplaceAll:
                EndActiveLocalMotion(
                    proposal.ShipId,
                    LocalMotionEndReason.ReplacedByCommand,
                    factProposals);
                _orders.ReplaceAll(
                    proposal.ShipId,
                    order,
                    transitions);
                if (proposal.Plan is { } replacementPlan)
                {
                    _orders.SetPlan(
                        proposal.ShipId,
                        order.Id,
                        replacementPlan,
                        transitions);
                }

                StartOrContinueOrders(
                    proposal.ShipId,
                    transitions,
                    factProposals);
                break;
            case OrderPlacement.Append:
                bool becameActive = _orders.Append(
                    proposal.ShipId,
                    order,
                    transitions);
                if (becameActive)
                {
                    if (proposal.Plan is { } appendedPlan)
                    {
                        _orders.SetPlan(
                            proposal.ShipId,
                            order.Id,
                            appendedPlan,
                            transitions);
                    }

                    StartOrContinueOrders(
                        proposal.ShipId,
                        transitions,
                        factProposals);
                }

                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported order placement {proposal.Placement}.");
        }
    }

    /// <summary>
    /// Evaluates and commits one ordinary order cancellation through the
    /// per-ship lifecycle, including active-motion materialization when needed.
    /// </summary>
    private GameplayCommandHandlingResult HandleCancel(
        CommandSource source,
        CancelShipOrderCommand command)
    {
        CancelOrderEvaluation evaluation = EvaluateCancel(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        CancelOrderProposal proposal = evaluation.Proposal
            ?? throw new InvalidOperationException(
                "Accepted cancel-order evaluation produced no proposal.");
        CommitCancellation(proposal, transitions, factProposals);
        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    /// <summary>
    /// Cancels every eligible selected current order after shared preflight;
    /// idle selected members remain deliberate no-ops.
    /// </summary>
    private GameplayCommandHandlingResult HandleGroupCancel(
        CommandSource source,
        CancelShipGroupCommand command)
    {
        GroupCancelOrderEvaluation evaluation = EvaluateGroupCancel(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        IReadOnlyList<CancelOrderProposal> proposals = evaluation.Proposals
            ?? throw new InvalidOperationException(
                "Accepted group cancellation evaluation produced no proposals.");
        foreach (CancelOrderProposal proposal in proposals)
        {
            CommitCancellation(proposal, transitions, factProposals);
        }

        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    /// <summary>
    /// Cancels an already-resolved current or queued order, buffering its
    /// physical and lifecycle effects. Group cancellation supplies only active
    /// proposals, while individual cancellation may also supply queued work.
    /// </summary>
    private void CommitCancellation(
        CancelOrderProposal proposal,
        List<ShipOrderTransition> transitions,
        List<GameFactProposal> factProposals)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(factProposals);
        if (proposal.WasActive)
        {
            EndActiveLocalMotion(
                proposal.ShipId,
                LocalMotionEndReason.CancelledByCommand,
                factProposals);
        }

        CancelOrderDisposition disposition =
            _orders.Cancel(
                proposal.ShipId,
                proposal.OrderId,
                transitions);
        if (disposition == CancelOrderDisposition.Missing)
        {
            throw new InvalidOperationException(
                $"Evaluated order {proposal.OrderId} disappeared before commit.");
        }

        if (disposition == CancelOrderDisposition.Active)
        {
            StartOrContinueOrders(
                proposal.ShipId,
                transitions,
                factProposals);
        }
    }

    private MoveOrderEvaluation EvaluateMove(
        CommandSource source,
        MoveShipCommand command)
    {
        if (RejectIneligible(command.ShipId, source) is { } rejection)
        {
            return new MoveOrderEvaluation(null, rejection);
        }

        SystemPosition? origin = _movement.PositionAt(
            command.ShipId,
            CurrentTime);
        if (origin is null)
        {
            if (_movement.GetState(command.ShipId)
                is not ShipSpatialState.ConnectorTransit)
            {
                throw new InvalidOperationException(
                    $"Controlled ship {command.ShipId} has no spatial state.");
            }

            return new MoveOrderEvaluation(
                new MoveOrderProposal(
                    command.ShipId,
                    source,
                    command.Destination,
                    command.Placement,
                    command.RequestedHeading,
                    null),
                null);
        }

        NavigationPlanResult result = Plan(
            command.ShipId,
            origin.Value,
            command.Destination);
        if (result is NavigationPlanResult.Unreachable unreachable)
        {
            return new MoveOrderEvaluation(
                null,
                CommandResult.Rejected(
                    CommandRejectionCodes.InvalidState,
                    $"Destination is unreachable: {unreachable.Reason}."));
        }

        TravelPlan plan = ((NavigationPlanResult.Planned)result).Plan;
        ValidateExecutablePlan(origin.Value, command.Destination, plan);
        return new MoveOrderEvaluation(
            new MoveOrderProposal(
                command.ShipId,
                source,
                command.Destination,
                command.Placement,
                command.RequestedHeading,
                plan),
            null);
    }

    /// <summary>
    /// Resolves every group member before mutation so a stale, uncontrolled, or
    /// unreachable member rejects the complete one-shot move.
    /// </summary>
    private GroupMoveOrderEvaluation EvaluateGroupMove(
        CommandSource source,
        MoveShipGroupCommand command)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(command);
        IReadOnlyList<SystemPosition> destinations = _groupMoveFormationResolver.Resolve(
            command.ShipIds,
            command.Destination);
        if (destinations.Count != command.ShipIds.Count)
        {
            return new GroupMoveOrderEvaluation(
                null,
                CommandResult.Rejected(
                    CommandRejectionCodes.InvalidIntent,
                    "The group formation resolver did not return one destination per ship."));
        }

        var proposals = new List<MoveOrderProposal>(command.ShipIds.Count);
        for (int index = 0; index < command.ShipIds.Count; index++)
        {
            MoveOrderEvaluation member = EvaluateMove(
                source,
                new MoveShipCommand(
                    command.ShipIds[index],
                    new NavigationDestination.Position(destinations[index]),
                    OrderPlacement.ReplaceAll));
            if (member.Rejection is { } rejection)
            {
                return new GroupMoveOrderEvaluation(
                    null,
                    CommandResult.Rejected(
                        rejection.RejectionCode
                            ?? CommandRejectionCodes.InvalidState,
                        $"Group move rejected for ship {command.ShipIds[index]}: {rejection.Reason}"));
            }

            proposals.Add(member.Proposal
                ?? throw new InvalidOperationException(
                    "Accepted group move member evaluation produced no proposal."));
        }

        return new GroupMoveOrderEvaluation(proposals, null);
    }

    private CancelOrderEvaluation EvaluateCancel(
        CommandSource source,
        CancelShipOrderCommand command)
    {
        if (RejectIneligible(command.ShipId, source) is { } rejection)
        {
            return new CancelOrderEvaluation(null, rejection);
        }

        if (!_orders.Contains(command.ShipId, command.OrderId))
        {
            return new CancelOrderEvaluation(
                null,
                CommandResult.Rejected(
                    CommandRejectionCodes.OrderNotFound,
                    $"Ship {command.ShipId} has no active or queued order {command.OrderId}."));
        }

        return new CancelOrderEvaluation(
            new CancelOrderProposal(
                command.ShipId,
                command.OrderId,
                _orders.IsActive(command.ShipId, command.OrderId)),
            null);
    }

    /// <summary>
    /// Validates every selected ship before collecting its current order. Idle
    /// members deliberately contribute no proposal after the shared admission
    /// succeeds, so they are no-ops rather than partial cancellation failures.
    /// </summary>
    private GroupCancelOrderEvaluation EvaluateGroupCancel(
        CommandSource source,
        CancelShipGroupCommand command)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(command);
        var proposals = new List<CancelOrderProposal>();
        foreach (ShipId shipId in command.ShipIds)
        {
            if (RejectIneligible(shipId, source) is { } rejection)
            {
                return new GroupCancelOrderEvaluation(
                    null,
                    CommandResult.Rejected(
                        rejection.RejectionCode
                            ?? CommandRejectionCodes.InvalidState,
                        $"Group cancellation rejected for ship {shipId}: {rejection.Reason}"));
            }

            ShipOrder? active = _orders.GetActive(shipId);
            if (active is not null)
            {
                proposals.Add(new CancelOrderProposal(shipId, active.Id, WasActive: true));
            }
        }

        return new GroupCancelOrderEvaluation(proposals, null);
    }

    private GameplayCommandHandlingResult HandleBeginOverride(
        CommandSource source,
        BeginScriptedOverrideCommand command)
    {
        BeginOverrideEvaluation evaluation = EvaluateBeginOverride(
            source,
            command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        BeginOverrideProposal proposal = evaluation.Proposal
            ?? throw new InvalidOperationException(
                "Accepted begin-override evaluation produced no proposal.");
        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        EndActiveLocalMotion(
            proposal.ShipId,
            LocalMotionEndReason.SuspendedByScriptedOverride,
            factProposals);
        _orders.BeginOverride(proposal.ShipId, transitions);
        _control.BeginOverride(
            proposal.ShipId,
            proposal.Source,
            proposal.Reason);
        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    private GameplayCommandHandlingResult HandleEndOverride(
        CommandSource source,
        EndScriptedOverrideCommand command)
    {
        EndOverrideEvaluation evaluation = EvaluateEndOverride(source, command);
        if (evaluation.Rejection is { } rejection)
        {
            return new GameplayCommandHandlingResult(rejection);
        }

        EndOverrideProposal proposal = evaluation.Proposal
            ?? throw new InvalidOperationException(
                "Accepted end-override evaluation produced no proposal.");
        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        EndActiveLocalMotion(
            proposal.ShipId,
            LocalMotionEndReason.ScriptedOverrideEnded,
            factProposals);
        _orders.EndOverride(
            proposal.ShipId,
            proposal.ReleasePolicy,
            transitions);
        _control.EndOverride(proposal.ShipId);
        StartOrContinueOrders(
            proposal.ShipId,
            transitions,
            factProposals);
        AddOrderTransitionProposals(transitions, factProposals);
        return new GameplayCommandHandlingResult(
            CommandResult.Accepted(),
            factProposals);
    }

    private BeginOverrideEvaluation EvaluateBeginOverride(
        CommandSource source,
        BeginScriptedOverrideCommand command)
    {
        ActorOverrideValidation validation = _control.ValidateBeginOverride(
            command.ShipId,
            source,
            command.ExpectedRevision);
        CommandResult? rejection = RejectInvalidOverride(
            validation,
            command.ShipId);
        return rejection is null
            ? new BeginOverrideEvaluation(
                new BeginOverrideProposal(
                    command.ShipId,
                    source,
                    command.Reason),
                null)
            : new BeginOverrideEvaluation(null, rejection);
    }

    private EndOverrideEvaluation EvaluateEndOverride(
        CommandSource source,
        EndScriptedOverrideCommand command)
    {
        ActorOverrideValidation validation = _control.ValidateEndOverride(
            command.ShipId,
            source,
            command.ExpectedRevision);
        CommandResult? rejection = RejectInvalidOverride(
            validation,
            command.ShipId);
        return rejection is null
            ? new EndOverrideEvaluation(
                new EndOverrideProposal(
                    command.ShipId,
                    command.ReleasePolicy),
                null)
            : new EndOverrideEvaluation(null, rejection);
    }

    /// <summary>
    /// Advances one active order until it either completes, waits for transit,
    /// or publishes exactly one bound physical movement schedule.
    /// </summary>

}
