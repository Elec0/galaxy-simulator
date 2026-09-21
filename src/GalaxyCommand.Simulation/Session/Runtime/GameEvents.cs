namespace GalaxyCommand.Simulation;

public abstract record GameEventKind
{
    private GameEventKind()
    {
    }

    public sealed record SpatialMovement(SpatialMovementEvent Event) : GameEventKind;

    public sealed record Economic(EconomicEvent Event) : GameEventKind;
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
}

internal sealed record PreparedMovementCancellation(
    EventKey EventKey,
    EventGeneration Generation,
    GameEvent Event);

/// <summary>
/// Fixed persistent coordinator for actor commands, orders, movement, spatial
/// events, and their semantic facts.
/// </summary>
