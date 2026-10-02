namespace GalaxyCommand.Simulation;

/// <summary>
/// Composite precision candidate that turns from rest toward the resolved
/// course before all-direction precision translation.
/// </summary>
public sealed record TurnThenPrecisionSubCruiseManeuverPlan
{
    private TurnThenPrecisionSubCruiseManeuverPlan(
        ShipHeading courseHeading,
        StationaryTurnManeuverPlan courseTurnPlan,
        PrecisionSubCruiseManeuverPlan precisionPlan)
    {
        CourseHeading = courseHeading;
        CourseTurnPlan = courseTurnPlan;
        PrecisionPlan = precisionPlan;
    }

    public ShipHeading CourseHeading { get; }

    public StationaryTurnManeuverPlan CourseTurnPlan { get; }

    public PrecisionSubCruiseManeuverPlan PrecisionPlan { get; }

    public SimulationTime StartsAt => CourseTurnPlan.StartsAt;

    public SimulationTime TranslationStartsAt => CourseTurnPlan.EndsAt;

    public SimulationTime EndsAt => PrecisionPlan.EndsAt;

    /// <summary>
    /// Builds a course turn followed by precision translation. The turn need
    /// only meet the accepted heading tolerance because precision thrust does
    /// not require an exact forward-heading projection.
    /// </summary>
    public static bool TryCreateFromRest(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        EffectiveShipManeuverCapability capability,
        out TurnThenPrecisionSubCruiseManeuverPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A turn-then-precision plan requires a destination in the starting system.",
                nameof(destination));
        }

        if (start.Velocity != ShipVelocity.Zero)
        {
            throw new ArgumentException(
                "A turn-then-precision plan requires zero starting velocity.",
                nameof(start));
        }

        if (start.Position == destination)
        {
            plan = null;
            return false;
        }

        ShipHeading course = ManeuverHeadingProjection.ResolveCourseHeading(
            checked((long)(((Int128)destination.Position.X.Units
                - start.Position.Position.X.Units) * 1_000)),
            checked((long)(((Int128)destination.Position.Y.Units
                - start.Position.Position.Y.Units) * 1_000)));
        if (!StationaryTurnManeuverPlan.TryCreate(
                startsAt,
                start,
                start.Position,
                course,
                capability.TurnRate,
                out StationaryTurnManeuverPlan? courseTurn)
            || courseTurn is null)
        {
            plan = null;
            return false;
        }

        ShipKinematicState turned = courseTurn.StateAt(courseTurn.EndsAt);
        if (!PrecisionSubCruiseManeuverPlan.TryCreateFromRest(
                courseTurn.EndsAt,
                turned,
                destination,
                capability,
                out PrecisionSubCruiseManeuverPlan? precision)
            || precision is null)
        {
            plan = null;
            return false;
        }

        plan = new TurnThenPrecisionSubCruiseManeuverPlan(
            course,
            courseTurn,
            precision);
        return true;
    }

    /// <summary>
    /// Evaluates the inclusive course turn followed by its precision
    /// translation and rejects timestamps outside the combined schedule.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time)
    {
        if (time < StartsAt || time > EndsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"Turn-then-precision time must be within {StartsAt.Milliseconds} through {EndsAt.Milliseconds} ms.");
        }

        return time <= TranslationStartsAt
            ? CourseTurnPlan.StateAt(time)
            : PrecisionPlan.StateAt(time);
    }
}
