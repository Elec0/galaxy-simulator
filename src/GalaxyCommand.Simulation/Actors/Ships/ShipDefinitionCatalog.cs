namespace GalaxyCommand.Simulation;

/// <summary>Retained operational definitions shared by starting ships and construction admission.</summary>
internal sealed class ShipDefinitionCatalog
{
    /// <summary>
    /// Canonicalizes definitions independently of producing facilities. Repeated identical
    /// definitions share one retained entry; conflicting meanings under an ID reject admission.
    /// </summary>
    internal ShipDefinitionCatalog(IEnumerable<ShipDesign> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var byId = new SortedDictionary<ConstructionDesignId, ShipDesign>(EntityIdComparer<ConstructionDesignId>.Instance);
        foreach (ShipDesign definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (byId.TryGetValue(definition.Id, out ShipDesign? prior))
            {
                if (prior.Name != definition.Name || prior.CargoCapacity != definition.CargoCapacity
                    || prior.ManeuverCapability != definition.ManeuverCapability
                    || prior.SensorRadiusUnits != definition.SensorRadiusUnits || prior.PickupRangeUnits != definition.PickupRangeUnits
                    || prior.Recipe.RequiredWork != definition.Recipe.RequiredWork
                    || !prior.Recipe.Inputs.OrderBy(input => input.Key.Value).SequenceEqual(
                        definition.Recipe.Inputs.OrderBy(input => input.Key.Value)))
                    throw new ArgumentException($"Conflicting retained ship definition {definition.Id}.", nameof(definitions));
            }
            else byId.Add(definition.Id, definition);
        }
        Definitions = Array.AsReadOnly(byId.Values.ToArray());
    }

    internal IReadOnlyList<ShipDesign> Definitions { get; }
}
