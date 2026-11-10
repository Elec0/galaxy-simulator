using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>Pure recovery calculation for unfinished station structures, with no refunds or inventory mutation.</summary>
internal static class StationDismantlingRecovery
{
    /// <summary>
    /// Caps each authored completed-structure recovery quantity by material actually consumed.
    /// Missing consumed inputs supply zero recovery; output is immutable and material-ordered.
    /// </summary>
    internal static IReadOnlyDictionary<MaterialId, Quantity> Evaluate(
        IReadOnlyDictionary<MaterialId, Quantity> authoredRecovery,
        IReadOnlyDictionary<MaterialId, Quantity> consumedInputs)
    {
        ArgumentNullException.ThrowIfNull(authoredRecovery);
        ArgumentNullException.ThrowIfNull(consumedInputs);
        var result = new SortedDictionary<MaterialId, Quantity>(EntityIdComparer<MaterialId>.Instance);
        foreach (MaterialId material in consumedInputs.Keys)
            if (material.Value == 0) throw new ArgumentException("Consumed material identities must be valid.", nameof(consumedInputs));
        foreach ((MaterialId material, Quantity authored) in authoredRecovery)
        {
            if (material.Value == 0) throw new ArgumentException("Recovery material identities must be valid.", nameof(authoredRecovery));
            ulong consumed = consumedInputs.TryGetValue(material, out Quantity value) ? value.Units : 0;
            ulong recovered = Math.Min(authored.Units, consumed);
            if (recovered != 0) result.Add(material, new(recovered));
        }
        return new ReadOnlyDictionary<MaterialId, Quantity>(result);
    }
}
