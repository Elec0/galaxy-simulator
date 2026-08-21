using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PhysicalDefinitionTests
{
    [Fact]
    public void DefinitionRequiresPositiveCapacityCost()
    {
        QualifiedContentKey key = QualifiedContentKey.Create(
            "core",
            "cargo",
            "ore");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PhysicalDefinition(
                key,
                PhysicalHoldingKind.Fungible,
                Quantity.Zero));
    }

    [Fact]
    public void DefinitionRejectsUnknownHoldingKind()
    {
        QualifiedContentKey key = QualifiedContentKey.Create(
            "core",
            "cargo",
            "ore");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PhysicalDefinition(
                key,
                (PhysicalHoldingKind)int.MaxValue,
                new Quantity(1)));
    }

    [Fact]
    public void CatalogOrdersDefinitionsByQualifiedKey()
    {
        PhysicalDefinition second = Definition("water");
        PhysicalDefinition first = Definition("ore");

        var catalog = new PhysicalDefinitionCatalog([second, first]);

        Assert.Equal(
            [first.Key, second.Key],
            catalog.Definitions.Select(definition => definition.Key));
    }

    [Fact]
    public void CatalogRejectsDuplicateQualifiedKey()
    {
        PhysicalDefinition definition = Definition("ore");

        Assert.Throws<ArgumentException>(
            () => new PhysicalDefinitionCatalog([definition, definition]));
    }

    [Fact]
    public void CatalogLooksUpOnlyAnExactQualifiedKey()
    {
        PhysicalDefinition definition = Definition("ore");
        var catalog = new PhysicalDefinitionCatalog([definition]);

        Assert.Same(definition, catalog.Get(definition.Key));
        Assert.Null(catalog.Get(QualifiedContentKey.Create("core", "cargo", "water")));
    }

    private static PhysicalDefinition Definition(string localId) =>
        new(
            QualifiedContentKey.Create("core", "cargo", localId),
            PhysicalHoldingKind.Fungible,
            new Quantity(1));
}
