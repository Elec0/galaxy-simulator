namespace GalaxyCommand.Simulation;

/// <summary>
/// Deterministic scalar accelerate, capped-travel, and brake profile for
/// aligned sub-cruise movement with a positive capped-speed interval.
/// </summary>
public sealed record CappedSpeedManeuverProfile
{
    private CappedSpeedManeuverProfile(
        ulong distanceMillimeters,
        ulong initialSpeedMillimetersPerSecond,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed,
        ulong accelerationDistanceMillimeters,
        ulong cappedTravelDistanceMillimeters,
        ulong brakingDistanceMillimeters)
    {
        DistanceMillimeters = distanceMillimeters;
        InitialSpeedMillimetersPerSecond = initialSpeedMillimetersPerSecond;
        Acceleration = acceleration;
        Braking = braking;
        MaximumSpeed = maximumSpeed;
        AccelerationDistanceMillimeters = accelerationDistanceMillimeters;
        CappedTravelDistanceMillimeters = cappedTravelDistanceMillimeters;
        BrakingDistanceMillimeters = brakingDistanceMillimeters;
    }

    public ulong DistanceMillimeters { get; }

    public ulong InitialSpeedMillimetersPerSecond { get; }

    public ManeuverAcceleration Acceleration { get; }

    public ManeuverAcceleration Braking { get; }

    public ManeuverSpeed MaximumSpeed { get; }

    public ulong AccelerationDistanceMillimeters { get; }

    public ulong CappedTravelDistanceMillimeters { get; }

    public ulong BrakingDistanceMillimeters { get; }

    /// <summary>
    /// Resolves an aligned scalar profile with a strictly positive exact
    /// capped-speed distance. Returns false when initial speed exceeds the cap
    /// or acceleration and braking consume the whole distance, leaving that
    /// boundary under triangular or prior-braking ownership. Published
    /// distances use nearest-unit rounding with half ties upward.
    /// </summary>
    public static bool TryCreate(
        ulong distanceMillimeters,
        ulong initialSpeedMillimetersPerSecond,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed,
        out CappedSpeedManeuverProfile? profile)
    {
        ArgumentOutOfRangeException.ThrowIfZero(distanceMillimeters);

        ulong cap = maximumSpeed.MillimetersPerSecond;
        if (initialSpeedMillimetersPerSecond > cap)
        {
            profile = null;
            return false;
        }

        UInt128 accelerationRate = acceleration.MillimetersPerSecondSquared;
        UInt128 brakingRate = braking.MillimetersPerSecondSquared;
        UInt128 capSquared = (UInt128)cap * cap;
        UInt128 initialSpeedSquared =
            (UInt128)initialSpeedMillimetersPerSecond
            * initialSpeedMillimetersPerSecond;
        UInt128 accelerationNumerator = capSquared - initialSpeedSquared;
        UInt128 brakingNumerator = capSquared;
        UInt128 commonDenominator = checked(
            (UInt128)2 * accelerationRate * brakingRate);
        UInt128 usedDistanceNumerator = checked(
            accelerationNumerator * brakingRate
            + brakingNumerator * accelerationRate);
        UInt128 totalDistanceNumerator = checked(
            (UInt128)distanceMillimeters * commonDenominator);

        // Equality has no capped-speed interval and remains the triangular
        // profile's exact cap-touch boundary.
        if (usedDistanceNumerator >= totalDistanceNumerator)
        {
            profile = null;
            return false;
        }

        UInt128 accelerationDistance = RoundPositiveRatio(
            accelerationNumerator,
            checked((UInt128)2 * accelerationRate));
        UInt128 brakingDistance = RoundPositiveRatio(
            brakingNumerator,
            checked((UInt128)2 * brakingRate));
        UInt128 cappedTravelDistance = RoundPositiveRatio(
            totalDistanceNumerator - usedDistanceNumerator,
            commonDenominator);
        if (accelerationDistance > ulong.MaxValue
            || brakingDistance > ulong.MaxValue
            || cappedTravelDistance > ulong.MaxValue)
        {
            throw new OverflowException(
                "A capped-speed profile distance exceeds the published range.");
        }

        profile = new CappedSpeedManeuverProfile(
            distanceMillimeters,
            initialSpeedMillimetersPerSecond,
            acceleration,
            braking,
            maximumSpeed,
            (ulong)accelerationDistance,
            (ulong)cappedTravelDistance,
            (ulong)brakingDistance);
        return true;
    }

    /// <summary>
    /// Rounds a positive exact ratio to nearest, with exact half-unit ties
    /// upward, without retaining the rational beyond profile construction.
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
