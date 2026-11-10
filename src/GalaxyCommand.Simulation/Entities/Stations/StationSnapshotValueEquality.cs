namespace GalaxyCommand.Simulation;

/// <summary>Semantic comparisons for historical station payloads whose collections are copied during private restoration.</summary>
internal static class StationSnapshotValueEquality
{
    /// <summary>Compares all scalar and collection values without granting equality from shared collection references.</summary>
    internal static bool Equal(StationSnapshot? left, StationSnapshot? right)
    {
        if (left is null || right is null || left.Projects is null || right.Projects is null
            || left.Modules is null || right.Modules is null || left.Projects.Count != right.Projects.Count
            || left with { Projects = right.Projects, Modules = right.Modules } != right
            || !left.Modules.SequenceEqual(right.Modules)) return false;
        for (int index = 0; index < left.Projects.Count; index++)
            if (!EqualProject(left.Projects[index], right.Projects[index])) return false;
        return true;
    }

    /// <summary>Includes private project quantities and assignments when validating an owned historical fact payload.</summary>
    private static bool EqualProject(StationProjectSnapshot? left, StationProjectSnapshot? right)
    {
        if (left is null || right is null || left.ConsumedInputs is null || right.ConsumedInputs is null
            || left.AssignedShips is null || right.AssignedShips is null || left.ConsumedInputs.Count != right.ConsumedInputs.Count
            || left with { ConsumedInputs = right.ConsumedInputs, AssignedShips = right.AssignedShips, AssignedModules = right.AssignedModules } != right
            || !left.AssignedShips.SequenceEqual(right.AssignedShips)
            || !(left.AssignedModules ?? []).SequenceEqual(right.AssignedModules ?? [])) return false;
        return left.ConsumedInputs.All(input => right.ConsumedInputs.TryGetValue(input.Key, out Quantity amount) && amount == input.Value);
    }
}
