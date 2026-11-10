namespace GalaxyCommand.Simulation;

/// <summary>Definition admission for station construction using the existing material inventory bridge.</summary>
internal static class StationConstructionAdmission
{
    /// <summary>
    /// Validates explicit base recipe and storage limits, returning the capacity needed for one work increment.
    /// Rejects unresolved materials, invalid recipe quantities, overflow, and staging deadlocks without mutation.
    /// </summary>
    internal static Quantity ValidateBaseRecipe(
        ConstructionRecipe recipe,
        MaterialInventoryCompatibilityMap materials,
        Quantity stagingCapacity,
        Quantity completedCapacity)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentOutOfRangeException.ThrowIfZero(recipe.RequiredWork.Units, nameof(recipe));
        if (completedCapacity.Units < stagingCapacity.Units)
            throw new ArgumentException("Completed base capacity must retain site staging capacity.", nameof(completedCapacity));

        ulong totalCapacity = 0;
        ulong minimumCapacity = 0;
        foreach ((MaterialId material, Quantity required) in recipe.Inputs)
        {
            ArgumentOutOfRangeException.ThrowIfZero(material.Value, nameof(recipe));
            if (required == Quantity.Zero)
                throw new ArgumentException("Station recipe inputs must be positive.", nameof(recipe));
            PhysicalDefinition definition = materials.Get(material)
                ?? throw new ArgumentException($"Station recipe material {material} is unresolved.", nameof(materials));

            // The first whole work unit consumes ceil(M/W) of every input at once.
            // Later one-unit increments never consume more, so this bound is both necessary and sufficient.
            ulong increment = (ulong)(((UInt128)required.Units + recipe.RequiredWork.Units - 1)
                / recipe.RequiredWork.Units);
            totalCapacity = checked(totalCapacity + checked(required.Units * definition.CapacityCost.Units));
            minimumCapacity = checked(minimumCapacity + checked(increment * definition.CapacityCost.Units));
        }

        if (stagingCapacity.Units < minimumCapacity)
            throw new ArgumentException("Site staging capacity cannot admit the first construction increment.", nameof(stagingCapacity));

        return new Quantity(minimumCapacity);
    }
}
