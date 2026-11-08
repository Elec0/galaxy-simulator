using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationConstructionProgressTests
{
    private static readonly MaterialId Metal = new(1);
    private static readonly MaterialId Electronics = new(2);

    [Fact]
    public void PartialDeliveryAdvancesToGreatestAffordableWork()
    {
        var recipe = Recipe(10, (Metal, 7), (Electronics, 3));
        var available = new Dictionary<MaterialId, Quantity>
        {
            [Metal] = new(4),
            [Electronics] = new(2),
        };

        StationConstructionProgress result = StationConstructionProgress.Evaluate(
            recipe, new Work(0), new Work(10), available);

        Assert.Equal(new Work(5), result.CompletedWork);
        Assert.Equal(new Quantity(4), result.ConsumedInputs[Metal]);
        Assert.Equal(new Quantity(2), result.ConsumedInputs[Electronics]);
        Assert.Equal(new Quantity(4), available[Metal]);
        Assert.Equal(new Quantity(2), available[Electronics]);
    }

    [Fact]
    public void PreviouslyConsumedMaterialSupportsFurtherWorkWithoutAnotherIncrement()
    {
        StationConstructionProgress result = StationConstructionProgress.Evaluate(
            Recipe(10, (Metal, 3)), new Work(1), new Work(9),
            new Dictionary<MaterialId, Quantity>());

        Assert.Equal(new Work(3), result.CompletedWork);
        Assert.Empty(result.ConsumedInputs);
    }

    [Fact]
    public void MissingFirstIncrementPreventsAnyProgress()
    {
        StationConstructionProgress result = StationConstructionProgress.Evaluate(
            Recipe(10, (Metal, 1)), new Work(0), new Work(10),
            new Dictionary<MaterialId, Quantity>());

        Assert.Equal(new Work(0), result.CompletedWork);
        Assert.Empty(result.ConsumedInputs);
    }

    [Fact]
    public void CompletionConsumesOnlyRemainingRecipeInputs()
    {
        StationConstructionProgress result = StationConstructionProgress.Evaluate(
            Recipe(10, (Metal, 7)), new Work(5), new Work(ulong.MaxValue),
            new Dictionary<MaterialId, Quantity> { [Metal] = new(ulong.MaxValue) });

        Assert.Equal(new Work(10), result.CompletedWork);
        Assert.Equal(new Quantity(3), result.ConsumedInputs[Metal]);
    }

    [Fact]
    public void ExactArithmeticSupportsMaximumMaterialAndWorkValues()
    {
        StationConstructionProgress result = StationConstructionProgress.Evaluate(
            Recipe(ulong.MaxValue, (Metal, ulong.MaxValue)), new Work(0),
            new Work(ulong.MaxValue),
            new Dictionary<MaterialId, Quantity> { [Metal] = new(ulong.MaxValue - 1) });

        Assert.Equal(new Work(ulong.MaxValue - 1), result.CompletedWork);
        Assert.Equal(new Quantity(ulong.MaxValue - 1), result.ConsumedInputs[Metal]);
    }

    [Fact]
    public void MaterialFreeRecipeRemainsBoundedByAvailableWork()
    {
        StationConstructionProgress result = StationConstructionProgress.Evaluate(
            Recipe(10), new Work(3), new Work(2), new Dictionary<MaterialId, Quantity>());

        Assert.Equal(new Work(5), result.CompletedWork);
        Assert.Empty(result.ConsumedInputs);
    }

    [Fact]
    public void ZeroWorkAndCompletedProjectsConsumeNothing()
    {
        var available = new Dictionary<MaterialId, Quantity> { [Metal] = new(10) };
        ConstructionRecipe recipe = Recipe(10, (Metal, 7));

        StationConstructionProgress paused = StationConstructionProgress.Evaluate(
            recipe, new Work(4), new Work(0), available);
        StationConstructionProgress completed = StationConstructionProgress.Evaluate(
            recipe, new Work(10), new Work(10), available);

        Assert.Equal(new Work(4), paused.CompletedWork);
        Assert.Empty(paused.ConsumedInputs);
        Assert.Equal(new Work(10), completed.CompletedWork);
        Assert.Empty(completed.ConsumedInputs);
    }

    [Fact]
    public void ConsumptionIsCanonicalAndCannotBeMutated()
    {
        StationConstructionProgress result = StationConstructionProgress.Evaluate(
            Recipe(1, (Electronics, 1), (Metal, 1)), new Work(0), new Work(1),
            new Dictionary<MaterialId, Quantity> { [Electronics] = new(1), [Metal] = new(1) });

        Assert.Equal([Metal, Electronics], result.ConsumedInputs.Keys);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<MaterialId, Quantity>)result.ConsumedInputs).Add(new MaterialId(3), new(1)));
    }

    [Fact]
    public void EveryUpdatePartitionHasIdenticalCumulativeConsumption()
    {
        ConstructionRecipe recipe = Recipe(12, (Metal, 7), (Electronics, 5));
        for (int mask = 0; mask < 1 << 11; mask++)
        {
            Work completed = new(0);
            var remaining = new Dictionary<MaterialId, Quantity>
            {
                [Metal] = new(7), [Electronics] = new(5),
            };
            int previousBoundary = 0;
            for (int boundary = 1; boundary <= 12; boundary++)
            {
                if (boundary != 12 && (mask & (1 << (boundary - 1))) == 0) continue;
                StationConstructionProgress result = StationConstructionProgress.Evaluate(
                    recipe, completed, new Work((ulong)(boundary - previousBoundary)), remaining);
                foreach ((MaterialId material, Quantity quantity) in result.ConsumedInputs)
                    remaining[material] = remaining[material].Subtract(quantity);
                completed = result.CompletedWork;
                previousBoundary = boundary;
            }

            Assert.Equal(new Work(12), completed);
            Assert.All(remaining.Values, quantity => Assert.Equal(Quantity.Zero, quantity));
        }
    }

    [Fact]
    public void AffordableWorkMatchesExhaustiveSmallIntegerOracle()
    {
        for (ulong requiredWork = 1; requiredWork <= 12; requiredWork++)
        for (ulong requiredMaterial = 1; requiredMaterial <= 12; requiredMaterial++)
        for (ulong completed = 0; completed <= requiredWork; completed++)
        for (ulong available = 0; available <= requiredMaterial; available++)
        {
            ulong before = (requiredMaterial * completed + requiredWork - 1) / requiredWork;
            ulong expected = completed;
            for (ulong candidate = completed; candidate <= requiredWork; candidate++)
            {
                ulong consumption = (requiredMaterial * candidate + requiredWork - 1) / requiredWork;
                if (consumption - before <= available) expected = candidate;
            }

            StationConstructionProgress result = StationConstructionProgress.Evaluate(
                Recipe(requiredWork, (Metal, requiredMaterial)), new Work(completed),
                new Work(requiredWork), new Dictionary<MaterialId, Quantity> { [Metal] = new(available) });

            Assert.Equal(new Work(expected), result.CompletedWork);
        }
    }

    [Fact]
    public void RejectsInvalidRecipeOrPriorProgress()
    {
        var available = new Dictionary<MaterialId, Quantity>();
        Assert.Throws<ArgumentOutOfRangeException>(() => StationConstructionProgress.Evaluate(
            Recipe(0), new Work(0), new Work(0), available));
        Assert.Throws<ArgumentException>(() => StationConstructionProgress.Evaluate(
            Recipe(1, (Metal, 0)), new Work(0), new Work(1), available));
        Assert.Throws<ArgumentOutOfRangeException>(() => StationConstructionProgress.Evaluate(
            Recipe(1), new Work(2), new Work(0), available));
    }

    private static ConstructionRecipe Recipe(ulong work, params (MaterialId Id, ulong Units)[] inputs) =>
        new(inputs.Select(input => new KeyValuePair<MaterialId, Quantity>(input.Id, new(input.Units))), new Work(work));
}
