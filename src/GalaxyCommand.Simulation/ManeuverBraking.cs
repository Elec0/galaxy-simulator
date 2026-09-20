namespace GalaxyCommand.Simulation;

/// <summary>
/// Deterministic stopping-distance and latest-safe-braking policy shared by
/// terminal approaches and short-move planning.
/// </summary>
public static class ManeuverBraking
{
    /// <summary>
    /// Evaluates the constant-deceleration stopping-distance relation and
    /// publishes integer millimeters with normal rounding. A target speed at
    /// or above the current speed requires no braking distance.
    /// </summary>
    public static ulong RequiredDistanceMillimeters(
        ShipVelocity currentVelocity,
        ShipVelocity targetVelocity,
        ManeuverAcceleration deceleration)
    {
        ulong currentSpeed = ManeuverVector.SpeedMagnitude(currentVelocity);
        ulong targetSpeed = ManeuverVector.SpeedMagnitude(targetVelocity);
        if (currentSpeed <= targetSpeed)
        {
            return 0;
        }

        (UInt128 numerator, UInt128 denominator) = DistanceTerms(
            currentSpeed,
            targetSpeed,
            deceleration);
        UInt128 rounded = RoundPositiveRatio(numerator, denominator);
        if (rounded > ulong.MaxValue)
        {
            throw new OverflowException(
                "The required braking distance exceeds the published range.");
        }

        return (ulong)rounded;
    }

    /// <summary>
    /// Returns whether braking must begin at the current distance to reach the
    /// target speed within the supplied arrival tolerance. The comparison
    /// uses the exact stopping-distance ratio rather than its published
    /// rounded diagnostic value, and equality begins braking.
    /// </summary>
    public static bool ShouldBegin(
        ShipVelocity currentVelocity,
        ShipVelocity targetVelocity,
        ManeuverAcceleration deceleration,
        ulong remainingDistanceMillimeters,
        ulong arrivalToleranceMillimeters)
    {
        ulong currentSpeed = ManeuverVector.SpeedMagnitude(currentVelocity);
        ulong targetSpeed = ManeuverVector.SpeedMagnitude(targetVelocity);
        if (currentSpeed <= targetSpeed)
        {
            return false;
        }

        (UInt128 numerator, UInt128 denominator) = DistanceTerms(
            currentSpeed,
            targetSpeed,
            deceleration);
        ulong usableDistance = remainingDistanceMillimeters
            > arrivalToleranceMillimeters
                ? remainingDistanceMillimeters - arrivalToleranceMillimeters
                : 0;

        // Comparing the quotient floor with an integer distance preserves the
        // exact >= boundary without multiplying two potentially wide values.
        return numerator / denominator >= usableDistance;
    }

    /// <summary>
    /// Produces the numerator and denominator of
    /// (currentSpeed^2 - targetSpeed^2) / (2 * deceleration).
    /// </summary>
    private static (UInt128 Numerator, UInt128 Denominator) DistanceTerms(
        ulong currentSpeed,
        ulong targetSpeed,
        ManeuverAcceleration deceleration) =>
        (
            (UInt128)currentSpeed * currentSpeed
                - (UInt128)targetSpeed * targetSpeed,
            (UInt128)2 * deceleration.MillimetersPerSecondSquared);

    /// <summary>
    /// Rounds a positive exact ratio to nearest, with exact half-unit ties
    /// upward, while retaining the wide intermediate only within evaluation.
    /// </summary>
    private static UInt128 RoundPositiveRatio(
        UInt128 numerator,
        UInt128 denominator)
    {
        UInt128 quotient = numerator / denominator;
        UInt128 remainder = numerator % denominator;
        UInt128 half = denominator / 2;
        return remainder > half
            || denominator % 2 == 0 && remainder == half
                ? quotient + 1
                : quotient;
    }
}
