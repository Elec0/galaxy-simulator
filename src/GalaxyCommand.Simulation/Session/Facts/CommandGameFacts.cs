using System.Collections.ObjectModel;
using System.Globalization;

namespace GalaxyCommand.Simulation;

public sealed record CommandAcceptedFact : GameFact
{
    public CommandAcceptedFact(
        CommandSequence commandSequence,
        CommandSource source,
        string commandKind)
    {
        ArgumentOutOfRangeException.ThrowIfZero(commandSequence.Value);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandKind);
        CommandSequence = commandSequence;
        Source = source;
        CommandKind = commandKind;
    }

    public CommandSequence CommandSequence { get; }

    public CommandSource Source { get; }

    public string CommandKind { get; }
}

public sealed record CommandRejectedFact : GameFact
{
    public CommandRejectedFact(
        CommandSequence commandSequence,
        CommandSource source,
        string commandKind,
        CommandRejectionCode rejectionCode)
    {
        ArgumentOutOfRangeException.ThrowIfZero(commandSequence.Value);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectionCode.Value);
        CommandSequence = commandSequence;
        Source = source;
        CommandKind = commandKind;
        RejectionCode = rejectionCode;
    }

    public CommandSequence CommandSequence { get; }

    public CommandSource Source { get; }

    public string CommandKind { get; }

    public CommandRejectionCode RejectionCode { get; }
}

public sealed record ShipOrderTransitionFact : GameFact
{
    /// <summary>
    /// Captures one lifecycle transition together with the immutable terminal
    /// destination and optional final-heading intent of its order.
    /// </summary>
    public ShipOrderTransitionFact(
        ShipId shipId,
        ShipOrderId orderId,
        CommandSource source,
        NavigationDestination destination,
        ShipOrderStatus? previousStatus,
        ShipOrderStatus nextStatus,
        ShipOrderReason reason,
        ShipHeading? requestedHeading = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(orderId.Value);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (previousStatus is { } previous && !Enum.IsDefined(previous))
        {
            throw new ArgumentOutOfRangeException(
                nameof(previousStatus),
                previousStatus,
                "Unknown previous ship-order status.");
        }

        if (!Enum.IsDefined(nextStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextStatus),
                nextStatus,
                "Unknown next ship-order status.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "Unknown ship-order reason.");
        }

        ShipId = shipId;
        OrderId = orderId;
        Source = source;
        Destination = destination;
        RequestedHeading = requestedHeading;
        PreviousStatus = previousStatus;
        NextStatus = nextStatus;
        Reason = reason;
    }

    public ShipId ShipId { get; }

    public ShipOrderId OrderId { get; }

    public CommandSource Source { get; }

    public NavigationDestination Destination { get; }

    public ShipHeading? RequestedHeading { get; }

    public ShipOrderStatus? PreviousStatus { get; }

    public ShipOrderStatus NextStatus { get; }

    public ShipOrderReason Reason { get; }
}

