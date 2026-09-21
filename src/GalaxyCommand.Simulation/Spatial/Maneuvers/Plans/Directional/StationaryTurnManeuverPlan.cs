namespace GalaxyCommand.Simulation;

/// <summary>
/// One analytic zero-distance turn from exact rest toward a requested terminal
/// heading. Translation and moving turns remain owned by directional planning.
/// </summary>
public sealed record StationaryTurnManeuverPlan
{
    private const ulong MillisecondsPerSecond = 1_000;

    private StationaryTurnManeuverPlan(
        SystemPosition destination,
        ShipHeading requestedHeading,
        AnalyticManeuverSegment turnPhase)
    {
        Destination = destination;
        RequestedHeading = requestedHeading;
        TurnPhase = turnPhase;
    }

    public SystemPosition Destination { get; }

    public ShipHeading RequestedHeading { get; }

    public AnalyticManeuverSegment TurnPhase { get; }

    public SimulationTime StartsAt => TurnPhase.StartsAt;

    public SimulationTime EndsAt => TurnPhase.EndsAt;

    /// <summary>
    /// Builds a shortest-arc turn at the effective turn-rate cap when the ship
    /// is exactly stationary at the same-system destination. A state already
    /// within terminal tolerance or requiring translation returns false.
    /// </summary>
    public static bool TryCreate(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading requestedHeading,
        ManeuverTurnRate turnRate,
        out StationaryTurnManeuverPlan? plan)
    {
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "A stationary turn requires a destination in the starting system.",
                nameof(destination));
        }

        if (start.Position != destination
            || start.Velocity != ShipVelocity.Zero
            || ManeuverArrival.EvaluateTerminal(
                start,
                destination,
                requestedHeading).IsSatisfied)
        {
            plan = null;
            return false;
        }

        int signedTurn = start.Heading.ShortestTurnTo(requestedHeading);
        long rateMagnitude = checked((long)turnRate.MillidegreesPerSecond);
        var angularRate = new ShipAngularRate(
            signedTurn > 0
                ? rateMagnitude
                : checked(-rateMagnitude));
        SimulationDuration duration = RoundedDuration(
            (ulong)Math.Abs(signedTurn),
            turnRate.MillidegreesPerSecond);
        var turnPhase = new AnalyticManeuverSegment(
            startsAt,
            startsAt.Add(duration),
            start,
            ShipAcceleration.Zero,
            angularRate);

        // Endpoint validation keeps extreme authored rates from turning the
        // minimum one-millisecond phase into an invalid terminal candidate.
        if (!ManeuverArrival.EvaluateTerminal(
                turnPhase.StateAt(turnPhase.EndsAt),
                destination,
                requestedHeading).IsSatisfied)
        {
            plan = null;
            return false;
        }

        plan = new StationaryTurnManeuverPlan(
            destination,
            requestedHeading,
            turnPhase);
        return true;
    }

    /// <summary>
    /// Evaluates the immutable turn at an inclusive scheduled time. The
    /// underlying analytic phase rejects times outside the plan.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time) =>
        TurnPhase.StateAt(time);

    /// <summary>
    /// Converts angular distance and rate to milliseconds using normal
    /// rounding. A positive sub-millisecond turn retains a schedulable phase.
    /// </summary>
    private static SimulationDuration RoundedDuration(
        ulong distanceMillidegrees,
        ulong rateMillidegreesPerSecond)
    {
        UInt128 numerator =
            (UInt128)distanceMillidegrees * MillisecondsPerSecond;
        UInt128 denominator = rateMillidegreesPerSecond;
        UInt128 quotient = numerator / denominator;
        UInt128 remainder = numerator % denominator;
        UInt128 half = denominator / 2;
        if (remainder > half
            || denominator % 2 == 0 && remainder == half)
        {
            quotient++;
        }

        if (quotient > ulong.MaxValue)
        {
            throw new OverflowException(
                "A stationary turn duration exceeds simulation range.");
        }

        return new SimulationDuration(Math.Max(1, (ulong)quotient));
    }
}
