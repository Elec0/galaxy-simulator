using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Deterministic fixed-point projection from ship heading into system-local
/// acceleration axes.
/// </summary>
public static class ManeuverHeadingProjection
{
    private const long UnitScale = 1L << 62;
    private const long CordicGainCorrection = 2_800_459_870_029_452_954;
    private const long NanodegreesPerMillidegree = 1_000_000;
    private const long NanodegreesPerHalfTurn = 180_000_000_000;
    private const int QuarterTurnMillidegrees = 90_000;
    private const int HalfTurnMillidegrees = 180_000;
    private const int FullTurnMillidegrees = 360_000;

    private static readonly long[] CordicAnglesNanodegrees =
    [
        45_000_000_000,
        26_565_051_177,
        14_036_243_468,
        7_125_016_349,
        3_576_334_375,
        1_789_910_608,
        895_173_710,
        447_614_171,
        223_810_500,
        111_905_677,
        55_952_892,
        27_976_453,
        13_988_227,
        6_994_114,
        3_497_057,
        1_748_528,
        874_264,
        437_132,
        218_566,
        109_283,
        54_642,
        27_321,
        13_660,
        6_830,
        3_415,
        1_708,
        854,
        427,
        213,
        107,
        53,
        27,
        13,
        7,
        3,
        2,
        1,
    ];

    /// <summary>
    /// Projects positive forward acceleration at the supplied canonical
    /// heading. Zero points along positive X and clockwise rotation points
    /// toward negative Y. Published components use normal signed rounding and
    /// reject values outside their fixed-point representation.
    /// </summary>
    public static ShipAcceleration ProjectForward(
        ShipHeading heading,
        ManeuverAcceleration forwardAcceleration)
    {
        (long cosine, long sine) = UnitVector(heading);
        ulong magnitude = forwardAcceleration.MillimetersPerSecondSquared;
        long x = ProjectComponent(magnitude, cosine);
        long y = ProjectComponent(magnitude, checked(-sine));
        return new ShipAcceleration(x, y);
    }

    /// <summary>
    /// Projects a positive forward speed at the supplied canonical heading
    /// through the same fixed-point CORDIC basis used by forward thrust.
    /// </summary>
    public static ShipVelocity ProjectForwardVelocity(
        ShipHeading heading,
        ManeuverSpeed forwardSpeed)
    {
        (long cosine, long sine) = UnitVector(heading);
        ulong magnitude = forwardSpeed.MillimetersPerSecond;
        long x = ProjectComponent(magnitude, cosine);
        long y = ProjectComponent(magnitude, checked(-sine));
        return new ShipVelocity(x, y);
    }

    /// <summary>
    /// Projects one admitted primary-plus-precision allocation into system-local
    /// acceleration. Positive lateral precision points right, clockwise from
    /// forward. Invalid envelopes or components outside signed publication
    /// range return false and exact zero rather than a partial result.
    /// </summary>
    public static bool TryProject(
        ShipHeading heading,
        ShipRelativeThrustAllocation allocation,
        ManeuverAcceleration primaryLimit,
        ManeuverAcceleration precisionLimit,
        out ShipAcceleration acceleration)
    {
        if (!ManeuverThrustEnvelope.Contains(
                allocation,
                primaryLimit,
                precisionLimit))
        {
            acceleration = ShipAcceleration.Zero;
            return false;
        }

        (long cosine, long sine) = UnitVector(heading);
        BigInteger forward =
            (BigInteger)allocation.PrimaryForwardMillimetersPerSecondSquared
            + allocation.PrecisionForwardMillimetersPerSecondSquared;
        BigInteger lateral =
            allocation.PrecisionLateralMillimetersPerSecondSquared;

        // Positive lateral is the clockwise perpendicular basis, so its world
        // components are negative sine and negative cosine respectively.
        BigInteger xNumerator = forward * cosine - lateral * sine;
        BigInteger yNumerator = -forward * sine - lateral * cosine;
        if (!TryPublishComponent(xNumerator, out long x)
            || !TryPublishComponent(yNumerator, out long y))
        {
            acceleration = ShipAcceleration.Zero;
            return false;
        }

        acceleration = new ShipAcceleration(x, y);
        return true;
    }

    /// <summary>
    /// Resolves a nonzero system-local displacement to the nearest canonical
    /// millidegree course heading through fixed-point CORDIC vectoring. Zero X
    /// points east and positive heading turns clockwise. A zero vector has no
    /// course and is rejected.
    /// </summary>
    public static ShipHeading ResolveCourseHeading(
        long displacementX,
        long displacementY)
    {
        if (displacementX == 0 && displacementY == 0)
        {
            throw new ArgumentException(
                "A zero displacement has no course heading.");
        }

        if (displacementY == 0)
        {
            return displacementX > 0
                ? ShipHeading.Zero
                : new ShipHeading(HalfTurnMillidegrees);
        }

        if (displacementX == 0)
        {
            return displacementY < 0
                ? new ShipHeading(QuarterTurnMillidegrees)
                : new ShipHeading(3 * QuarterTurnMillidegrees);
        }

        // Scale before vectoring so small integer displacements retain useful
        // low bits through every shift while the full Int64 range remains safe.
        Int128 x = checked((Int128)displacementX * UnitScale);
        Int128 y = checked((Int128)displacementY * UnitScale);
        long counterclockwiseNanodegrees = 0;
        if (x < 0)
        {
            counterclockwiseNanodegrees = y >= 0
                ? NanodegreesPerHalfTurn
                : -NanodegreesPerHalfTurn;
            x = checked(-x);
            y = checked(-y);
        }

        for (int index = 0;
            index < CordicAnglesNanodegrees.Length && y != 0;
            index++)
        {
            Int128 priorX = x;
            Int128 priorY = y;
            if (priorY > 0)
            {
                x = checked(priorX + (priorY >> index));
                y = checked(priorY - (priorX >> index));
                counterclockwiseNanodegrees = checked(
                    counterclockwiseNanodegrees
                    + CordicAnglesNanodegrees[index]);
            }
            else
            {
                x = checked(priorX - (priorY >> index));
                y = checked(priorY + (priorX >> index));
                counterclockwiseNanodegrees = checked(
                    counterclockwiseNanodegrees
                    - CordicAnglesNanodegrees[index]);
            }
        }

        Int128 clockwiseMillidegrees = ManeuverKinematics.RoundSignedRatio(
            -(Int128)counterclockwiseNanodegrees,
            NanodegreesPerMillidegree);
        Int128 canonical = clockwiseMillidegrees
            % ShipHeading.MillidegreesPerRevolution;
        if (canonical < 0)
        {
            canonical += ShipHeading.MillidegreesPerRevolution;
        }

        return new ShipHeading((uint)canonical);
    }

    /// <summary>
    /// Resolves a Q2.62 cosine and mathematical sine through a nanodegree-angle
    /// CORDIC rotation. Exact cardinal headings bypass approximation so their
    /// zero components remain exact across the complete magnitude range.
    /// </summary>
    private static (long Cosine, long Sine) UnitVector(ShipHeading heading)
    {
        switch (heading.Millidegrees)
        {
            case 0:
                return (UnitScale, 0);
            case QuarterTurnMillidegrees:
                return (0, UnitScale);
            case HalfTurnMillidegrees:
                return (-UnitScale, 0);
            case 3 * QuarterTurnMillidegrees:
                return (0, -UnitScale);
        }

        int signedMillidegrees = (int)heading.Millidegrees;
        if (signedMillidegrees > HalfTurnMillidegrees)
        {
            signedMillidegrees -= FullTurnMillidegrees;
        }

        bool negate = false;
        if (signedMillidegrees > QuarterTurnMillidegrees)
        {
            signedMillidegrees -= HalfTurnMillidegrees;
            negate = true;
        }
        else if (signedMillidegrees < -QuarterTurnMillidegrees)
        {
            signedMillidegrees += HalfTurnMillidegrees;
            negate = true;
        }

        long x = CordicGainCorrection;
        long y = 0;
        long remaining = checked(
            signedMillidegrees * NanodegreesPerMillidegree);
        for (int index = 0; index < CordicAnglesNanodegrees.Length; index++)
        {
            long priorX = x;
            long priorY = y;
            if (remaining >= 0)
            {
                x = checked(priorX - (priorY >> index));
                y = checked(priorY + (priorX >> index));
                remaining -= CordicAnglesNanodegrees[index];
            }
            else
            {
                x = checked(priorX + (priorY >> index));
                y = checked(priorY - (priorX >> index));
                remaining += CordicAnglesNanodegrees[index];
            }
        }

        return negate ? (checked(-x), checked(-y)) : (x, y);
    }

    /// <summary>
    /// Applies one Q2.62 direction component and publishes the nearest signed
    /// acceleration unit with half-unit ties rounded away from zero.
    /// </summary>
    private static long ProjectComponent(ulong magnitude, long direction)
    {
        Int128 numerator = checked((Int128)magnitude * direction);
        Int128 rounded = ManeuverKinematics.RoundSignedRatio(
            numerator,
            (UInt128)UnitScale);
        return checked((long)rounded);
    }

    /// <summary>
    /// Publishes one exact Q2.62 projection with normal signed rounding. A
    /// rounded result outside the acceleration component representation is
    /// rejected without truncation.
    /// </summary>
    private static bool TryPublishComponent(
        BigInteger numerator,
        out long component)
    {
        bool negative = numerator.Sign < 0;
        BigInteger quotient = BigInteger.DivRem(
            BigInteger.Abs(numerator),
            UnitScale,
            out BigInteger remainder);
        if (remainder * 2 >= UnitScale)
        {
            quotient++;
        }

        BigInteger signed = negative ? -quotient : quotient;
        if (signed < long.MinValue || signed > long.MaxValue)
        {
            component = 0;
            return false;
        }

        component = (long)signed;
        return true;
    }
}
