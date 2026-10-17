namespace GalaxyCommand.Simulation;

/// <summary>Immutable participant state and movement-owner forecast revision.</summary>
public sealed record InteractionSpatialParticipant(ShipSpatialSnapshot Spatial, SpatialRevision Revision,
    LocalMotionCompletionReceipt? Completion = null,
    ExecutableBoundedTerminalManeuverPlan? ManeuverPlan = null,
    IReadOnlyList<ManeuverBoundaryReceipt>? ManeuverReceipts = null,
    IReadOnlyList<InteractionMotionCompletionReceipt>? InteractionReceipts = null);

/// <summary>Movement-owner proof that an exact analytic boundary applied uninterrupted at this timestamp.</summary>
public sealed record ManeuverBoundaryReceipt(MotionId MotionId, EventGeneration Generation,
    int PhaseIndex, ExecutableBoundedTerminalManeuverPlan Plan, SpatialRevision PreviousRevision,
    SpatialRevision CompletedRevision, SimulationTime Timestamp);

/// <summary>
/// Exact evidence that an uninterrupted linear trajectory completed at this
/// timestamp, retained through subsequent commits at the same timestamp.
/// </summary>
public sealed record LocalMotionCompletionReceipt(LocalMotionSnapshot Motion,
    SpatialRevision PreviousRevision, SpatialRevision CompletedRevision, SimulationTime Timestamp);

/// <summary>
/// Stable evaluation input captured between movement-owner commits. It exposes
/// no mutation authority and retains connector transit as a nonlocal state.
/// </summary>
public sealed class InteractionSpatialReadView
{
    private readonly Dictionary<ShipId, InteractionSpatialParticipant> _participants;

    internal InteractionSpatialReadView(SimulationTime timestamp,
        IEnumerable<InteractionSpatialParticipant> participants)
    {
        Timestamp = timestamp;
        _participants = participants.ToDictionary(participant => participant.Spatial.ShipId);
    }

    public SimulationTime Timestamp { get; }

    internal IEnumerable<InteractionSpatialParticipant> Participants => _participants.Values;

    /// <summary>Returns immutable participant state, or null for an absent ship.</summary>
    public InteractionSpatialParticipant? Get(ShipId shipId) => _participants.GetValueOrDefault(shipId);
}
