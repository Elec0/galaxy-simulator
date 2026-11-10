namespace GalaxyCommand.Simulation;

internal sealed record PreparedStationShipPolicy(EntityLifecycleOwner Owner,
    ShipMaterializationPolicy? Expected, ShipMaterializationPolicy Replacement);

internal sealed partial class EntityLifecycleOwner
{
    /// <summary>Accepts only retained exact ship definitions before a station provider changes policy.</summary>
    internal PreparedStationShipPolicy? PrepareStationShipPolicy(ShipMaterializationPolicy policy)
    {
        foreach (ShipDesign design in policy.AllowedDesigns.Values)
        {
            if (!_operationalShipDefinitions.TryGetValue(design.Id, out ShipDesign? retained)) return null;
            try { _ = new ShipDefinitionCatalog([retained, design]); }
            catch (ArgumentException) { return null; }
        }
        // Live orders and materialization use the same retained definition objects after restoration.
        var canonical = new ShipMaterializationPolicy(policy.FacilityId, policy.PrincipalId, policy.Position,
            policy.BaseController, policy.InitialOrderPolicy,
            policy.AllowedDesigns.Keys.Select(id => _operationalShipDefinitions[id]));
        return new(this, _policies.GetValueOrDefault(policy.FacilityId), canonical);
    }

    internal bool CanApplyStationShipPolicy(PreparedStationShipPolicy prepared) =>
        ReferenceEquals(prepared.Owner, this)
        && ReferenceEquals(_policies.GetValueOrDefault(prepared.Replacement.FacilityId), prepared.Expected);

    internal void ApplyStationShipPolicy(PreparedStationShipPolicy prepared)
    {
        if (!CanApplyStationShipPolicy(prepared)) throw new InvalidOperationException("Station ship policy changed after preparation.");
        _policies[prepared.Replacement.FacilityId] = prepared.Replacement;
    }
}
