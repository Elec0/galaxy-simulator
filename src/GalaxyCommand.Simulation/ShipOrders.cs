namespace GalaxyCommand.Simulation;

public enum ShipOrderStatus
{
    Active,
    Completed,
    Cancelled,
}

public enum ShipOrderReason
{
    MovingToDestination,
    DestinationReached,
    CancelledByCommand,
}

public sealed record ShipOrderSnapshot(
    ShipOrderId Id,
    NavigationDestination Destination,
    ShipOrderStatus Status,
    ShipOrderReason Reason);

internal sealed class ShipOrderBook
{
    private readonly SortedDictionary<ShipId, OrderState> _current =
        new(EntityIdComparer<ShipId>.Instance);
    private readonly IdSequence<ShipOrderId> _ids = new();

    internal ShipOrderId AllocateId() => _ids.Allocate();

    internal void Start(
        ShipId shipId,
        ShipOrderId orderId,
        NavigationDestination destination,
        MotionId motionId)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _current[shipId] = new OrderState(
            orderId,
            destination,
            ShipOrderStatus.Active,
            ShipOrderReason.MovingToDestination,
            motionId);
    }

    internal void CompleteImmediately(
        ShipId shipId,
        ShipOrderId orderId,
        NavigationDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _current[shipId] = new OrderState(
            orderId,
            destination,
            ShipOrderStatus.Completed,
            ShipOrderReason.DestinationReached,
            null);
    }

    internal bool Cancel(ShipId shipId)
    {
        if (!_current.TryGetValue(shipId, out OrderState? current)
            || current.Status != ShipOrderStatus.Active)
        {
            return false;
        }

        _current[shipId] = current with
        {
            Status = ShipOrderStatus.Cancelled,
            Reason = ShipOrderReason.CancelledByCommand,
            MotionId = null,
        };
        return true;
    }

    internal void CompleteMovement(ShipId shipId, MotionId motionId)
    {
        if (!_current.TryGetValue(shipId, out OrderState? current)
            || current.Status != ShipOrderStatus.Active
            || current.MotionId != motionId)
        {
            throw new InvalidOperationException(
                $"Ship {shipId} has no active order for completed motion {motionId}.");
        }

        _current[shipId] = current with
        {
            Status = ShipOrderStatus.Completed,
            Reason = ShipOrderReason.DestinationReached,
            MotionId = null,
        };
    }

    internal ShipOrderSnapshot? Capture(ShipId shipId)
    {
        if (!_current.TryGetValue(shipId, out OrderState? current))
        {
            return null;
        }

        return new ShipOrderSnapshot(
            current.Id,
            current.Destination,
            current.Status,
            current.Reason);
    }

    private sealed record OrderState(
        ShipOrderId Id,
        NavigationDestination Destination,
        ShipOrderStatus Status,
        ShipOrderReason Reason,
        MotionId? MotionId);
}
