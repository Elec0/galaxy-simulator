namespace GalaxyCommand.Simulation;

internal sealed record PreparedStationShipOrder(SessionEconomyOwner Owner, ConstructionProcess Process,
    ShipMaterializationPolicy Policy, ShipDesign Design, IdSequenceCheckpoint Sequence)
{
    internal bool CanCommit() => Owner.CanCommitStationShipOrder(this);
    internal ConstructionOrderId Apply() => Owner.ApplyStationShipOrder(this);
}

internal sealed partial class SessionEconomyOwner
{
    /// <summary>Uses only this facility's retained authored designs and proves the next shared order identity before enqueue.</summary>
    internal PreparedStationShipOrder? PrepareStationShipOrder(FacilityId facility, ConstructionDesignId design)
    {
        if (_constructionIds.CaptureCheckpoint().NextValue is null
            || !_constructionProcesses.TryGetValue(facility, out var process)
            || _lifecycle.MaterializationPolicies.FirstOrDefault(value => value.FacilityId == facility) is not { } policy
            || policy.GetDesign(design) is not { } definition) return null;
        return new(this, process, policy, definition, _constructionIds.CaptureCheckpoint());
    }
    internal bool CanCommitStationShipOrder(PreparedStationShipOrder prepared) => ReferenceEquals(prepared.Owner, this)
        && ReferenceEquals(_constructionProcesses.GetValueOrDefault(prepared.Process.FacilityId), prepared.Process)
        && ReferenceEquals(_lifecycle.MaterializationPolicies.FirstOrDefault(value => value.FacilityId == prepared.Process.FacilityId), prepared.Policy)
        && ReferenceEquals(prepared.Policy.GetDesign(prepared.Design.Id), prepared.Design)
        && prepared.Sequence == _constructionIds.CaptureCheckpoint();
    internal ConstructionOrderId ApplyStationShipOrder(PreparedStationShipOrder prepared)
    {
        if (!CanCommitStationShipOrder(prepared)) throw new InvalidOperationException("Station ship order changed after preparation.");
        return prepared.Process.Enqueue(_constructionIds, prepared.Design);
    }
    internal bool TryResolveConstructionOrder(FacilityId facility, ConstructionOrderId order, out ConstructionDesignId design)
    {
        if (_constructionProcesses.GetValueOrDefault(facility)?.GetOrder(order) is { } current)
        { design = current.DesignId; return true; }
        return TryResolveArchivedConstructionOrder(facility, order, out design);
    }
}
