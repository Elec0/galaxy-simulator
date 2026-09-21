namespace GalaxyCommand.Simulation;

/// <summary>
/// Monotonic identity of one ship's effective maneuver capability. The base
/// design starts at zero; TASK-068 equipment commits will advance it.
/// </summary>
public readonly record struct ShipManeuverCapabilityRevision(ulong Value)
{
    public static ShipManeuverCapabilityRevision Initial { get; } = new(0);

    public ShipManeuverCapabilityRevision Next() =>
        new(checked(Value + 1));
}

/// <summary>
/// Positive fixed-point speed magnitude used by authoritative maneuver policy.
/// </summary>
public readonly record struct ManeuverSpeed
{
    /// <summary>Creates a positive speed from already-resolved fixed-point units.</summary>
    public ManeuverSpeed(ulong millimetersPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfZero(millimetersPerSecond);
        MillimetersPerSecond = millimetersPerSecond;
    }

    public ulong MillimetersPerSecond { get; }

    /// <summary>
    /// Resolves a positive invariant meters-per-second string with no more than
    /// three fractional digits. Invalid or inexact input is rejected.
    /// </summary>
    public static ManeuverSpeed ParseMetersPerSecond(string value) =>
        new(ManeuverFixedPoint.ParsePositiveMilliUnits(value));
}

/// <summary>
/// Positive fixed-point acceleration magnitude used by authoritative maneuver policy.
/// </summary>
public readonly record struct ManeuverAcceleration
{
    /// <summary>Creates a positive acceleration from already-resolved fixed-point units.</summary>
    public ManeuverAcceleration(ulong millimetersPerSecondSquared)
    {
        ArgumentOutOfRangeException.ThrowIfZero(millimetersPerSecondSquared);
        MillimetersPerSecondSquared = millimetersPerSecondSquared;
    }

    public ulong MillimetersPerSecondSquared { get; }

    /// <summary>
    /// Resolves a positive invariant meters-per-second-squared string with no
    /// more than three fractional digits. Invalid or inexact input is rejected.
    /// </summary>
    public static ManeuverAcceleration ParseMetersPerSecondSquared(string value) =>
        new(ManeuverFixedPoint.ParsePositiveMilliUnits(value));
}

/// <summary>
/// Positive fixed-point angular-rate magnitude used by authoritative turning.
/// </summary>
public readonly record struct ManeuverTurnRate
{
    /// <summary>Creates a positive turn rate from already-resolved fixed-point units.</summary>
    public ManeuverTurnRate(ulong millidegreesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfZero(millidegreesPerSecond);
        MillidegreesPerSecond = millidegreesPerSecond;
    }

    public ulong MillidegreesPerSecond { get; }

    /// <summary>
    /// Resolves a positive invariant degrees-per-second string with no more
    /// than three fractional digits. Invalid or inexact input is rejected.
    /// </summary>
    public static ManeuverTurnRate ParseDegreesPerSecond(string value) =>
        new(ManeuverFixedPoint.ParsePositiveMilliUnits(value));
}

/// <summary>
/// Authoritative signed system-local velocity in integer millimeters per
/// simulated second.
/// </summary>
public readonly record struct ShipVelocity(
    long MillimetersPerSecondX,
    long MillimetersPerSecondY)
{
    public static ShipVelocity Zero { get; } = new(0, 0);
}

/// <summary>
/// Canonical authoritative ship heading where zero points east and increasing
/// values rotate clockwise.
/// </summary>
public readonly record struct ShipHeading
{
    public const uint MillidegreesPerRevolution = 360_000;

    public static ShipHeading Zero { get; } = new(0);

    /// <summary>
    /// Creates a canonical heading. Exactly one full revolution is accepted as
    /// authored-equivalent zero; larger values are rejected rather than wrapped.
    /// </summary>
    public ShipHeading(uint millidegrees)
    {
        if (millidegrees > MillidegreesPerRevolution)
        {
            throw new ArgumentOutOfRangeException(
                nameof(millidegrees),
                millidegrees,
                "Heading cannot exceed one full revolution.");
        }

        Millidegrees = millidegrees == MillidegreesPerRevolution
            ? 0
            : millidegrees;
    }

    public uint Millidegrees { get; }

    /// <summary>
    /// Resolves invariant authored degrees with no more than three fractional
    /// digits and canonicalizes 360 degrees to zero.
    /// </summary>
    public static ShipHeading ParseDegrees(string value)
    {
        ulong millidegrees = ManeuverFixedPoint.ParseNonNegativeMilliUnits(value);
        if (millidegrees > MillidegreesPerRevolution)
        {
            throw new FormatException("Heading cannot exceed 360 degrees.");
        }

        return new ShipHeading((uint)millidegrees);
    }

    /// <summary>
    /// Returns the shortest signed turn to the target, where positive is
    /// clockwise. An exact half-revolution tie is always clockwise.
    /// </summary>
    public int ShortestTurnTo(ShipHeading target)
    {
        int clockwise = (int)((target.Millidegrees + MillidegreesPerRevolution - Millidegrees)
            % MillidegreesPerRevolution);
        return clockwise <= MillidegreesPerRevolution / 2
            ? clockwise
            : clockwise - (int)MillidegreesPerRevolution;
    }
}

/// <summary>
/// Immutable authored maneuver inputs for one ship design. Derived directional
/// rates and effective equipment-adjusted capability belong to later resolution.
/// </summary>
public sealed record ShipManeuverCapability
{
    public const int CurrentBehaviorVersion = 1;

    /// <summary>
    /// Creates the validated base capability without inventing a passive-rate
    /// override when content omits it.
    /// </summary>
    public ShipManeuverCapability(
        ulong baseMassKilograms,
        ManeuverAcceleration baseAcceleration,
        ManeuverAcceleration? customPassiveDeceleration,
        ManeuverSpeed maximumSubCruiseSpeed,
        ManeuverSpeed cruiseSpeed,
        ManeuverTurnRate turnRate,
        SimulationDuration movingSpoolDuration)
    {
        ArgumentOutOfRangeException.ThrowIfZero(baseMassKilograms);
        Validate(baseAcceleration, nameof(baseAcceleration));
        if (customPassiveDeceleration is ManeuverAcceleration passiveDeceleration)
        {
            Validate(passiveDeceleration, nameof(customPassiveDeceleration));
        }

        Validate(maximumSubCruiseSpeed, nameof(maximumSubCruiseSpeed));
        Validate(cruiseSpeed, nameof(cruiseSpeed));
        Validate(turnRate, nameof(turnRate));
        if (movingSpoolDuration == SimulationDuration.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(movingSpoolDuration),
                movingSpoolDuration,
                "Moving spool duration must be positive.");
        }

        BaseMassKilograms = baseMassKilograms;
        BaseAcceleration = baseAcceleration;
        CustomPassiveDeceleration = customPassiveDeceleration;
        MaximumSubCruiseSpeed = maximumSubCruiseSpeed;
        CruiseSpeed = cruiseSpeed;
        TurnRate = turnRate;
        MovingSpoolDuration = movingSpoolDuration;
    }

    public ulong BaseMassKilograms { get; }

    public ManeuverAcceleration BaseAcceleration { get; }

    public ManeuverAcceleration? CustomPassiveDeceleration { get; }

    public ManeuverSpeed MaximumSubCruiseSpeed { get; }

    public ManeuverSpeed CruiseSpeed { get; }

    public ManeuverTurnRate TurnRate { get; }

    public SimulationDuration MovingSpoolDuration { get; }

    /// <summary>
    /// Resolves the mass-scaled directional and passive rates. Each exact
    /// rational exists only during this call and is rounded to the nearest
    /// fixed-point unit, with exact half-unit ties rounded upward.
    /// </summary>
    public EffectiveShipManeuverCapability ResolveForMass(
        ulong effectiveMassKilograms)
    {
        ArgumentOutOfRangeException.ThrowIfZero(effectiveMassKilograms);

        ManeuverAcceleration primary = ResolveAcceleration(
            BaseAcceleration,
            effectiveMassKilograms,
            denominatorMultiplier: 1);
        ManeuverAcceleration precision = ResolveAcceleration(
            BaseAcceleration,
            effectiveMassKilograms,
            denominatorMultiplier: 10);
        ManeuverAcceleration passive = CustomPassiveDeceleration is ManeuverAcceleration custom
            ? ResolveAcceleration(custom, effectiveMassKilograms, denominatorMultiplier: 1)
            : ResolveAcceleration(BaseAcceleration, effectiveMassKilograms, denominatorMultiplier: 4);

        return new EffectiveShipManeuverCapability(
            effectiveMassKilograms,
            primary,
            precision,
            passive,
            MaximumSubCruiseSpeed,
            CruiseSpeed,
            TurnRate,
            MovingSpoolDuration);
    }

    /// <summary>Rejects a default fixed-point value at the aggregate boundary.</summary>
    private static void Validate(ManeuverAcceleration value, string parameterName)
    {
        if (value.MillimetersPerSecondSquared == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Acceleration must be positive.");
        }
    }

    /// <summary>Rejects a default fixed-point value at the aggregate boundary.</summary>
    private static void Validate(ManeuverSpeed value, string parameterName)
    {
        if (value.MillimetersPerSecond == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Speed must be positive.");
        }
    }

    /// <summary>Rejects a default fixed-point value at the aggregate boundary.</summary>
    private static void Validate(ManeuverTurnRate value, string parameterName)
    {
        if (value.MillidegreesPerSecond == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Turn rate must be positive.");
        }
    }

    /// <summary>
    /// Applies mass scaling and an optional policy ratio as one exact fraction,
    /// then publishes only the rounded fixed-point rate.
    /// </summary>
    private ManeuverAcceleration ResolveAcceleration(
        ManeuverAcceleration authored,
        ulong effectiveMassKilograms,
        uint denominatorMultiplier)
    {
        UInt128 numerator =
            (UInt128)authored.MillimetersPerSecondSquared * BaseMassKilograms;
        UInt128 denominator =
            (UInt128)effectiveMassKilograms * denominatorMultiplier;
        UInt128 rounded = RoundToNearestWithHalfUp(numerator, denominator);
        if (rounded == 0 || rounded > ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "Mass scaling must resolve to a positive representable acceleration.");
        }

        return new ManeuverAcceleration((ulong)rounded);
    }

    /// <summary>Rounds one positive exact ratio to nearest with half ties upward.</summary>
    private static UInt128 RoundToNearestWithHalfUp(
        UInt128 numerator,
        UInt128 denominator)
    {
        UInt128 quotient = numerator / denominator;
        UInt128 remainder = numerator % denominator;
        return remainder * 2 >= denominator
            ? quotient + 1
            : quotient;
    }
}

/// <summary>
/// Immutable maneuver capability after mass scaling and directional policy
/// have resolved every acceleration to authoritative fixed-point units.
/// </summary>
public sealed record EffectiveShipManeuverCapability
{
    internal EffectiveShipManeuverCapability(
        ulong effectiveMassKilograms,
        ManeuverAcceleration primaryAcceleration,
        ManeuverAcceleration precisionAcceleration,
        ManeuverAcceleration passiveDeceleration,
        ManeuverSpeed maximumSubCruiseSpeed,
        ManeuverSpeed cruiseSpeed,
        ManeuverTurnRate turnRate,
        SimulationDuration movingSpoolDuration)
    {
        EffectiveMassKilograms = effectiveMassKilograms;
        PrimaryAcceleration = primaryAcceleration;
        PrecisionAcceleration = precisionAcceleration;
        PassiveDeceleration = passiveDeceleration;
        MaximumSubCruiseSpeed = maximumSubCruiseSpeed;
        CruiseSpeed = cruiseSpeed;
        TurnRate = turnRate;
        MovingSpoolDuration = movingSpoolDuration;
    }

    public ulong EffectiveMassKilograms { get; }

    public ManeuverAcceleration PrimaryAcceleration { get; }

    public ManeuverAcceleration PrecisionAcceleration { get; }

    public ManeuverAcceleration PassiveDeceleration { get; }

    public ManeuverSpeed MaximumSubCruiseSpeed { get; }

    public ManeuverSpeed CruiseSpeed { get; }

    public ManeuverTurnRate TurnRate { get; }

    public SimulationDuration MovingSpoolDuration { get; }
}

internal static class ManeuverFixedPoint
{
    /// <summary>
    /// Parses the deliberately narrow authored decimal grammar and scales it
    /// without binary floating point or intermediate UInt64 overflow.
    /// </summary>
    internal static ulong ParsePositiveMilliUnits(string value)
    {
        ulong result = ParseNonNegativeMilliUnits(value);
        if (result == 0)
        {
            throw Invalid(value);
        }

        return result;
    }

    /// <summary>
    /// Parses the narrow authored decimal grammar while allowing exact zero.
    /// </summary>
    internal static ulong ParseNonNegativeMilliUnits(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw Invalid(value);
        }

        int decimalPoint = value.IndexOf('.');
        if (decimalPoint != value.LastIndexOf('.'))
        {
            throw Invalid(value);
        }

        int wholeLength = decimalPoint < 0 ? value.Length : decimalPoint;
        int fractionalLength = decimalPoint < 0 ? 0 : value.Length - decimalPoint - 1;
        if (wholeLength == 0 || fractionalLength > 3 || decimalPoint >= 0 && fractionalLength == 0)
        {
            throw Invalid(value);
        }

        UInt128 whole;
        UInt128 fractional;
        try
        {
            whole = ParseDigits(value.AsSpan(0, wholeLength), value);
            fractional = decimalPoint < 0
                ? 0
                : ParseDigits(value.AsSpan(decimalPoint + 1, fractionalLength), value);
            for (int index = fractionalLength; index < 3; index++)
            {
                fractional *= 10;
            }
        }
        catch (OverflowException)
        {
            throw Invalid(value);
        }

        UInt128 maximumWhole = ((UInt128)ulong.MaxValue - fractional) / 1_000;
        if (whole > maximumWhole)
        {
            throw Invalid(value);
        }

        UInt128 scaled = whole * 1_000 + fractional;
        return (ulong)scaled;
    }

    /// <summary>Accumulates ASCII digits while keeping overflow outside UInt64.</summary>
    private static UInt128 ParseDigits(ReadOnlySpan<char> digits, string source)
    {
        UInt128 value = 0;
        foreach (char digit in digits)
        {
            if (digit is < '0' or > '9')
            {
                throw Invalid(source);
            }

            value = checked(value * 10 + (uint)(digit - '0'));
        }

        return value;
    }

    private static FormatException Invalid(string? value) =>
        new($"'{value}' is not a positive invariant decimal with at most three fractional digits.");
}
