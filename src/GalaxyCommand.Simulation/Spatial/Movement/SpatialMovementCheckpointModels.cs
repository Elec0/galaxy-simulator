using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

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

    internal sealed record AnalyticManeuver : ShipSpatialStateCheckpoint
    {
        internal AnalyticManeuver(
            MotionId id,
            EventGeneration generation,
            ExecutableBoundedTerminalManeuverPlan plan,
            ManeuverObjective objective,
            int currentPhaseIndex,
            IEnumerable<int> waypointPhaseIndices,
            IEnumerable<EventKey> pendingEventKeys)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(waypointPhaseIndices);
            ArgumentNullException.ThrowIfNull(pendingEventKeys);
            Id = id;
            Generation = generation;
            Plan = plan;
            Objective = objective;
            CurrentPhaseIndex = currentPhaseIndex;
            WaypointPhaseIndices = new ReadOnlyCollection<int>(
                waypointPhaseIndices.ToArray());
            PendingEventKeys = new ReadOnlyCollection<EventKey>(
                pendingEventKeys.ToArray());
        }

        internal MotionId Id { get; }

        internal EventGeneration Generation { get; }

        internal ExecutableBoundedTerminalManeuverPlan Plan { get; }

        internal ManeuverObjective Objective { get; }

        internal int CurrentPhaseIndex { get; }

        internal ReadOnlyCollection<int> WaypointPhaseIndices { get; }

        internal ReadOnlyCollection<EventKey> PendingEventKeys { get; }
    }

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
    ShipVelocity Velocity,
    ShipHeading Heading,
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
