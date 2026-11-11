namespace GalaxyCommand.Simulation;

/// <summary>Validates saved selected instructions at their structural boundaries without selecting a replacement plan.</summary>
internal static class SelectedManeuverCheckpointValidation
{
    private sealed record Interval(SimulationTime StartsAt, SimulationTime EndsAt,
        ShipKinematicState Start, ShipKinematicState End);

    /// <summary>Checks every concrete payload, including future phase joins that the current-state check cannot see.</summary>
    internal static void Validate(ExecutableManeuverPayload payload, ExecutableBoundedTerminalManeuverPlan restored)
    {
        switch (payload)
        {
            case ExecutableManeuverPayload.TerminalSettle settle:
                Require(settle.State.Position.SystemId.Value != 0 && settle.State.Velocity == ShipVelocity.Zero);
                break;
            case ExecutableManeuverPayload.PassiveDrag drag:
                Require(drag.Plan.Kind == ManeuverDecelerationKind.PassiveDrag && drag.Plan.TargetSpeedMillimetersPerSecond == 0);
                Inspect(drag.Plan);
                break;
            case ExecutableManeuverPayload.Turn item: Inspect(item.Plan); break;
            case ExecutableManeuverPayload.Directional item: Inspect(item.Plan); break;
            case ExecutableManeuverPayload.MovingAligned item: Inspect(item.Plan); break;
            case ExecutableManeuverPayload.Cruise item: Inspect(item.Plan); break;
            case ExecutableManeuverPayload.BrakeThenDirectional item: Inspect(item.Plan); break;
            case ExecutableManeuverPayload.ReducedThrust item: Inspect(item.Plan); break;
            case ExecutableManeuverPayload.ImmediateBraking item: Inspect(item.Plan); break;
            case ExecutableManeuverPayload.Triangular item: Inspect(item.Plan); break;
            case ExecutableManeuverPayload.CappedSpeed item: Inspect(item.Plan); break;
            case ExecutableManeuverPayload.ForcedDropout item:
                Require(item.DropoutPhase.Kind == ManeuverDecelerationKind.ActiveBrake);
                // RestoreSelected already checks the exact child join; the child was admitted once by RestorePayload.
                Inspect(item.DropoutPhase);
                Arrival(From(restored), item.Destination, item.RequestedHeading);
                break;
            case ExecutableManeuverPayload.WaypointRoute item:
                Require(item.Transitions.Count > 0);
                Interval route = Inspect(item.Transitions[0]);
                foreach (AnalyticManeuverSegment segment in item.Transitions.Skip(1)) route = Join(route, Inspect(segment));
                // WaypointRoute checks the final child join while constructing the isolated result.
                Require(route.StartsAt == restored.StartsAt && route.EndsAt <= restored.EndsAt);
                break;
            default: throw Invalid();
        }
    }

    /// <summary>Traverses only the admitted concrete plan vocabulary and requires exact phase-state continuity.</summary>
    private static Interval Inspect(object? value)
    {
        switch (value)
        {
            case AnalyticManeuverSegment phase:
                Require(phase.EndsAt > phase.StartsAt && phase.Start.Position.SystemId.Value != 0);
                return new(phase.StartsAt, phase.EndsAt, phase.Start, phase.StateAt(phase.EndsAt));
            case DecelerationManeuverSegment phase:
                Require(Enum.IsDefined(phase.Kind) && phase.Deceleration.MillimetersPerSecondSquared > 0
                    && phase.InitialSpeedMillimetersPerSecond == ManeuverVector.SpeedMagnitude(phase.Start.Velocity)
                    && phase.TargetSpeedMillimetersPerSecond < phase.InitialSpeedMillimetersPerSecond);
                return new(phase.StartsAt, phase.EndsAt, phase.Start, phase.StateAt(phase.EndsAt));
            case StationaryTurnManeuverPlan plan:
                Interval turn = Inspect(plan.TurnPhase);
                Require(plan.TurnPhase.Acceleration == ShipAcceleration.Zero && turn.Start.Velocity == ShipVelocity.Zero
                    && turn.Start.Position == plan.Destination && plan.TurnPhase.AngularRate.MillidegreesPerSecond != 0);
                return Arrival(turn, plan.Destination, plan.RequestedHeading);
            case ShortMoveImmediateBrakingPlan plan:
                Interval immediate = Brake(plan.BrakingPhase);
                Require(plan.CompletesTerminalArrival == ManeuverArrival.EvaluateTerminal(immediate.End, plan.Destination, null).IsSatisfied);
                return immediate;
            case ShortMoveTriangularPlan plan:
                Interval triangular = Join(Inspect(plan.AccelerationPhase), Brake(plan.BrakingPhase));
                ShortMoveTriangularProfile profile = plan.Profile;
                Require(plan.AccelerationPhase.AngularRate == ShipAngularRate.Zero && profile is not null && !profile.BeginsWithBraking && profile.DistanceMillimeters > 0
                    && profile.Acceleration.MillimetersPerSecondSquared > 0 && profile.Braking == plan.BrakingPhase.Deceleration
                    && profile.MaximumSpeed.MillimetersPerSecond > 0 && profile.SwitchDistanceMillimeters <= profile.DistanceMillimeters
                    && profile.InitialSpeedMillimetersPerSecond == ManeuverVector.SpeedMagnitude(triangular.Start.Velocity)
                    && profile.SwitchSpeedMillimetersPerSecond >= profile.InitialSpeedMillimetersPerSecond
                    && profile.SwitchSpeedMillimetersPerSecond <= profile.MaximumSpeed.MillimetersPerSecond
                    && ManeuverVector.SpeedMagnitude(plan.AccelerationPhase.StateAt(plan.SwitchesAt).Velocity) <= profile.MaximumSpeed.MillimetersPerSecond);
                return Arrival(triangular, plan.Destination, null);
            case CappedSpeedManeuverPlan plan:
                Interval travel = Inspect(plan.CappedTravelPhase);
                Require(plan.CappedTravelPhase.Acceleration == ShipAcceleration.Zero && plan.CappedTravelPhase.AngularRate == ShipAngularRate.Zero);
                Interval capped = plan.AccelerationPhase is null ? travel : Join(Inspect(plan.AccelerationPhase), travel);
                capped = Join(capped, Brake(plan.BrakingPhase));
                CappedSpeedManeuverProfile cap = plan.Profile;
                Require((plan.AccelerationPhase is null || plan.AccelerationPhase.AngularRate == ShipAngularRate.Zero)
                    && cap is not null && cap.DistanceMillimeters > 0 && cap.CappedTravelDistanceMillimeters > 0
                    && cap.MaximumSpeed.MillimetersPerSecond > 0 && cap.Acceleration.MillimetersPerSecondSquared > 0
                    && cap.Braking == plan.BrakingPhase.Deceleration
                    && cap.InitialSpeedMillimetersPerSecond == ManeuverVector.SpeedMagnitude(capped.Start.Velocity)
                    && cap.InitialSpeedMillimetersPerSecond <= cap.MaximumSpeed.MillimetersPerSecond
                    && ManeuverVector.SpeedMagnitude(travel.Start.Velocity) == cap.MaximumSpeed.MillimetersPerSecond);
                return Arrival(capped, plan.Destination, null);
            case AlignedSubCruisePlanSelection selection:
                Require(selection.SettledState is null && Count(selection.ImmediateBrakingPlan, selection.TriangularPlan, selection.CappedSpeedPlan) == 1);
                return selection.Kind switch
                {
                    AlignedSubCruisePlanKind.ImmediateBraking => Inspect(selection.ImmediateBrakingPlan),
                    AlignedSubCruisePlanKind.Triangular => Inspect(selection.TriangularPlan),
                    AlignedSubCruisePlanKind.CappedSpeed => Inspect(selection.CappedSpeedPlan),
                    _ => throw Invalid(),
                };
            case PrimarySubCruiseManeuverPlan plan:
                Require(plan.PrimaryAcceleration.MillimetersPerSecondSquared > 0 && plan.BrakingAcceleration.MillimetersPerSecondSquared > 0);
                Interval primary = Inspect(plan.TranslationPlan);
                Require(primary.Start.Velocity == ShipVelocity.Zero && primary.Start.Heading == plan.CourseHeading);
                return Arrival(primary, plan.Destination, null);
            case PrecisionSubCruiseManeuverPlan plan:
                Require(plan.PrecisionAcceleration.MillimetersPerSecondSquared > 0);
                Interval precision = Inspect(plan.TranslationPlan);
                Require(precision.Start.Velocity == ShipVelocity.Zero);
                return Arrival(precision, plan.Destination, null);
            case TurnThenSubCruiseManeuverPlan plan:
                Require(plan.CourseTurnPlan.RequestedHeading == plan.CourseHeading);
                return Arrival(Join(Inspect(plan.CourseTurnPlan), Inspect(plan.TranslationPlan)), plan.Destination, null);
            case TurnThenPrecisionSubCruiseManeuverPlan plan:
                Require(plan.CourseTurnPlan.RequestedHeading == plan.CourseHeading);
                return Join(Inspect(plan.CourseTurnPlan), Inspect(plan.PrecisionPlan));
            case StationaryDirectionalManeuverPlan plan:
                Require(Count(plan.PrimarySubCruisePlan, plan.TurnThenPrimaryPlan, plan.TurnThenPrecisionPlan, plan.PrecisionSubCruisePlan) == 1);
                Interval directional = plan.Kind switch
                {
                    StationaryDirectionalPlanKind.PrimarySubCruise => Inspect(plan.PrimarySubCruisePlan),
                    StationaryDirectionalPlanKind.TurnThenPrimary => Inspect(plan.TurnThenPrimaryPlan),
                    StationaryDirectionalPlanKind.TurnThenPrecisionSubCruise => Inspect(plan.TurnThenPrecisionPlan),
                    StationaryDirectionalPlanKind.PrecisionSubCruise => Inspect(plan.PrecisionSubCruisePlan),
                    _ => throw Invalid(),
                };
                return Arrival(AppendTurn(directional, plan.FinalTurnPlan), plan.Destination, plan.RequestedHeading);
            case MovingAlignedTerminalManeuverPlan plan:
                return Arrival(AppendTurn(Inspect(plan.TranslationPlan), plan.FinalTurnPlan), plan.Destination, plan.RequestedHeading);
            case BrakeThenStationaryDirectionalManeuverPlan plan:
                Require(Count(plan.StationaryPlan, plan.ReducedThrustPlan) <= 1);
                Interval brakeThen = Brake(plan.BrakingPhase);
                if (((object?)plan.StationaryPlan ?? plan.ReducedThrustPlan) is { } continuationPlan)
                    brakeThen = Join(brakeThen, Inspect(continuationPlan));
                return Arrival(brakeThen, plan.Destination, plan.RequestedHeading);
            case ReducedThrustTerminalManeuverPlan plan:
                Interval reduced = Join(Inspect(plan.AccelerationPhase), Inspect(plan.BrakingPhase));
                Require(reduced.Start.Velocity == ShipVelocity.Zero && plan.AccelerationPhase.AngularRate == ShipAngularRate.Zero
                    && plan.BrakingPhase.AngularRate == ShipAngularRate.Zero
                    && plan.AccelerationPhase.EndsAt.Milliseconds - plan.AccelerationPhase.StartsAt.Milliseconds
                        == plan.BrakingPhase.EndsAt.Milliseconds - plan.BrakingPhase.StartsAt.Milliseconds
                    && (Int128)plan.AccelerationPhase.Acceleration.MillimetersPerSecondSquaredX
                        == -(Int128)plan.BrakingPhase.Acceleration.MillimetersPerSecondSquaredX
                    && (Int128)plan.AccelerationPhase.Acceleration.MillimetersPerSecondSquaredY
                        == -(Int128)plan.BrakingPhase.Acceleration.MillimetersPerSecondSquaredY);
                return Arrival(AppendTurn(reduced, plan.FinalTurnPlan), plan.Destination, plan.RequestedHeading);
            case CruiseTerminalManeuverPlan plan:
                Interval spool = Inspect(plan.MovingSpoolPhase);
                Require(plan.MovingSpoolPhase.Acceleration == ShipAcceleration.Zero && plan.MovingSpoolPhase.AngularRate == ShipAngularRate.Zero
                    && plan.CruisePhase.Acceleration == ShipAcceleration.Zero && plan.CruisePhase.AngularRate == ShipAngularRate.Zero);
                Interval cruise = plan.AccelerationPhase is null ? spool : Join(Inspect(plan.AccelerationPhase), spool);
                if (plan.CourseTurnPlan is not null) cruise = Join(Inspect(plan.CourseTurnPlan), cruise);
                // Cruise entry/dropout intentionally change velocity; position and heading still join exactly.
                Interval fast = Inspect(plan.CruisePhase);
                Require(spool.Start.Heading == plan.CourseHeading && fast.Start.Heading == plan.CourseHeading
                    && ManeuverVector.SpeedMagnitude(fast.Start.Velocity) > ManeuverVector.SpeedMagnitude(spool.Start.Velocity));
                Interval braking = Brake(plan.BrakingPhase);
                Require(braking.Start.Velocity == spool.Start.Velocity);
                cruise = Join(Join(cruise, fast, allowVelocityChange: true), braking, allowVelocityChange: true);
                return Arrival(AppendTurn(cruise, plan.FinalTurnPlan), plan.Destination, plan.RequestedHeading);
            default: throw Invalid();
        }
    }

    /// <summary>Requires active braking to exact rest, distinct from passive drag and bounded cruise dropout.</summary>
    private static Interval Brake(DecelerationManeuverSegment phase)
    {
        Require(phase.Kind == ManeuverDecelerationKind.ActiveBrake && phase.TargetSpeedMillimetersPerSecond == 0);
        return Inspect(phase);
    }

    /// <summary>Appends an optional already-selected turn at the exact stopped state.</summary>
    private static Interval AppendTurn(Interval interval, StationaryTurnManeuverPlan? turn) =>
        turn is null ? interval : Join(interval, Inspect(turn));

    /// <summary>Checks inclusive endpoint equality; only documented cruise boundaries may replace velocity.</summary>
    private static Interval Join(Interval first, Interval second, bool allowVelocityChange = false)
    {
        Require(first.EndsAt == second.StartsAt && first.End.Position == second.Start.Position
            && first.End.Heading == second.Start.Heading && (allowVelocityChange || first.End.Velocity == second.Start.Velocity));
        return new(first.StartsAt, second.EndsAt, first.Start, second.End);
    }

    /// <summary>Requires the accepted no-snap terminal tolerances and a valid same-system destination.</summary>
    private static Interval Arrival(Interval interval, SystemPosition destination, ShipHeading? heading)
    {
        Require(destination.SystemId.Value != 0 && interval.Start.Position.SystemId == destination.SystemId
            && ManeuverArrival.EvaluateTerminal(interval.End, destination, heading).IsSatisfied);
        return interval;
    }

    private static Interval From(ExecutableBoundedTerminalManeuverPlan plan) =>
        new(plan.StartsAt, plan.EndsAt, plan.StateAt(plan.StartsAt), plan.StateAt(plan.EndsAt));

    /// <summary>Counts populated union arms so hidden unused payloads cannot bypass validation.</summary>
    private static int Count(params object?[] values) => values.Count(value => value is not null);

    /// <summary>Returns one bounded diagnostic without echoing externally supplied checkpoint values.</summary>
    private static void Require(bool valid)
    {
        if (!valid) throw Invalid();
    }

    private static ArgumentException Invalid() => new("A selected maneuver checkpoint has inconsistent analytic instructions.");
}
