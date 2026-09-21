using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Distinguishes aggregate holdings from individually identified physical
/// items without assigning equipment or other domain behavior.
/// </summary>
public enum PhysicalHoldingKind
{
    Fungible,
    Discrete,
}

/// <summary>
/// Immutable runtime definition for one kind of physical inventory content.
/// </summary>
public sealed record PhysicalDefinition
{
    /// <summary>
    /// Creates a physical definition with a positive capacity cost for every
    /// held unit or instance.
    /// </summary>
    public PhysicalDefinition(
        QualifiedContentKey key,
        PhysicalHoldingKind holdingKind,
        Quantity capacityCost)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Enum.IsDefined(holdingKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(holdingKind),
                holdingKind,
                "Unknown physical holding kind.");
        }

        if (capacityCost == Quantity.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacityCost),
                "Physical definitions must consume positive cargo capacity.");
        }

        Key = key;
        HoldingKind = holdingKind;
        CapacityCost = capacityCost;
    }

    public QualifiedContentKey Key { get; }

    public PhysicalHoldingKind HoldingKind { get; }

    public Quantity CapacityCost { get; }
}

/// <summary>
/// Immutable physical-definition lookup in canonical qualified-key order.
/// </summary>
public sealed class PhysicalDefinitionCatalog
{
    private readonly ReadOnlyCollection<PhysicalDefinition> _definitions;
    private readonly ReadOnlyDictionary<QualifiedContentKey, PhysicalDefinition> _byKey;

    /// <summary>
    /// Creates a catalog and rejects duplicate qualified content identities.
    /// </summary>
    public PhysicalDefinitionCatalog(IEnumerable<PhysicalDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var sorted = new SortedDictionary<QualifiedContentKey, PhysicalDefinition>(
            Comparer<QualifiedContentKey>.Create(
                (left, right) => StringComparer.Ordinal.Compare(
                    left?.ToString(),
                    right?.ToString())));
        foreach (PhysicalDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!sorted.TryAdd(definition.Key, definition))
            {
                throw new ArgumentException(
                    $"Duplicate physical definition {definition.Key}.",
                    nameof(definitions));
            }
        }

        _definitions = Array.AsReadOnly(sorted.Values.ToArray());
        _byKey = new ReadOnlyDictionary<QualifiedContentKey, PhysicalDefinition>(sorted);
    }

    public IReadOnlyList<PhysicalDefinition> Definitions => _definitions;

    /// <summary>
    /// Returns the definition for an exact qualified key, or null when the
    /// compatible content set does not provide it.
    /// </summary>
    public PhysicalDefinition? Get(QualifiedContentKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _byKey.GetValueOrDefault(key);
    }
}

/// <summary>
/// Explicit one-to-one bridge from legacy material identities to compatible
/// fungible definitions. Unit capacity is required to preserve legacy behavior.
/// </summary>
public sealed class MaterialInventoryCompatibilityMap
{
    private readonly ReadOnlyDictionary<MaterialId, PhysicalDefinition> _byMaterial;

    public MaterialInventoryCompatibilityMap(
        IEnumerable<KeyValuePair<MaterialId, PhysicalDefinition>> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        var byMaterial = new SortedDictionary<MaterialId, PhysicalDefinition>(
            EntityIdComparer<MaterialId>.Instance);
        var definitionKeys = new HashSet<QualifiedContentKey>();
        foreach ((MaterialId materialId, PhysicalDefinition definition) in mappings)
        {
            ArgumentOutOfRangeException.ThrowIfZero(materialId.Value);
            ArgumentNullException.ThrowIfNull(definition);
            if (definition.HoldingKind != PhysicalHoldingKind.Fungible
                || definition.CapacityCost != new Quantity(1))
            {
                throw new ArgumentException(
                    $"Legacy material {materialId} requires a fungible one-unit-capacity definition.",
                    nameof(mappings));
            }

            if (!byMaterial.TryAdd(materialId, definition)
                || !definitionKeys.Add(definition.Key))
            {
                throw new ArgumentException(
                    "Material compatibility mappings must be one-to-one.",
                    nameof(mappings));
            }
        }

        _byMaterial = new ReadOnlyDictionary<MaterialId, PhysicalDefinition>(byMaterial);
    }

    public IReadOnlyDictionary<MaterialId, PhysicalDefinition> Mappings => _byMaterial;

    public PhysicalDefinition? Get(MaterialId materialId) =>
        _byMaterial.GetValueOrDefault(materialId);

    internal PhysicalDefinition GetRequired(MaterialId materialId) =>
        Get(materialId)
        ?? throw new InvalidOperationException(
            $"Material {materialId} has no compatible physical definition.");
}
