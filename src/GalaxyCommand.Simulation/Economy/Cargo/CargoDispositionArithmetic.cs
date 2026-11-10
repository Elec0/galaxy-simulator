namespace GalaxyCommand.Simulation;

/// <summary>Pure cargo calculations; callers own policy selection, preparation, and commit.</summary>
internal static class CargoDispositionArithmetic
{
    /// <summary>Floors one definition's selected quantity by its explicit recovery fraction.</summary>
    public static Quantity SurvivalCount(Quantity selected, ulong numerator, ulong denominator)
    {
        ArgumentOutOfRangeException.ThrowIfZero(denominator);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(numerator, denominator);

        // Multiply before dividing so neither per-unit rounding nor cross-occurrence carry changes survival.
        UInt128 recovered = checked((UInt128)selected.Units * numerator) / denominator;
        return new Quantity(checked((ulong)recovered));
    }

    /// <summary>Rounds the capped storage curve upward once to a whole simulated millisecond.</summary>
    public static SimulationDuration CollectionDuration(Quantity occupiedStorage,
        SimulationDuration minimum, SimulationDuration maximum, Quantity storageCap)
    {
        ArgumentOutOfRangeException.ThrowIfZero(occupiedStorage.Units, nameof(occupiedStorage));
        ArgumentOutOfRangeException.ThrowIfZero(minimum.Milliseconds, nameof(minimum));
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum.Milliseconds, minimum.Milliseconds, nameof(maximum));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(storageCap.Units, 1UL, nameof(storageCap));

        ulong size = Math.Min(occupiedStorage.Units, storageCap.Units);
        ulong denominator = storageCap.Units - 1;
        UInt128 numerator = checked((UInt128)(maximum.Milliseconds - minimum.Milliseconds) * (size - 1));
        // Round only the exact rational increment. Even maximal policy values fit the wide intermediate.
        UInt128 increment = checked(numerator + denominator - 1) / denominator;
        return new SimulationDuration(checked(minimum.Milliseconds + (ulong)increment));
    }

    /// <summary>Rejects zero lifetime and unrepresentable expiry before cargo publication.</summary>
    public static SimulationTime ExpiryAt(SimulationTime createdAt, SimulationDuration lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfZero(lifetime.Milliseconds, nameof(lifetime));
        return createdAt.Add(lifetime);
    }
}
