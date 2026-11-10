using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationEconomicProviderDefinitionTests
{
    [Fact]
    public void ProductionProviderRejectsInvalidWorkOutputAndThroughput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StationProductionProvider(new([], new(1), new(1), new(0)), new(1), false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StationProductionProvider(new([], new(1), new(0), new(1)), new(1), false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StationProductionProvider(new([], new(1), new(1), new(1)), default, false));
    }

    [Fact]
    public void ProductionRecipeCannotBeMutatedThroughAnExposedDictionary()
    {
        var provider = new StationProductionProvider(new([new(new MaterialId(1), new Quantity(2))], new(2), new(1), new(1)), new(1), false);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<MaterialId, Quantity>)provider.Recipe.Inputs).Add(new(3), new(1)));
    }

    [Fact]
    public void ShipProviderRequiresExplicitPositiveRateDesignsAndNonScriptMaterializationControl()
    {
        Assert.Throws<ArgumentException>(() => new StationShipConstructionProvider([], new(1), GameSessionTestFixture.PlayerController, InitialShipOrderPolicy.NoInitialOrder));
        Assert.Throws<ArgumentException>(() => new StationShipConstructionProvider([GameSessionTestFixture.Design, GameSessionTestFixture.Design], new(1), GameSessionTestFixture.PlayerController, InitialShipOrderPolicy.NoInitialOrder));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StationShipConstructionProvider([GameSessionTestFixture.Design], default, GameSessionTestFixture.PlayerController, InitialShipOrderPolicy.NoInitialOrder));
        Assert.Throws<ArgumentException>(() => new StationShipConstructionProvider([GameSessionTestFixture.Design], new(1), new(ActorControllerKind.Script, new("script")), InitialShipOrderPolicy.NoInitialOrder));
    }

    [Fact]
    public void ModuleCarriesOnlyExplicitEconomicProviders()
    {
        var production = new StationProductionProvider(new([], new(1), new(1), new(1)), new(2), true);
        var ships = new StationShipConstructionProvider([GameSessionTestFixture.Design], new(3), GameSessionTestFixture.PlayerController, InitialShipOrderPolicy.NoInitialOrder);
        var module = new StationModuleDefinition(QualifiedContentKey.Create("test.providers", "station-module", "combined"),
            "Combined", 1, new(0), 0, null, new([], new(1)), new(1), [], [], productionProvider: production, shipConstructionProvider: ships);
        Assert.Same(production, module.ProductionProvider);
        Assert.Same(ships, module.ShipConstructionProvider);
        Assert.Null(module.ConstructionCapability);
    }
}
