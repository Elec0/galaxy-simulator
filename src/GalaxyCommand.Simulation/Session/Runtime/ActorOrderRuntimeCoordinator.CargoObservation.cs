namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    /// <summary>
    /// Groups covered objects by their exposed original handle. Private alias
    /// merges must not regroup visible inspectors or disclose a hidden bridge.
    /// </summary>
    internal IReadOnlyList<CargoObjectView> CaptureCoveredCargoField(PrincipalId observer, DebrisFieldId originalFieldId)
    {
        IReadOnlyList<CargoObjectView> covered = CaptureCoveredCargo(observer);
        return Array.AsReadOnly(covered.Where(view => view.OriginalFieldId == originalFieldId).ToArray());
    }

    /// <summary>
    /// Exposes current contents only through a live owned pickup interaction. Range
    /// and order authority permit this local inspection independently of map coverage.
    /// </summary>
    internal IReadOnlyList<CargoCollectionProgressView> CaptureCargoCollectionProgress(PrincipalId observer)
    {
        ThrowIfUnhealthy();
        ArgumentOutOfRangeException.ThrowIfZero(observer.Value);
        if (_cargoAttempts is null || _cargo is null) return Array.Empty<CargoCollectionProgressView>();
        var views = new List<CargoCollectionProgressView>();
        foreach (CargoCollectionAttempt attempt in _cargoAttempts.CaptureAttempts())
        {
            if (_lifecycle.GetRequiredShip(attempt.ShipId).PrincipalId != observer
                || _orders.GetActive(attempt.ShipId) is not { Action: ShipActionIntent.Cargo } active
                || active.Id != attempt.OrderId || CargoAuthorityFailure(attempt.ShipId, active) is not null
                || !CargoInRange(attempt.ShipId, attempt.TargetPosition, CargoPickupRange(attempt.ShipId))
                || _cargo.CaptureObjectView(attempt.TargetId) is not { } target) continue;
            views.Add(new(attempt.ShipId, attempt.OrderId, target, attempt.ReservedQuantity, attempt.StartedAt, attempt.DueAt));
        }
        return Array.AsReadOnly(views.ToArray());
    }

    /// <summary>
    /// Queries current physical coverage for a principal without storing knowledge,
    /// adding sensor sources, or canonicalizing exposed original field handles.
    /// </summary>
    internal IReadOnlyList<CargoObjectView> CaptureCoveredCargo(PrincipalId observer)
    {
        ThrowIfUnhealthy();
        ArgumentOutOfRangeException.ThrowIfZero(observer.Value);
        if (_cargo is null) return Array.Empty<CargoObjectView>();
        IReadOnlyList<CargoObjectView> objects = _cargo.CaptureObjectViews();
        PlayerCoverageBoundary coverage = CaptureCoverageBoundary(observer);
        HashSet<EntityId> covered = objects.Where(view => coverage.Contains(view.Position)).Select(view => view.EntityId).ToHashSet();
        return CargoObjectObservation.ProjectCovered(objects, covered);
    }
}
