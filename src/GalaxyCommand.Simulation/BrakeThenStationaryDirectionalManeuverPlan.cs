namespace GalaxyCommand.Simulation;

/// <summary>
/// Conservative moving-state candidate that applies precision active braking
/// to zero, then plans stationary directional or reduced-thrust travel from
/// the materialized stop without changing its position or heading.
/// </summary>
public sealed record BrakeThenStationaryDirectionalManeuverPlan
{
    private BrakeThenStationaryDirectionalManeuverPlan(
        SystemPosition destination,
        ShipHeading? requestedHeading,
        DecelerationManeuverSegment brakingPhase,
        StationaryDirectionalManeuverPlan? stationaryPlan,
        ReducedThrustTerminalManeuverPlan? reducedThrustPlan)
    {
        Destination = destination;
        RequestedHeading = requestedHeading;
        BrakingPhase = brakingPhase;
        StationaryPlan = stationaryPlan;
        ReducedThrustPlan = reducedThrustPlan;
    }

    public SystemPosition Destination { get; }

    public ShipHeading? RequestedHeading { get; }

    public DecelerationManeuverSegment BrakingPhase { get; }

    public StationaryDirectionalManeuverPlan? StationaryPlan { get; }

    public ReducedThrustTerminalManeuverPlan? ReducedThrustPlan { get; }

    public SimulationTime StartsAt => BrakingPhase.StartsAt;

    public SimulationTime BrakingEndsAt => BrakingPhase.EndsAt;

    public SimulationTime EndsAt =>
        StationaryPlan?.EndsAt
        ?? ReducedThrustPlan?.EndsAt
        ?? BrakingEndsAt;

    /// <summary>
    /// Builds one precision-brake candidate from nonzero velocity, then invokes
    /// stationary directional selection at the exact braking endpoint. Failure
    /// publishes no partial plan when the second stage remains unsupported.
    /// </summary>
    internal static bool TryCreate(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        EffectiveShipManeuverCapability capability,
        ManeuverObjective objective,
        out BrakeThenStationaryDirectionalManeuverPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A brake-then-stationary plan requires a destination in the starting system.",
                nameof(destination));
        }

        if (start.Velocity == ShipVelocity.Zero)
        {
            plan = null;
            return false;
        }

        var braking = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.ActiveBrake,
            startsAt,
            start,
            capability.PrecisionAcceleration);
        ShipKinematicState stopped = braking.StateAt(braking.EndsAt);
        StationaryDirectionalPlanSelection stationarySelection =
            StationaryDirectionalManeuverPlanner.Select(
                braking.EndsAt,
                stopped,
                destination,
                requestedHeading,
                capability,
                objective);

        StationaryDirectionalManeuverPlan? stationaryPlan =
            stationarySelection.CompletePlan;
        ReducedThrustTerminalManeuverPlan? reducedThrustPlan = null;
        if (stationarySelection.Kind
                == StationaryDirectionalPlanKind.DirectionalPlanner)
        {
            ReducedThrustTerminalManeuverPlan.TryCreateFromRest(
                braking.EndsAt,
                stopped,
                destination,
                capability.PrecisionAcceleration,
                capability.MaximumSubCruiseSpeed,
                requestedHeading,
                capability.TurnRate,
                out reducedThrustPlan);
        }

        if (stationarySelection.Kind
                == StationaryDirectionalPlanKind.DirectionalPlanner
            && reducedThrustPlan is null
            || stationarySelection.Kind
                    != StationaryDirectionalPlanKind.TerminalSettle
                && stationaryPlan is null
                && reducedThrustPlan is null)
        {
            plan = null;
            return false;
        }

        ShipKinematicState arrived = stationaryPlan is not null
            ? stationaryPlan.StateAt(stationaryPlan.EndsAt)
            : reducedThrustPlan is not null
                ? reducedThrustPlan.StateAt(reducedThrustPlan.EndsAt)
                : stopped;
        if (!ManeuverArrival.EvaluateTerminal(
                arrived,
                destination,
                requestedHeading).IsSatisfied)
        {
            plan = null;
            return false;
        }

        plan = new BrakeThenStationaryDirectionalManeuverPlan(
            destination,
            requestedHeading,
            braking,
            stationaryPlan,
            reducedThrustPlan);
        return true;
    }

    /// <summary>
    /// Evaluates braking through its inclusive endpoint, then the stationary
    /// directional stage. Times outside the complete schedule reject.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Brake-then-stationary time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        if (time <= BrakingEndsAt)
        {
            return BrakingPhase.StateAt(time);
        }

        return StationaryPlan is not null
            ? StationaryPlan.StateAt(time)
            : ReducedThrustPlan!.StateAt(time);
    }
}
