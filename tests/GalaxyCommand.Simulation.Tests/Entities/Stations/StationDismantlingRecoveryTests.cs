namespace GalaxyCommand.Simulation.Tests;

public sealed class StationDismantlingRecoveryTests
{
    [Fact]
    public void UnfinishedRecoveryIsCappedPerMaterialWithoutRoundingOrSubstitution()
    {
        var metal = new MaterialId(1);
        var chips = new MaterialId(2);
        var fuel = new MaterialId(3);
        var outputs = new Dictionary<MaterialId, Quantity> { [metal] = new(8), [chips] = new(3), [fuel] = new(4) };
        var consumed = new Dictionary<MaterialId, Quantity> { [metal] = new(2), [chips] = new(7) };
        IReadOnlyDictionary<MaterialId, Quantity> recovery = StationDismantlingRecovery.Evaluate(outputs, consumed);
        Assert.Equal(new Quantity(2), recovery[metal]);
        Assert.Equal(new Quantity(3), recovery[chips]);
        Assert.False(recovery.ContainsKey(fuel));
        outputs.Clear();
        consumed.Clear();
        Assert.Equal(2, recovery.Count);
    }

    [Fact]
    public void NoConsumedMaterialsYieldNoRecovery()
    {
        Assert.Empty(StationDismantlingRecovery.Evaluate(new Dictionary<MaterialId, Quantity> { [new(1)] = new(10) },
            new Dictionary<MaterialId, Quantity>()));
    }

    [Fact]
    public void RejectsDefaultMaterialIdentityWithoutProducingOutputs()
    {
        Assert.Throws<ArgumentException>(() => StationDismantlingRecovery.Evaluate(
            new Dictionary<MaterialId, Quantity> { [default] = new(1) }, new Dictionary<MaterialId, Quantity>()));
    }
}
