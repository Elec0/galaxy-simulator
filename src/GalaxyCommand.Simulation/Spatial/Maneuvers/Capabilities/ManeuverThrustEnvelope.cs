using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>
/// One thrust allocation in axes relative to the ship's current heading.
/// Primary thrust is forward-only; the signed precision vector can point in
/// any local direction, with positive lateral pointing right, clockwise from
/// forward.
/// </summary>
public readonly record struct ShipRelativeThrustAllocation(
    ulong PrimaryForwardMillimetersPerSecondSquared,
    long PrecisionForwardMillimetersPerSecondSquared,
    long PrecisionLateralMillimetersPerSecondSquared);

/// <summary>
/// Exact admission policy for simultaneous primary and precision thrust.
/// Rotation is independent and therefore absent from this translational budget.
/// </summary>
public static class ManeuverThrustEnvelope
{
    /// <summary>
    /// Returns whether the ship-relative allocation respects the forward cap,
    /// the precision-vector magnitude cap, and their shared elliptical budget.
    /// The comparison covers the complete fixed-point input range without
    /// floating-point conversion or overflow.
    /// </summary>
    public static bool Contains(
        ShipRelativeThrustAllocation allocation,
        ManeuverAcceleration primaryLimit,
        ManeuverAcceleration precisionLimit)
    {
        BigInteger primaryUse =
            allocation.PrimaryForwardMillimetersPerSecondSquared;
        BigInteger precisionForward =
            allocation.PrecisionForwardMillimetersPerSecondSquared;
        BigInteger precisionLateral =
            allocation.PrecisionLateralMillimetersPerSecondSquared;
        BigInteger primaryMaximum =
            primaryLimit.MillimetersPerSecondSquared;
        BigInteger precisionMaximum =
            precisionLimit.MillimetersPerSecondSquared;

        BigInteger primaryUseSquared = primaryUse * primaryUse;
        BigInteger precisionUseSquared =
            precisionForward * precisionForward
            + precisionLateral * precisionLateral;
        BigInteger primaryMaximumSquared =
            primaryMaximum * primaryMaximum;
        BigInteger precisionMaximumSquared =
            precisionMaximum * precisionMaximum;
        if (primaryUseSquared > primaryMaximumSquared
            || precisionUseSquared > precisionMaximumSquared)
        {
            return false;
        }

        // Cross multiplication retains the exact normalized-square comparison
        // only for this admission check and publishes no rational state.
        return primaryUseSquared * precisionMaximumSquared
            + precisionUseSquared * primaryMaximumSquared
            <= primaryMaximumSquared * precisionMaximumSquared;
    }
}
