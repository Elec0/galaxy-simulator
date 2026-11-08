using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Exact R01 accounting calculations only. Cargo selection, disposition and window commit belong to their owners.
/// </summary>
internal static class StationStorageLoss
{
    /// <summary>Compares accumulated loss with an explicit rational cutoff, including threshold equality.</summary>
    public static bool HasReachedCutoff(BigInteger accumulatedDestroyedCapacity, Quantity survivingCapacity,
        ulong cutoffNumerator, ulong cutoffDenominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(accumulatedDestroyedCapacity);
        ArgumentOutOfRangeException.ThrowIfZero(cutoffDenominator);
        if (accumulatedDestroyedCapacity.IsZero) return false;

        // Cross multiplication preserves equality at the threshold without floating-point rounding.
        return accumulatedDestroyedCapacity * cutoffDenominator
            >= (survivingCapacity.Units + accumulatedDestroyedCapacity) * cutoffNumerator;
    }

    /// <summary>Computes per-definition proportional loss after crediting cargo already removed for overflow.</summary>
    public static Quantity AdditionalLoss(Quantity held, Quantity overflowCredit,
        BigInteger accumulatedDestroyedCapacity, Quantity survivingCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(accumulatedDestroyedCapacity);
        if (accumulatedDestroyedCapacity.IsZero)
        {
            if (overflowCredit != Quantity.Zero)
                throw new ArgumentException("Overflow credits require accumulated storage loss.", nameof(overflowCredit));
            return Quantity.Zero;
        }

        // Credits participate in the original stock basis and are then subtracted once.
        // Both historical loss and held-plus-credited stock may exceed ulong after repeated changes.
        BigInteger denominator = survivingCapacity.Units + accumulatedDestroyedCapacity;
        BigInteger numerator = ((BigInteger)held.Units + overflowCredit.Units) * accumulatedDestroyedCapacity;
        BigInteger proportionalLoss = (numerator + denominator - 1) / denominator;
        BigInteger additional = BigInteger.Max(BigInteger.Zero, proportionalLoss - overflowCredit.Units);
        return new Quantity((ulong)BigInteger.Min(held.Units, additional));
    }

    /// <summary>Computes the minimum whole units needed at one overflow selection step, bounded by available stock.</summary>
    public static Quantity OverflowUnits(Quantity excessCapacity, Quantity unitCapacity,
        Quantity available)
    {
        ArgumentOutOfRangeException.ThrowIfZero(unitCapacity.Units, nameof(unitCapacity));
        UInt128 units = ((UInt128)excessCapacity.Units + unitCapacity.Units - 1) / unitCapacity.Units;
        return new Quantity(Math.Min(available.Units, (ulong)units));
    }
}
