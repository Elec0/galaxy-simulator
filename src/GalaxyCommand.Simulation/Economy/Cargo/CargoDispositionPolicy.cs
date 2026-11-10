using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Explicit supported causes for selecting a recovery fraction.</summary>
public enum CargoLossCause { EntityDestruction, StorageModuleLoss, FunctionalEjection }

/// <summary>Exact recovery ratio applied once per selected definition and causal occurrence.</summary>
public sealed record CargoRecoveryFraction
{
    /// <summary>Rejects an undefined ratio or recovery outside the inclusive zero-to-one range.</summary>
    public CargoRecoveryFraction(ulong numerator, ulong denominator)
    {
        ArgumentOutOfRangeException.ThrowIfZero(denominator);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(numerator, denominator);
        Numerator = numerator;
        Denominator = denominator;
    }
    public ulong Numerator { get; }
    public ulong Denominator { get; }
}

/// <summary>Immutable, explicitly selected cargo parameters and compatible algorithm versions.</summary>
public sealed class CargoDispositionPolicy
{
    /// <summary>
    /// Validates all parameters before admitting cargo authority. Every supported
    /// cause is explicit; functional ejection must preserve its entire selection.
    /// </summary>
    public CargoDispositionPolicy(QualifiedContentKey key, int generationBehaviorVersion, int mergeBehaviorVersion,
        int orderingBehaviorVersion, int durationBehaviorVersion,
        IEnumerable<KeyValuePair<CargoLossCause, CargoRecoveryFraction>> recoveryByCause,
        int fungibleObjectCount, ulong scatterRadiusMeters, SimulationDuration lifetime,
        SimulationDuration minimumCollectionDuration, SimulationDuration maximumCollectionDuration, Quantity collectionStorageCap)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(recoveryByCause);
        if (key.ContentKind.Value != "cargo-policy")
            throw new ArgumentException("The key must identify a cargo-policy definition.", nameof(key));
        ArgumentOutOfRangeException.ThrowIfNotEqual(generationBehaviorVersion, 1);
        ArgumentOutOfRangeException.ThrowIfNotEqual(mergeBehaviorVersion, 1);
        ArgumentOutOfRangeException.ThrowIfNotEqual(orderingBehaviorVersion, 1);
        ArgumentOutOfRangeException.ThrowIfNotEqual(durationBehaviorVersion, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fungibleObjectCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scatterRadiusMeters, 1UL << 50);
        ArgumentOutOfRangeException.ThrowIfZero(lifetime.Milliseconds, nameof(lifetime));
        ArgumentOutOfRangeException.ThrowIfZero(minimumCollectionDuration.Milliseconds, nameof(minimumCollectionDuration));
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCollectionDuration.Milliseconds,
            minimumCollectionDuration.Milliseconds, nameof(maximumCollectionDuration));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(collectionStorageCap.Units, 1UL, nameof(collectionStorageCap));

        var causes = new SortedDictionary<CargoLossCause, CargoRecoveryFraction>();
        foreach ((CargoLossCause cause, CargoRecoveryFraction fraction) in recoveryByCause)
        {
            if (!Enum.IsDefined(cause) || fraction is null || !causes.TryAdd(cause, fraction))
                throw new ArgumentException("Recovery causes must be supported, unique, and have exact fractions.", nameof(recoveryByCause));
        }
        if (causes.Count != Enum.GetValues<CargoLossCause>().Length
            || causes[CargoLossCause.FunctionalEjection].Numerator != causes[CargoLossCause.FunctionalEjection].Denominator)
            throw new ArgumentException("All causes are required and functional ejection must preserve cargo fully.", nameof(recoveryByCause));

        Key = key;
        GenerationBehaviorVersion = generationBehaviorVersion;
        MergeBehaviorVersion = mergeBehaviorVersion;
        OrderingBehaviorVersion = orderingBehaviorVersion;
        DurationBehaviorVersion = durationBehaviorVersion;
        RecoveryByCause = new ReadOnlyDictionary<CargoLossCause, CargoRecoveryFraction>(causes);
        FungibleObjectCount = fungibleObjectCount;
        ScatterRadiusMeters = scatterRadiusMeters;
        Lifetime = lifetime;
        MinimumCollectionDuration = minimumCollectionDuration;
        MaximumCollectionDuration = maximumCollectionDuration;
        CollectionStorageCap = collectionStorageCap;
    }

    public QualifiedContentKey Key { get; }
    public int GenerationBehaviorVersion { get; }
    public int MergeBehaviorVersion { get; }
    public int OrderingBehaviorVersion { get; }
    public int DurationBehaviorVersion { get; }
    public IReadOnlyDictionary<CargoLossCause, CargoRecoveryFraction> RecoveryByCause { get; }
    public int FungibleObjectCount { get; }
    public ulong ScatterRadiusMeters { get; }
    public SimulationDuration Lifetime { get; }
    public SimulationDuration MinimumCollectionDuration { get; }
    public SimulationDuration MaximumCollectionDuration { get; }
    public Quantity CollectionStorageCap { get; }
    /// <summary>Returns one configured cause without silently substituting a default survival rule.</summary>
    public CargoRecoveryFraction RecoveryFor(CargoLossCause cause) =>
        RecoveryByCause.TryGetValue(cause, out CargoRecoveryFraction? fraction)
            ? fraction : throw new ArgumentOutOfRangeException(nameof(cause));
}
