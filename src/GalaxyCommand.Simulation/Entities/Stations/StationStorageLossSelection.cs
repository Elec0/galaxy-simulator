using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Versioned shared station accounting policy; recovery disposition remains with the cargo policy.</summary>
public sealed record StationStorageLossPolicy
{
    /// <summary>Requires an explicit policy identity, supported behavior, and an exact positive cutoff no greater than one.</summary>
    public StationStorageLossPolicy(QualifiedContentKey key, int behaviorVersion, ulong cutoffNumerator, ulong cutoffDenominator)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfNotEqual(behaviorVersion, 1);
        ArgumentOutOfRangeException.ThrowIfZero(cutoffNumerator);
        ArgumentOutOfRangeException.ThrowIfZero(cutoffDenominator);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cutoffNumerator, cutoffDenominator);
        Key = key;
        BehaviorVersion = behaviorVersion;
        CutoffNumerator = cutoffNumerator;
        CutoffDenominator = cutoffDenominator;
    }

    public QualifiedContentKey Key { get; }
    public int BehaviorVersion { get; }
    public ulong CutoffNumerator { get; }
    public ulong CutoffDenominator { get; }
}

/// <summary>Immutable accumulated destruction and per-definition credits, including history larger than one cargo quantity.</summary>
internal sealed class StationStorageLossWindow
{
    internal static StationStorageLossWindow Empty { get; } = new(0, new Dictionary<QualifiedContentKey, BigInteger>());

    /// <summary>Copies canonical nonnegative accounting state; credits cannot exist outside a pending loss window.</summary>
    internal StationStorageLossWindow(BigInteger accumulatedDestroyedCapacity, IReadOnlyDictionary<QualifiedContentKey, BigInteger> overflowCredits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(accumulatedDestroyedCapacity);
        ArgumentNullException.ThrowIfNull(overflowCredits);
        var credits = new SortedDictionary<QualifiedContentKey, BigInteger>(Comparer<QualifiedContentKey>.Create(
            (left, right) => StringComparer.Ordinal.Compare(left.ToString(), right.ToString())));
        foreach ((QualifiedContentKey definition, BigInteger credit) in overflowCredits)
        {
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentOutOfRangeException.ThrowIfNegative(credit);
            if (credit > 0) credits.Add(definition, credit);
        }
        if (accumulatedDestroyedCapacity.IsZero && credits.Count > 0)
            throw new ArgumentException("Overflow credits require pending destruction.", nameof(overflowCredits));
        AccumulatedDestroyedCapacity = accumulatedDestroyedCapacity;
        OverflowCredits = new ReadOnlyDictionary<QualifiedContentKey, BigInteger>(credits);
    }

    internal BigInteger AccumulatedDestroyedCapacity { get; }
    internal IReadOnlyDictionary<QualifiedContentKey, BigInteger> OverflowCredits { get; }
}

internal sealed record StationStorageLossSelection(StationStorageLossWindow Window,
    IReadOnlyList<CargoDispositionSelection> Selected, Quantity RemovedCapacity)
{
    /// <summary>Evaluates one grouped storage loss without mutating stock, credits, identities or random streams.</summary>
    internal static StationStorageLossSelection Evaluate(StationStorageLossPolicy policy, StationStorageLossWindow window,
        StationId station, ulong occurrence, Quantity destroyedCapacity, Quantity survivingCapacity,
        InventoryCheckpoint inventory, PhysicalDefinitionCatalog definitions, DeterministicRandomOwner random)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfZero(station.Value);
        ArgumentOutOfRangeException.ThrowIfZero(occurrence);
        ArgumentOutOfRangeException.ThrowIfZero(destroyedCapacity.Units);
        Holding[] holdings = CaptureHoldings(inventory, definitions);
        BigInteger accumulated = window.AccumulatedDestroyedCapacity + destroyedCapacity.Units;
        bool settle = StationStorageLoss.HasReachedCutoff(accumulated, survivingCapacity,
            policy.CutoffNumerator, policy.CutoffDenominator);
        if (settle)
        {
            foreach (Holding holding in holdings)
            {
                BigInteger credit = window.OverflowCredits.GetValueOrDefault(holding.Definition.Key);
                BigInteger basis = holding.Quantity + credit;
                BigInteger denominator = survivingCapacity.Units + accumulated;
                BigInteger additional = BigInteger.Max(0, (basis * accumulated + denominator - 1) / denominator - credit);
                ulong count = (ulong)BigInteger.Min(holding.Quantity, additional);
                if (holding.Definition.HoldingKind == PhysicalHoldingKind.Fungible) holding.RemovedFungible = count;
                else
                    foreach (ItemInstanceId item in Shuffle(holding.Items, random, station, occurrence,
                        $"proportional:{holding.Definition.Key}").Take(checked((int)count))) holding.RemovedItems.Add(item);
            }
        }

        ulong used = 0;
        ulong removed = 0;
        var candidates = new List<OverflowCandidate>();
        foreach (Holding holding in holdings)
        {
            used = checked(used + checked(holding.Quantity * holding.Definition.CapacityCost.Units));
            removed = checked(removed + checked(holding.RemovedQuantity * holding.Definition.CapacityCost.Units));
            if (holding.Definition.HoldingKind == PhysicalHoldingKind.Fungible && holding.RemovedFungible < holding.Quantity)
                candidates.Add(new(holding, null));
            else
                candidates.AddRange(holding.Items.Where(item => !holding.RemovedItems.Contains(item))
                    .Select(item => new OverflowCandidate(holding, item)));
        }
        ulong excess = used - removed > survivingCapacity.Units ? used - removed - survivingCapacity.Units : 0;
        foreach (OverflowCandidate candidate in Shuffle(candidates, random, station, occurrence, "overflow"))
        {
            if (excess == 0) break;
            Holding holding = candidate.Holding;
            ulong count = candidate.Item is { } item
                ? holding.RemovedItems.Add(item) ? 1UL : 0UL
                : StationStorageLoss.OverflowUnits(new(excess), holding.Definition.CapacityCost,
                    new(holding.Quantity - holding.RemovedFungible)).Units;
            if (candidate.Item is null) holding.RemovedFungible += count;
            ulong freed = checked(count * holding.Definition.CapacityCost.Units);
            removed = checked(removed + freed);
            excess = freed >= excess ? 0 : excess - freed;
        }
        if (excess != 0) throw new InvalidOperationException("Selected station cargo cannot satisfy surviving capacity.");

        var selected = new List<CargoDispositionSelection>();
        var credits = window.OverflowCredits.ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach (Holding holding in holdings)
        {
            if (holding.RemovedQuantity == 0) continue;
            if (holding.Definition.HoldingKind == PhysicalHoldingKind.Fungible)
                selected.Add(new CargoDispositionSelection.Fungible(holding.Definition.Key, new(holding.RemovedFungible)));
            else
                selected.AddRange(holding.RemovedItems.OrderBy(item => item.Value)
                    .Select(item => new CargoDispositionSelection.Discrete(holding.Definition.Key, item)));
            if (!settle) credits[holding.Definition.Key] = credits.GetValueOrDefault(holding.Definition.Key) + holding.RemovedQuantity;
        }
        return new(settle ? StationStorageLossWindow.Empty : new(accumulated, credits), selected.AsReadOnly(), new(removed));
    }

    /// <summary>Starts every decision from canonical definition/instance order, independently of inventory enumeration order.</summary>
    private static Holding[] CaptureHoldings(InventoryCheckpoint inventory, PhysicalDefinitionCatalog definitions)
    {
        var holdings = new List<Holding>();
        foreach (InventoryFungibleCheckpoint held in inventory.FungibleHoldings)
        {
            PhysicalDefinition definition = definitions.Get(held.DefinitionKey)
                ?? throw new ArgumentException("A station holding definition is unresolved.", nameof(definitions));
            if (definition.HoldingKind != PhysicalHoldingKind.Fungible) throw new ArgumentException("Station holding kind is inconsistent.", nameof(inventory));
            holdings.Add(new(definition, held.Quantity.Units, []));
        }
        foreach (var group in inventory.DiscreteItems.GroupBy(item => item.DefinitionKey))
        {
            PhysicalDefinition definition = definitions.Get(group.Key)
                ?? throw new ArgumentException("A station item definition is unresolved.", nameof(definitions));
            if (definition.HoldingKind != PhysicalHoldingKind.Discrete) throw new ArgumentException("Station item kind is inconsistent.", nameof(inventory));
            holdings.Add(new(definition, 0, group.Select(item => item.Id).OrderBy(item => item.Value).ToArray()));
        }
        return holdings.OrderBy(holding => holding.Definition.Key.ToString(), StringComparer.Ordinal).ToArray();
    }

    /// <summary>Uses unbiased derived draws scoped by station, committed occurrence, purpose and canonical shuffle step.</summary>
    private static T[] Shuffle<T>(IEnumerable<T> values, DeterministicRandomOwner random, StationId station,
        ulong occurrence, string purpose)
    {
        T[] shuffled = values.ToArray();
        for (int index = shuffled.Length - 1; index > 0; index--)
        {
            var key = new RandomSampleKey(RandomScope.SessionRuntime, "station-storage-loss", "station", station.Value,
                "storage-destruction", occurrence, purpose, 0, index.ToString(CultureInfo.InvariantCulture));
            int other = checked((int)random.DeriveBelow(key, (ulong)index + 1));
            (shuffled[index], shuffled[other]) = (shuffled[other], shuffled[index]);
        }
        return shuffled;
    }

    private sealed class Holding(PhysicalDefinition definition, ulong fungible, ItemInstanceId[] items)
    {
        internal PhysicalDefinition Definition { get; } = definition;
        internal ItemInstanceId[] Items { get; } = items;
        internal ulong Quantity => Definition.HoldingKind == PhysicalHoldingKind.Fungible ? fungible : (ulong)Items.Length;
        internal ulong RemovedFungible { get; set; }
        internal HashSet<ItemInstanceId> RemovedItems { get; } = [];
        internal ulong RemovedQuantity => RemovedFungible + (ulong)RemovedItems.Count;
    }

    private sealed record OverflowCandidate(Holding Holding, ItemInstanceId? Item);
}
