namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    private sealed record MoveOrderProposal(
        ShipId ShipId,
        CommandSource Source,
        NavigationDestination Destination,
        OrderPlacement Placement,
        ShipHeading? RequestedHeading,
        TravelPlan? Plan);

    private sealed record MoveOrderEvaluation(
        MoveOrderProposal? Proposal,
        CommandResult? Rejection);

    private sealed record GroupMoveOrderEvaluation(
        IReadOnlyList<MoveOrderProposal>? Proposals,
        CommandResult? Rejection);

    private readonly record struct CancelOrderProposal(
        ShipId ShipId,
        ShipOrderId OrderId,
        bool WasActive);

    private sealed record CancelOrderEvaluation(
        CancelOrderProposal? Proposal,
        CommandResult? Rejection);

    private sealed record GroupCancelOrderEvaluation(
        IReadOnlyList<CancelOrderProposal>? Proposals,
        CommandResult? Rejection);

    private sealed record BeginOverrideProposal(
        ShipId ShipId,
        CommandSource Source,
        ActorOverrideReasonId Reason);

    private sealed record BeginOverrideEvaluation(
        BeginOverrideProposal? Proposal,
        CommandResult? Rejection);

    private sealed record EndOverrideProposal(
        ShipId ShipId,
        ScriptedOverrideReleasePolicy ReleasePolicy);

    private sealed record EndOverrideEvaluation(
        EndOverrideProposal? Proposal,
        CommandResult? Rejection);
}
