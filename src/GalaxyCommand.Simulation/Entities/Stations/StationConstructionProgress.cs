using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>Pure station work evaluation; inventory consumption remains an owner commit responsibility.</summary>
internal sealed class StationConstructionProgress
{
    private StationConstructionProgress(Work completedWork, IReadOnlyDictionary<MaterialId, Quantity> consumedInputs)
    {
        CompletedWork = completedWork;
        ConsumedInputs = consumedInputs;
    }

    public Work CompletedWork { get; }

    public IReadOnlyDictionary<MaterialId, Quantity> ConsumedInputs { get; }

    /// <summary>
    /// Finds the greatest affordable progress and its next consumable increment without reserving or mutating cargo.
    /// Invalid recipes or progress outside the recipe bounds are rejected before evaluation.
    /// </summary>
    public static StationConstructionProgress Evaluate(
        ConstructionRecipe recipe,
        Work completedWork,
        Work suppliedWork,
        IReadOnlyDictionary<MaterialId, Quantity> availableInputs)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(availableInputs);
        ulong totalWork = recipe.RequiredWork.Units;
        ArgumentOutOfRangeException.ThrowIfZero(totalWork, nameof(recipe));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(completedWork.Units, totalWork, nameof(completedWork));

        ulong nextWork = completedWork.Units + Math.Min(suppliedWork.Units, totalWork - completedWork.Units);
        foreach ((MaterialId material, Quantity required) in recipe.Inputs)
        {
            if (required == Quantity.Zero)
                throw new ArgumentException("Station recipe inputs must be positive.", nameof(recipe));

            ulong consumed = CumulativeConsumption(required.Units, completedWork.Units, totalWork);
            ulong available = availableInputs.TryGetValue(material, out Quantity quantity) ? quantity.Units : 0;
            ulong affordable = consumed + Math.Min(available, required.Units - consumed);

            // ceil(M*w/W) <= budget is exactly w <= floor(budget*W/M).
            // Widen before multiplication; even maximum ulong recipe values remain exact.
            ulong affordableWork = (ulong)((UInt128)affordable * totalWork / required.Units);
            nextWork = Math.Min(nextWork, affordableWork);
        }

        var increments = new SortedDictionary<MaterialId, Quantity>(EntityIdComparer<MaterialId>.Instance);
        foreach ((MaterialId material, Quantity required) in recipe.Inputs)
        {
            ulong increment = CumulativeConsumption(required.Units, nextWork, totalWork)
                - CumulativeConsumption(required.Units, completedWork.Units, totalWork);
            if (increment != 0) increments.Add(material, new Quantity(increment));
        }

        return new StationConstructionProgress(new Work(nextWork),
            new ReadOnlyDictionary<MaterialId, Quantity>(increments));
    }

    private static ulong CumulativeConsumption(ulong quantity, ulong completedWork, ulong totalWork) =>
        (ulong)(((UInt128)quantity * completedWork + totalWork - 1) / totalWork);
}
