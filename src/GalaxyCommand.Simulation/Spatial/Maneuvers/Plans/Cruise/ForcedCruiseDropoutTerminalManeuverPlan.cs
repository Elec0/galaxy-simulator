namespace GalaxyCommand.Simulation;

/// <summary>
/// Complete terminal maneuver after an unplanned cruise interruption. It
/// preserves cruise velocity at the trigger, brakes to maximum sub-cruise
/// speed at twice effective primary acceleration, then delegates the remaining
/// destination work to the ordinary bounded planner.
/// </summary>
public sealed record ForcedCruiseDropoutTerminalManeuverPlan
{
    private ForcedCruiseDropoutTerminalManeuverPlan(
        SystemPosition destination,
        ShipHeading? requestedHeading,
        DecelerationManeuverSegment dropoutPhase,
        ExecutableBoundedTerminalManeuverPlan continuationPlan)
    {
        Destination = destination;
        RequestedHeading = requestedHeading;
        DropoutPhase = dropoutPhase;
        ContinuationPlan = continuationPlan;
    }

    public SystemPosition Destination { get; }

    public ShipHeading? RequestedHeading { get; }

    public DecelerationManeuverSegment DropoutPhase { get; }

    public ExecutableBoundedTerminalManeuverPlan ContinuationPlan { get; }

    public SimulationTime StartsAt => DropoutPhase.StartsAt;

    public SimulationTime DropoutEndsAt => DropoutPhase.EndsAt;

    public SimulationTime EndsAt => ContinuationPlan.EndsAt;

    /// <summary>
    /// Creates the forced-dropout phase only from exact authored cruise speed.
    /// The post-dropout state must admit a complete ordinary continuation;
    /// otherwise no partial plan is published.
    /// </summary>
    public static bool TryCreate(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        EffectiveShipManeuverCapability capability,
        ManeuverObjective objective,
        out ForcedCruiseDropoutTerminalManeuverPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A forced cruise dropout requires a destination in the starting system.",
                nameof(destination));
        }

        if (capability.CruiseSpeed.MillimetersPerSecond
                <= capability.MaximumSubCruiseSpeed.MillimetersPerSecond
            || ManeuverVector.SpeedMagnitude(start.Velocity)
            != capability.CruiseSpeed.MillimetersPerSecond)
        {
            plan = null;
            return false;
        }

        ulong dropoutRate = checked(
            capability.PrimaryAcceleration.MillimetersPerSecondSquared * 2);
        var dropout = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.ActiveBrake,
            startsAt,
            start,
            new ManeuverAcceleration(dropoutRate),
            capability.MaximumSubCruiseSpeed);
        ShipKinematicState atSubCruise = dropout.StateAt(dropout.EndsAt);
        BoundedTerminalPlanSelection continuation =
            BoundedTerminalManeuverPlanner.Select(
                dropout.EndsAt,
                atSubCruise,
                destination,
                requestedHeading,
                capability,
                objective);
        if (continuation.ExecutablePlan is not { } executable)
        {
            plan = null;
            return false;
        }

        plan = new ForcedCruiseDropoutTerminalManeuverPlan(
            destination,
            requestedHeading,
            dropout,
            executable);
        return true;
    }

    /// <summary>
    /// Evaluates the dedicated dropout brake through its inclusive endpoint,
    /// then the complete ordinary continuation. Times outside the combined
    /// schedule reject through the owning phase evaluator.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Forced cruise dropout time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        return time <= DropoutEndsAt
            ? DropoutPhase.StateAt(time)
            : ContinuationPlan.StateAt(time);
    }
}
