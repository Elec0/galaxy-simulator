namespace GalaxyCommand.Simulation;

/// <summary>
/// One active-braking phase selected when aligned velocity is already at or
/// beyond a short move's deterministic switch boundary.
/// </summary>
public sealed record ShortMoveImmediateBrakingPlan
{
    private const long MillimetersPerMeter = 1_000;

    private ShortMoveImmediateBrakingPlan(
        SystemPosition destination,
        DecelerationManeuverSegment brakingPhase,
        bool completesTerminalArrival)
    {
        Destination = destination;
        BrakingPhase = brakingPhase;
        CompletesTerminalArrival = completesTerminalArrival;
    }

    public SystemPosition Destination { get; }

    public DecelerationManeuverSegment BrakingPhase { get; }

    public bool CompletesTerminalArrival { get; }

    public SimulationTime StartsAt => BrakingPhase.StartsAt;

    public SimulationTime EndsAt => BrakingPhase.EndsAt;

    /// <summary>
    /// Builds an immediate active-braking phase for velocity exactly aligned
    /// toward a same-system destination. Returns false when arrival is already
    /// satisfied, the ship is stationary or unaligned, or acceleration remains
    /// legal before the switch. Zero-distance excess velocity always brakes.
    /// A false <see cref="CompletesTerminalArrival"/> result tells the caller to
    /// replan from the unavoidable stopped state.
    /// </summary>
    public static bool TryCreateAligned(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed,
        out ShortMoveImmediateBrakingPlan? plan)
    {
        if (start.Position.SystemId != destination.SystemId)
        {
            throw new ArgumentException(
                "An immediate-braking plan requires a destination in the starting system.",
                nameof(destination));
        }

        if (ManeuverArrival.EvaluateTerminal(start, destination, null).IsSatisfied
            || start.Velocity == ShipVelocity.Zero)
        {
            plan = null;
            return false;
        }

        long deltaX = DeltaMillimeters(
            start.Position.Position.X,
            destination.Position.X);
        long deltaY = DeltaMillimeters(
            start.Position.Position.Y,
            destination.Position.Y);
        if (deltaX != 0 || deltaY != 0)
        {
            if (!IsAlignedToward(deltaX, deltaY, start.Velocity))
            {
                plan = null;
                return false;
            }

            ulong distance = ManeuverVector.SpeedMagnitude(
                new ShipVelocity(deltaX, deltaY));
            ulong initialSpeed = ManeuverVector.SpeedMagnitude(start.Velocity);
            if (!ShortMoveTriangularProfile.TryCreate(
                    distance,
                    initialSpeed,
                    acceleration,
                    braking,
                    maximumSpeed,
                    out ShortMoveTriangularProfile? candidateProfile)
                || candidateProfile is not { BeginsWithBraking: true })
            {
                plan = null;
                return false;
            }
        }

        var brakingPhase = new DecelerationManeuverSegment(
            ManeuverDecelerationKind.ActiveBrake,
            startsAt,
            start,
            braking);
        ShipKinematicState stopped = brakingPhase.StateAt(brakingPhase.EndsAt);
        bool completesTerminalArrival = ManeuverArrival.EvaluateTerminal(
            stopped,
            destination,
            requestedHeading: null).IsSatisfied;
        plan = new ShortMoveImmediateBrakingPlan(
            destination,
            brakingPhase,
            completesTerminalArrival);
        return true;
    }

    /// <summary>
    /// Evaluates the immutable active-braking phase. The owned segment rejects
    /// times outside the inclusive scheduled boundaries.
    /// </summary>
    public ShipKinematicState StateAt(SimulationTime time) =>
        BrakingPhase.StateAt(time);

    private static long DeltaMillimeters(
        SpatialCoordinate start,
        SpatialCoordinate destination) =>
        checked((long)(
            ((Int128)destination.Units - start.Units)
            * MillimetersPerMeter));

    /// <summary>
    /// Requires exact collinearity and a positive dot product so this narrow
    /// plan never invents lateral correction or a turn policy.
    /// </summary>
    private static bool IsAlignedToward(
        long deltaX,
        long deltaY,
        ShipVelocity velocity)
    {
        Int128 cross = checked(
            (Int128)deltaX * velocity.MillimetersPerSecondY
            - (Int128)deltaY * velocity.MillimetersPerSecondX);
        Int128 dot = checked(
            (Int128)deltaX * velocity.MillimetersPerSecondX
            + (Int128)deltaY * velocity.MillimetersPerSecondY);
        return cross == 0 && dot > 0;
    }
}
