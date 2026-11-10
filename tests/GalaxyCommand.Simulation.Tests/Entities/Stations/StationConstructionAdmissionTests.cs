using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationConstructionAdmissionTests
{
    private static readonly MaterialId Metal = new(1);
    private static readonly MaterialId Electronics = new(2);

    [Fact]
    public void StagingNeedsOnlyTheNextWholeWorkIncrement()
    {
        Quantity minimum = StationConstructionAdmission.ValidateBaseRecipe(
            Recipe(10, (Metal, 17), (Electronics, 3)), Materials(), new(3), new(3));

        Assert.Equal(new Quantity(3), minimum);
    }

    [Fact]
    public void IndividuallyAffordableInputsCanStillDeadlockTogether()
    {
        Assert.Throws<ArgumentException>(() => StationConstructionAdmission.ValidateBaseRecipe(
            Recipe(10, (Metal, 1), (Electronics, 1)), Materials(), new(1), new(10)));
    }

    [Fact]
    public void BaseCompletionCannotShrinkStagingCapacity()
    {
        Assert.Throws<ArgumentException>(() => StationConstructionAdmission.ValidateBaseRecipe(
            Recipe(10, (Metal, 1)), Materials(), new(3), new(2)));
    }

    [Fact]
    public void MaterialFreeRecipeCanUseZeroStagingCapacity()
    {
        Assert.Equal(Quantity.Zero, StationConstructionAdmission.ValidateBaseRecipe(
            Recipe(10), Materials(), Quantity.Zero, Quantity.Zero));
    }

    [Fact]
    public void MissingMaterialReferenceRejectsAdmission()
    {
        Assert.Throws<ArgumentException>(() => StationConstructionAdmission.ValidateBaseRecipe(
            Recipe(10, (new MaterialId(3), 1)), Materials(), new(10), new(10)));
    }

    [Fact]
    public void RejectsZeroWorkZeroInputAndDefaultMaterialIdentity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StationConstructionAdmission.ValidateBaseRecipe(
            Recipe(0), Materials(), new(10), new(10)));
        Assert.Throws<ArgumentException>(() => StationConstructionAdmission.ValidateBaseRecipe(
            Recipe(10, (Metal, 0)), Materials(), new(10), new(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => StationConstructionAdmission.ValidateBaseRecipe(
            Recipe(10, (default, 1)), Materials(), new(10), new(10)));
    }

    [Fact]
    public void CheckedRecipeTotalsRejectOverflowEvenWhenIndividualIncrementsFit()
    {
        Assert.Throws<OverflowException>(() => StationConstructionAdmission.ValidateBaseRecipe(
            Recipe(ulong.MaxValue, (Metal, ulong.MaxValue), (Electronics, 1)),
            Materials(), new(2), new(2)));
    }

    [Fact]
    public void MaximumValuesUseExactCeilingWithoutOverflow()
    {
        Assert.Equal(new Quantity(1), StationConstructionAdmission.ValidateBaseRecipe(
            Recipe(ulong.MaxValue, (Metal, ulong.MaxValue)), Materials(), new(1), new(1)));
    }

    [Fact]
    public void MinimumStagingSupportsEveryIncrementForSmallRecipes()
    {
        for (ulong work = 1; work <= 10; work++)
        for (ulong metal = 1; metal <= 12; metal++)
        for (ulong electronics = 1; electronics <= 12; electronics++)
        {
            ConstructionRecipe recipe = Recipe(work, (Metal, metal), (Electronics, electronics));
            Quantity minimum = StationConstructionAdmission.ValidateBaseRecipe(
                recipe, Materials(), new(24), new(24));
            ulong largest = 0;
            for (ulong completed = 0; completed < work; completed++)
            {
                StationConstructionProgress increment = StationConstructionProgress.Evaluate(
                    recipe, new(completed), new(1),
                    new Dictionary<MaterialId, Quantity> { [Metal] = new(metal), [Electronics] = new(electronics) });
                ulong needed = increment.ConsumedInputs.Values.Aggregate(0UL, (sum, quantity) => sum + quantity.Units);
                largest = Math.Max(largest, needed);
            }

            Assert.Equal(largest, minimum.Units);
        }
    }

    private static ConstructionRecipe Recipe(ulong work, params (MaterialId Id, ulong Units)[] inputs) =>
        new(inputs.Select(input => new KeyValuePair<MaterialId, Quantity>(input.Id, new(input.Units))), new(work));

    private static MaterialInventoryCompatibilityMap Materials() => new(
        new[] { Metal, Electronics }.Select(id => new KeyValuePair<MaterialId, PhysicalDefinition>(id,
            new(QualifiedContentKey.Create("test.stations", "material", $"material-{id.Value}"),
                PhysicalHoldingKind.Fungible, new(1)))));
}
