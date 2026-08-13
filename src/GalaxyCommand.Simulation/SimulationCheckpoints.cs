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

internal sealed record WorldSystemCheckpoint(
    SystemId Id,
    string? Name);

internal sealed record WorldConnectorEndpointCheckpoint(
    ConnectorEndpointId Id,
    SystemId SystemId,
    SpatialCoordinate X,
    SpatialCoordinate Y);

internal sealed record WorldTransitConnectionCheckpoint(
    TransitConnectionId Id,
    ConnectorEndpointId SourceEndpointId,
    ConnectorEndpointId DestinationEndpointId,
    SimulationDuration Duration);

internal sealed class WorldTopologyCheckpoint
{
    internal WorldTopologyCheckpoint(
        IEnumerable<WorldSystemCheckpoint?> systems,
        IEnumerable<WorldConnectorEndpointCheckpoint?> endpoints,
        IEnumerable<WorldTransitConnectionCheckpoint?> connections)
    {
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(connections);
        Systems = new ReadOnlyCollection<WorldSystemCheckpoint?>(systems.ToArray());
        Endpoints = new ReadOnlyCollection<WorldConnectorEndpointCheckpoint?>(
            endpoints.ToArray());
        Connections = new ReadOnlyCollection<WorldTransitConnectionCheckpoint?>(
            connections.ToArray());
    }

    internal ReadOnlyCollection<WorldSystemCheckpoint?> Systems { get; }

    internal ReadOnlyCollection<WorldConnectorEndpointCheckpoint?> Endpoints { get; }

    internal ReadOnlyCollection<WorldTransitConnectionCheckpoint?> Connections { get; }
}

internal sealed record NavigationPolicyCheckpoint(
    string? Kind,
    int BehaviorVersion);

internal sealed record TravelTimePolicyCheckpoint(
    string? Kind,
    int BehaviorVersion,
    ulong MillisecondsPerMapUnit);

internal sealed record ConstructionInputPolicyCheckpoint(
    MaterialId MaterialId,
    Quantity Quantity);

internal sealed record ShipDesignPolicyCheckpoint(
    ConstructionDesignId Id,
    string? Name,
    IReadOnlyList<ConstructionInputPolicyCheckpoint?> Inputs,
    Work RequiredWork,
    Quantity CargoCapacity);

internal sealed record MaterializationPolicyCheckpoint(
    FacilityId FacilityId,
    PrincipalId PrincipalId,
    SystemId SystemId,
    SpatialCoordinate X,
    SpatialCoordinate Y,
    ActorControllerKind BaseControllerKind,
    string? BaseControllerId,
    InitialShipOrderPolicy InitialOrderPolicy,
    IReadOnlyList<ShipDesignPolicyCheckpoint?> AllowedDesigns);

internal sealed record RuntimePolicyManifestCheckpoint(
    NavigationPolicyCheckpoint? Navigation,
    TravelTimePolicyCheckpoint? TravelTime,
    IReadOnlyList<MaterializationPolicyCheckpoint?> MaterializationPolicies,
    int FactRetentionCapacity);

internal sealed record ProductionRecipeCheckpoint(
    IReadOnlyList<ConstructionInputPolicyCheckpoint?> Inputs,
    MaterialId OutputMaterial,
    Quantity OutputQuantity,
    Work RequiredWork);

internal sealed record ProductionReservationLinkCheckpoint(
    MaterialId MaterialId,
    ReservationId ReservationId);

internal sealed record ProductionJobCheckpoint(
    ProductionJobId Id,
    ProductionRecipeCheckpoint? Recipe,
    bool IsRepeating,
    ProductionJobStatus Status,
    SimulationTime? CompletesAt,
    EventGeneration Generation,
    IReadOnlyList<ProductionReservationLinkCheckpoint?> Reservations);

internal sealed record ProductionLineCheckpoint(
    FacilityId FacilityId,
    InventoryId InventoryId,
    Throughput Throughput,
    ProductionJobId? ActiveJobId,
    IReadOnlyList<ProductionJobId> QueuedJobIds,
    IReadOnlyList<ProductionJobCheckpoint?> Jobs);

internal sealed record ProductionOwnerCheckpoint(
    IdSequenceCheckpoint JobIds,
    IReadOnlyList<ProductionLineCheckpoint?> Lines);

internal sealed record ConstructionReservationLinkCheckpoint(
    MaterialId MaterialId,
    ReservationId ReservationId);

internal sealed record ConstructionOrderCheckpoint(
    ConstructionOrderId Id,
    ConstructionDesignId DesignId,
    ConstructionOrderStatus Status,
    SimulationTime? CompletesAt,
    EventGeneration Generation,
    IReadOnlyList<ConstructionReservationLinkCheckpoint?> Reservations);

internal sealed record ConstructionShipIdentityCheckpoint(
    EntityId EntityId,
    ShipId ShipId,
    InventoryId CargoInventoryId);

internal sealed record ConstructionMaterializationReceiptCheckpoint(
    ConstructionMaterializationEffect? Effect,
    ConstructionShipIdentityCheckpoint? ShipIdentity);

internal sealed record ConstructionProcessCheckpoint(
    FacilityId FacilityId,
    InventoryId InventoryId,
    Throughput Throughput,
    ConstructionOrderId? ActiveOrderId,
    IReadOnlyList<ConstructionOrderId> QueuedOrderIds,
    IReadOnlyList<ConstructionOrderCheckpoint?> Orders,
    IReadOnlyList<ConstructionMaterializationEffect?> PendingMaterializations,
    IReadOnlyList<ConstructionMaterializationReceiptCheckpoint?> MaterializationReceipts);

internal sealed record ConstructionOwnerCheckpoint(
    IdSequenceCheckpoint OrderIds,
    IReadOnlyList<ConstructionProcessCheckpoint?> Processes);

internal sealed record RelationshipPrincipalCheckpoint(
    PrincipalId Id,
    string? ContentId,
    string? Name);

internal sealed record RelationshipStandingPolicyCheckpoint(
    string? Id,
    StandingValue Minimum,
    StandingValue Maximum,
    StandingValue Initial,
    StandingValue AdversarialThreshold,
    StandingValue NeutralThreshold,
    StandingValue FavorableThreshold,
    StandingValue AlliedThreshold);

internal sealed record RelationshipStandingCheckpoint(
    PrincipalId AssessingPrincipalId,
    PrincipalId SubjectPrincipalId,
    StandingValue Value);

internal sealed record RelationshipDiplomacyCheckpoint(
    PrincipalId LowerPrincipalId,
    PrincipalId UpperPrincipalId,
    DiplomaticCondition Condition);

internal sealed record RelationshipGrantCheckpoint(
    RelationshipGrantId Id,
    PrincipalId IssuerPrincipalId,
    PrincipalId HolderPrincipalId,
    string? Kind,
    StandingBand MinimumStandingBand,
    bool IsIssued);

internal sealed record StandingBatchReceiptCheckpoint
{
    internal StandingBatchReceiptCheckpoint(
        StandingChangeBatchId batchId,
        IEnumerable<StandingChangeProposal?> proposals,
        StandingChangeBatchResult.Applied? result)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        BatchId = batchId;
        Proposals = new ReadOnlyCollection<StandingChangeProposal?>(proposals.ToArray());
        Result = result;
    }

    internal StandingChangeBatchId BatchId { get; }

    internal IReadOnlyList<StandingChangeProposal?> Proposals { get; }

    internal StandingChangeBatchResult.Applied? Result { get; init; }
}

internal sealed record PolicyBatchReceiptCheckpoint
{
    internal PolicyBatchReceiptCheckpoint(
        RelationshipPolicyChangeBatchId batchId,
        IEnumerable<RelationshipPolicyChangeProposal?> proposals,
        RelationshipPolicyChangeBatchResult.Applied? result)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        BatchId = batchId;
        Proposals = new ReadOnlyCollection<RelationshipPolicyChangeProposal?>(
            proposals.ToArray());
        Result = result;
    }

    internal RelationshipPolicyChangeBatchId BatchId { get; }

    internal IReadOnlyList<RelationshipPolicyChangeProposal?> Proposals { get; }

    internal RelationshipPolicyChangeBatchResult.Applied? Result { get; init; }
}

internal sealed class RelationshipCheckpoint
{
    internal RelationshipCheckpoint(
        PrincipalId playerPrincipalId,
        RelationshipStandingPolicyCheckpoint? standingPolicy,
        IEnumerable<RelationshipPrincipalCheckpoint?> principals,
        IEnumerable<RelationshipStandingCheckpoint?> standings,
        IEnumerable<RelationshipDiplomacyCheckpoint?> diplomaticConditions,
        IEnumerable<RelationshipGrantCheckpoint?> grants,
        IEnumerable<StandingBatchReceiptCheckpoint?> standingReceipts,
        IEnumerable<PolicyBatchReceiptCheckpoint?> policyReceipts)
    {
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(standings);
        ArgumentNullException.ThrowIfNull(diplomaticConditions);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(standingReceipts);
        ArgumentNullException.ThrowIfNull(policyReceipts);
        PlayerPrincipalId = playerPrincipalId;
        StandingPolicy = standingPolicy;
        Principals = new ReadOnlyCollection<RelationshipPrincipalCheckpoint?>(
            principals.ToArray());
        Standings = new ReadOnlyCollection<RelationshipStandingCheckpoint?>(
            standings.ToArray());
        DiplomaticConditions = new ReadOnlyCollection<RelationshipDiplomacyCheckpoint?>(
            diplomaticConditions.ToArray());
        Grants = new ReadOnlyCollection<RelationshipGrantCheckpoint?>(grants.ToArray());
        StandingReceipts = new ReadOnlyCollection<StandingBatchReceiptCheckpoint?>(
            standingReceipts.ToArray());
        PolicyReceipts = new ReadOnlyCollection<PolicyBatchReceiptCheckpoint?>(
            policyReceipts.ToArray());
    }

    internal PrincipalId PlayerPrincipalId { get; }

    internal RelationshipStandingPolicyCheckpoint? StandingPolicy { get; }

    internal ReadOnlyCollection<RelationshipPrincipalCheckpoint?> Principals { get; }

    internal ReadOnlyCollection<RelationshipStandingCheckpoint?> Standings { get; }

    internal ReadOnlyCollection<RelationshipDiplomacyCheckpoint?>
        DiplomaticConditions
    { get; }

    internal ReadOnlyCollection<RelationshipGrantCheckpoint?> Grants { get; }

    internal ReadOnlyCollection<StandingBatchReceiptCheckpoint?> StandingReceipts { get; }

    internal ReadOnlyCollection<PolicyBatchReceiptCheckpoint?> PolicyReceipts { get; }
}

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

internal sealed record EntityLifecycleShipCheckpoint(
    EntityId EntityId,
    ShipId ShipId,
    PrincipalId PrincipalId,
    ConstructionDesignId DesignId,
    InventoryId CargoInventoryId);

internal sealed record EntityMaterializationReceiptCheckpoint(
    ConstructionMaterializationEffect? Effect,
    EntityId EntityId,
    ShipId ShipId,
    InventoryId CargoInventoryId);

internal sealed record EntityRemovalReceiptCheckpoint(
    EntityRemovalRequest? Request,
    ShipId ShipId,
    InventoryId CargoInventoryId);

internal sealed class EntityLifecycleCheckpoint
{
    internal EntityLifecycleCheckpoint(
        IdSequenceCheckpoint entityIds,
        IdSequenceCheckpoint shipIds,
        IdSequenceCheckpoint inventoryIds,
        InventoryRegistryCheckpoint inventories,
        IEnumerable<EntityLifecycleShipCheckpoint?> liveShips,
        IEnumerable<EntityMaterializationReceiptCheckpoint?> materializationReceipts,
        IEnumerable<EntityRemovalReceiptCheckpoint?> removalReceipts)
    {
        ArgumentNullException.ThrowIfNull(entityIds);
        ArgumentNullException.ThrowIfNull(shipIds);
        ArgumentNullException.ThrowIfNull(inventoryIds);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(liveShips);
        ArgumentNullException.ThrowIfNull(materializationReceipts);
        ArgumentNullException.ThrowIfNull(removalReceipts);
        EntityIds = entityIds;
        ShipIds = shipIds;
        InventoryIds = inventoryIds;
        Inventories = inventories;
        LiveShips = new ReadOnlyCollection<EntityLifecycleShipCheckpoint?>(
            liveShips.ToArray());
        MaterializationReceipts =
            new ReadOnlyCollection<EntityMaterializationReceiptCheckpoint?>(
                materializationReceipts.ToArray());
        RemovalReceipts =
            new ReadOnlyCollection<EntityRemovalReceiptCheckpoint?>(
                removalReceipts.ToArray());
    }

    internal IdSequenceCheckpoint EntityIds { get; }

    internal IdSequenceCheckpoint ShipIds { get; }

    internal IdSequenceCheckpoint InventoryIds { get; }

    internal InventoryRegistryCheckpoint Inventories { get; }

    internal ReadOnlyCollection<EntityLifecycleShipCheckpoint?> LiveShips { get; }

    internal ReadOnlyCollection<EntityMaterializationReceiptCheckpoint?>
        MaterializationReceipts
    { get; }

    internal ReadOnlyCollection<EntityRemovalReceiptCheckpoint?> RemovalReceipts { get; }
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
