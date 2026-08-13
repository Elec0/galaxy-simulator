using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

internal sealed record CheckpointValidationFailure(
    string Path,
    string Message);

internal sealed class CheckpointResult<T>
    where T : class
{
    private CheckpointResult(T? value, CheckpointValidationFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    internal T? Value { get; }

    internal CheckpointValidationFailure? Failure { get; }

    internal bool IsSuccess => Value is not null;

    internal static CheckpointResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new CheckpointResult<T>(value, null);
    }

    internal static CheckpointResult<T> Rejected(
        CheckpointValidationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new CheckpointResult<T>(null, failure);
    }
}

internal sealed class EventAgendaCheckpoint<TEvent>
{
    internal EventAgendaCheckpoint(
        SimulationTime currentTime,
        ulong nextCreationSequence,
        IEnumerable<ScheduledEvent<TEvent>> pendingEvents)
    {
        ArgumentNullException.ThrowIfNull(pendingEvents);
        CurrentTime = currentTime;
        NextCreationSequence = nextCreationSequence;
        PendingEvents = new ReadOnlyCollection<ScheduledEvent<TEvent>>(
            pendingEvents.ToArray());
    }

    internal SimulationTime CurrentTime { get; }

    internal ulong NextCreationSequence { get; }

    internal ReadOnlyCollection<ScheduledEvent<TEvent>> PendingEvents { get; }
}

internal sealed class SimulationEngineCheckpoint<TEvent>
{
    internal SimulationEngineCheckpoint(
        bool isInitialized,
        SimulationTime accruedThrough,
        EventAgendaCheckpoint<TEvent> agenda)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        IsInitialized = isInitialized;
        AccruedThrough = accruedThrough;
        Agenda = agenda;
    }

    internal bool IsInitialized { get; }

    internal SimulationTime AccruedThrough { get; }

    internal EventAgendaCheckpoint<TEvent> Agenda { get; }
}

internal sealed record IdSequenceCheckpoint(ulong? NextValue);

internal sealed record CommandAdmissionCheckpoint(
    IdSequenceCheckpoint Sequences,
    SimulationTime? LastSubmittedAt);

internal sealed record ActorControlCheckpoint(
    ShipId ShipId,
    ActorController? BaseController,
    ActorController? TemporaryOverride,
    ActorOverrideReasonId? TemporaryOverrideReason,
    ActorControlRevision Revision);

internal sealed class ActorControlRegistryCheckpoint
{
    internal ActorControlRegistryCheckpoint(
        IEnumerable<ActorControlCheckpoint?> actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        Actors = new ReadOnlyCollection<ActorControlCheckpoint?>(
            actors.ToArray());
    }

    internal ReadOnlyCollection<ActorControlCheckpoint?> Actors { get; }
}

internal sealed record ShipOrderCheckpoint(
    ShipOrderId Id,
    CommandSource? Source,
    NavigationDestination? Destination,
    ShipOrderStatus? Status,
    ShipOrderReason? Reason,
    TravelPlan? Plan,
    int NextLegIndex,
    MotionId? MotionId,
    ConnectorTransitId? TransitId);

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

internal sealed class GameFactStoreCheckpoint
{
    internal GameFactStoreCheckpoint(
        int capacity,
        IdSequenceCheckpoint sequences,
        IEnumerable<GameFactEnvelope?> retainedFacts)
    {
        ArgumentNullException.ThrowIfNull(sequences);
        ArgumentNullException.ThrowIfNull(retainedFacts);
        Capacity = capacity;
        Sequences = sequences;
        RetainedFacts = new ReadOnlyCollection<GameFactEnvelope?>(
            retainedFacts.ToArray());
    }

    internal int Capacity { get; }

    internal IdSequenceCheckpoint Sequences { get; }

    internal ReadOnlyCollection<GameFactEnvelope?> RetainedFacts { get; }
}

internal sealed record InventoryMaterialCheckpoint(
    MaterialId MaterialId,
    Quantity Quantity);

internal sealed class InventoryCheckpoint
{
    internal InventoryCheckpoint(
        InventoryId id,
        Quantity capacity,
        IEnumerable<InventoryMaterialCheckpoint> storedMaterials,
        IEnumerable<Reservation> reservations,
        IEnumerable<CapacityReservation> capacityReservations)
    {
        ArgumentNullException.ThrowIfNull(storedMaterials);
        ArgumentNullException.ThrowIfNull(reservations);
        ArgumentNullException.ThrowIfNull(capacityReservations);
        Id = id;
        Capacity = capacity;
        StoredMaterials = new ReadOnlyCollection<InventoryMaterialCheckpoint>(
            storedMaterials.ToArray());
        Reservations = new ReadOnlyCollection<Reservation>(
            reservations.ToArray());
        CapacityReservations = new ReadOnlyCollection<CapacityReservation>(
            capacityReservations.ToArray());
    }

    internal InventoryId Id { get; }

    internal Quantity Capacity { get; }

    internal ReadOnlyCollection<InventoryMaterialCheckpoint> StoredMaterials { get; }

    internal ReadOnlyCollection<Reservation> Reservations { get; }

    internal ReadOnlyCollection<CapacityReservation> CapacityReservations { get; }
}

internal sealed class InventoryRegistryCheckpoint
{
    internal InventoryRegistryCheckpoint(
        IEnumerable<InventoryCheckpoint> inventories)
    {
        ArgumentNullException.ThrowIfNull(inventories);
        Inventories = new ReadOnlyCollection<InventoryCheckpoint>(
            inventories.ToArray());
    }

    internal ReadOnlyCollection<InventoryCheckpoint> Inventories { get; }
}

internal abstract record ShipSpatialStateCheckpoint
{
    private ShipSpatialStateCheckpoint()
    {
    }

    internal sealed record AtPosition(SystemPosition Position)
        : ShipSpatialStateCheckpoint;

    internal sealed record LocalMotion(
        MotionId Id,
        EventGeneration Generation,
        SystemPosition Origin,
        SystemPosition Destination,
        SimulationTime DepartedAt,
        SimulationTime ArrivesAt,
        EventKey? CompletionEventKey)
        : ShipSpatialStateCheckpoint;

    internal sealed record ConnectorTransit(
        ConnectorTransitId Id,
        EventGeneration Generation,
        TransitConnectionId ConnectionId,
        SystemPosition Source,
        SystemPosition Destination,
        SimulationTime DepartedAt,
        SimulationTime ArrivesAt,
        EventKey? CompletionEventKey)
        : ShipSpatialStateCheckpoint;
}

internal sealed record SpatialActorCheckpoint(
    ShipId ShipId,
    EventGeneration Generation,
    ShipSpatialStateCheckpoint State);

internal sealed class SpatialMovementCheckpoint
{
    internal SpatialMovementCheckpoint(
        IdSequenceCheckpoint motionIds,
        IdSequenceCheckpoint transitIds,
        IEnumerable<SpatialActorCheckpoint> actors)
    {
        ArgumentNullException.ThrowIfNull(motionIds);
        ArgumentNullException.ThrowIfNull(transitIds);
        ArgumentNullException.ThrowIfNull(actors);
        MotionIds = motionIds;
        TransitIds = transitIds;
        Actors = new ReadOnlyCollection<SpatialActorCheckpoint>(
            actors.ToArray());
    }

    internal IdSequenceCheckpoint MotionIds { get; }

    internal IdSequenceCheckpoint TransitIds { get; }

    internal ReadOnlyCollection<SpatialActorCheckpoint> Actors { get; }
}
