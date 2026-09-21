namespace GalaxyCommand.Simulation;

/// <summary>
/// Deterministic scalar switch profile for a collinear terminal move that
/// accelerates at most once and then brakes without capped-speed travel.
/// </summary>
public sealed record ShortMoveTriangularProfile
{
    private ShortMoveTriangularProfile(
        ulong distanceMillimeters,
        ulong initialSpeedMillimetersPerSecond,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed,
        bool beginsWithBraking,
        ulong switchDistanceMillimeters,
        ulong switchSpeedMillimetersPerSecond)
    {
        DistanceMillimeters = distanceMillimeters;
        InitialSpeedMillimetersPerSecond = initialSpeedMillimetersPerSecond;
        Acceleration = acceleration;
        Braking = braking;
        MaximumSpeed = maximumSpeed;
        BeginsWithBraking = beginsWithBraking;
        SwitchDistanceMillimeters = switchDistanceMillimeters;
        SwitchSpeedMillimetersPerSecond = switchSpeedMillimetersPerSecond;
    }

    public ulong DistanceMillimeters { get; }

    public ulong InitialSpeedMillimetersPerSecond { get; }

    public ManeuverAcceleration Acceleration { get; }

    public ManeuverAcceleration Braking { get; }

    public ManeuverSpeed MaximumSpeed { get; }

    public bool BeginsWithBraking { get; }

    public ulong SwitchDistanceMillimeters { get; }

    public ulong SwitchSpeedMillimetersPerSecond { get; }

    /// <summary>
    /// Resolves the unique accelerate-to-brake switch for a positive scalar
    /// distance from rest. Returns false when the exact switch speed exceeds
    /// the supplied cap because that candidate requires another profile.
    /// Published switch values use nearest-unit rounding with half ties up.
    /// Arithmetic outside the checked fixed-point envelope is rejected.
    /// </summary>
    public static bool TryCreateFromRest(
        ulong distanceMillimeters,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed,
        out ShortMoveTriangularProfile? profile) =>
        TryCreate(
            distanceMillimeters,
            initialSpeedMillimetersPerSecond: 0,
            acceleration,
            braking,
            maximumSpeed,
            out profile);

    /// <summary>
    /// Resolves the unique accelerate-to-brake switch for a positive scalar
    /// distance and an initial speed already aligned toward the destination.
    /// A speed at or beyond the switch produces an immediate-braking profile,
    /// including when that speed exceeds the current cap. Other candidates
    /// return false when reaching their switch would exceed the cap.
    /// </summary>
    public static bool TryCreate(
        ulong distanceMillimeters,
        ulong initialSpeedMillimetersPerSecond,
        ManeuverAcceleration acceleration,
        ManeuverAcceleration braking,
        ManeuverSpeed maximumSpeed,
        out ShortMoveTriangularProfile? profile)
    {
        ArgumentOutOfRangeException.ThrowIfZero(distanceMillimeters);

        UInt128 accelerationRate = acceleration.MillimetersPerSecondSquared;
        UInt128 brakingRate = braking.MillimetersPerSecondSquared;
        UInt128 rateSum = accelerationRate + brakingRate;
        UInt128 initialSpeedSquared =
            (UInt128)initialSpeedMillimetersPerSecond
            * initialSpeedMillimetersPerSecond;
        UInt128 brakingBoundary = checked(
            (UInt128)2 * brakingRate * distanceMillimeters);
        bool beginsWithBraking = initialSpeedSquared >= brakingBoundary;
        if (beginsWithBraking)
        {
            profile = new ShortMoveTriangularProfile(
                distanceMillimeters,
                initialSpeedMillimetersPerSecond,
                acceleration,
                braking,
                maximumSpeed,
                beginsWithBraking: true,
                switchDistanceMillimeters: 0,
                switchSpeedMillimetersPerSecond:
                    initialSpeedMillimetersPerSecond);
            return true;
        }

        UInt128 switchDistanceNumerator =
            brakingBoundary - initialSpeedSquared;
        UInt128 switchDistanceDenominator = checked((UInt128)2 * rateSum);
        UInt128 switchSpeedSquaredNumerator = checked(
            brakingRate
            * checked(
                (UInt128)2 * distanceMillimeters * accelerationRate
                + initialSpeedSquared));

        if (ExceedsCap(
                switchSpeedSquaredNumerator,
                rateSum,
                maximumSpeed.MillimetersPerSecond))
        {
            profile = null;
            return false;
        }

        UInt128 roundedSwitchDistance = RoundPositiveRatio(
            switchDistanceNumerator,
            switchDistanceDenominator);
        if (roundedSwitchDistance > ulong.MaxValue)
        {
            throw new OverflowException(
                "The triangular switch distance exceeds the published range.");
        }

        ulong switchSpeed = RoundSquareRootRatio(
            switchSpeedSquaredNumerator,
            rateSum);
        profile = new ShortMoveTriangularProfile(
            distanceMillimeters,
            initialSpeedMillimetersPerSecond,
            acceleration,
            braking,
            maximumSpeed,
            beginsWithBraking: false,
            (ulong)roundedSwitchDistance,
            switchSpeed);
        return true;
    }

    /// <summary>
    /// Compares the exact rational switch speed squared with the integral cap
    /// while avoiding an unnecessary overflowing cap-side product.
    /// </summary>
    private static bool ExceedsCap(
        UInt128 numerator,
        UInt128 denominator,
        ulong maximumSpeed)
    {
        UInt128 capSquared = (UInt128)maximumSpeed * maximumSpeed;
        if (capSquared > UInt128.MaxValue / denominator)
        {
            return false;
        }

        return numerator > capSquared * denominator;
    }

    /// <summary>
    /// Rounds the square root of a positive exact ratio directly to the nearest
    /// integer, with exact half-unit ties upward.
    /// </summary>
    private static ulong RoundSquareRootRatio(
        UInt128 numerator,
        UInt128 denominator)
    {
        UInt128 quotient = numerator / denominator;
        UInt128 maximumSquare = (UInt128)ulong.MaxValue * ulong.MaxValue;
        if (quotient > maximumSquare)
        {
            throw new OverflowException(
                "The triangular switch speed exceeds the published range.");
        }

        ulong floor = IntegerSquareRoot(quotient);
        UInt128 midpointBase = (UInt128)floor * floor + floor;
        UInt128 remainder = numerator % denominator;
        bool roundUp = quotient > midpointBase
            || quotient == midpointBase && remainder * 4 >= denominator;
        return roundUp ? checked(floor + 1) : floor;
    }

    /// <summary>
    /// Finds the greatest unsigned integer whose square does not exceed the
    /// supplied bounded value without using floating-point arithmetic.
    /// </summary>
    private static ulong IntegerSquareRoot(UInt128 value)
    {
        ulong low = 0;
        ulong high = ulong.MaxValue;
        ulong result = 0;
        while (low <= high)
        {
            ulong midpoint = low + ((high - low) >> 1);
            UInt128 squared = (UInt128)midpoint * midpoint;
            if (squared <= value)
            {
                result = midpoint;
                if (midpoint == ulong.MaxValue)
                {
                    break;
                }

                low = midpoint + 1;
            }
            else
            {
                high = midpoint - 1;
            }
        }

        return result;
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
