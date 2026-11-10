namespace GalaxyCommand.Simulation;

public abstract record GameEventKind
{
    private GameEventKind()
    {
    }

    public sealed record SpatialMovement(SpatialMovementEvent Event) : GameEventKind;

    public sealed record Economic(EconomicEvent Event) : GameEventKind;

    public sealed record SpatialInteraction(InteractionCandidate Candidate) : GameEventKind;
    public sealed record SensorActionReevaluation(int ActorCount) : GameEventKind;
    public sealed record CargoExpiry(EntityId ObjectId) : GameEventKind;
    public sealed record CargoCollectionCompletion(ShipId ShipId, ShipOrderId OrderId) : GameEventKind;
    public sealed record CargoCollectionRangeExit(ShipId ShipId, ShipOrderId OrderId) : GameEventKind;
    public sealed record CargoCollectionReevaluation(int ActorCount) : GameEventKind;
}

public sealed record GameEventRecord(
    SimulationTime Timestamp,
    EventPhase Phase,
    ulong CreationSequence,
    EventGeneration Generation,
    ScheduledEventDisposition Disposition,
    GameEventKind Kind);

internal abstract record GameEvent
{
    private GameEvent()
    {
    }

    internal sealed record SpatialMovement(SpatialMovementEvent Event) : GameEvent;

    internal sealed record Economic(EconomicEvent Event) : GameEvent;

    internal sealed record SpatialInteraction(InteractionForecast Forecast) : GameEvent;
    internal sealed record SensorActionReevaluation(IReadOnlyList<SensorActionWake> Work) : GameEvent;
    internal sealed record CargoExpiry(EntityId ObjectId) : GameEvent;
    internal sealed record CargoCollectionCompletion(ShipId ShipId, ShipOrderId OrderId, EventGeneration AttemptGeneration) : GameEvent;
    internal sealed record CargoCollectionRangeExit(ShipId ShipId, ShipOrderId OrderId, EventGeneration AttemptGeneration) : GameEvent;
    internal sealed record CargoCollectionReevaluation(IReadOnlyList<CargoCollectionWake> Work) : GameEvent;
}

internal sealed record PreparedMovementCancellation(
    EventKey EventKey,
    EventGeneration Generation,
    GameEvent Event);

/// <summary>
/// Fixed persistent coordinator for actor commands, orders, movement, spatial
/// events, and their semantic facts.
/// </summary>
