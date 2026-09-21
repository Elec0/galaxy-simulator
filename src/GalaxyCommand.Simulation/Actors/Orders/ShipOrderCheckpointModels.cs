using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record ShipOrderCheckpoint(
    ShipOrderId Id,
    CommandSource? Source,
    NavigationDestination? Destination,
    ShipOrderStatus? Status,
    ShipOrderReason? Reason,
    TravelPlan? Plan,
    int NextLegIndex,
    MotionId? MotionId,
    ConnectorTransitId? TransitId,
    ShipHeading? RequestedHeading = null);

internal sealed class ShipOrderWorkSetCheckpoint
{
    internal ShipOrderWorkSetCheckpoint(
        ShipOrderCheckpoint? Active,
        IEnumerable<ShipOrderCheckpoint?> Queue,
        ShipOrderCheckpoint? LastTerminal)
    {
        ArgumentNullException.ThrowIfNull(Queue);
        this.Active = Active;
        this.Queue = new ReadOnlyCollection<ShipOrderCheckpoint?>(
            Queue.ToArray());
        this.LastTerminal = LastTerminal;
    }

    internal ShipOrderCheckpoint? Active { get; }

    internal ReadOnlyCollection<ShipOrderCheckpoint?> Queue { get; }

    internal ShipOrderCheckpoint? LastTerminal { get; }
}

internal sealed record ShipActorOrdersCheckpoint(
    ShipId ShipId,
    ShipOrderWorkSetCheckpoint? Base,
    ShipOrderWorkSetCheckpoint? Override);

internal sealed class ShipOrderCoordinatorCheckpoint
{
    internal ShipOrderCoordinatorCheckpoint(
        IdSequenceCheckpoint orderIds,
        IEnumerable<ShipActorOrdersCheckpoint?> actors)
    {
        ArgumentNullException.ThrowIfNull(orderIds);
        ArgumentNullException.ThrowIfNull(actors);
        OrderIds = orderIds;
        Actors = new ReadOnlyCollection<ShipActorOrdersCheckpoint?>(
            actors.ToArray());
    }

    internal IdSequenceCheckpoint OrderIds { get; }

    internal ReadOnlyCollection<ShipActorOrdersCheckpoint?> Actors { get; }
}

