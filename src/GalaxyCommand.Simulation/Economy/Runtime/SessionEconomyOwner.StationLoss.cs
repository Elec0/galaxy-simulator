using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>Revalidated economic consequences joined to one canonical cargo delta.</summary>
internal sealed class PreparedStationEconomyLoss : IPreparedStationWorkflowLoss
{
    internal PreparedStationEconomyLoss(SessionEconomyOwner owner, StationId station,
        IReadOnlyList<StationModuleId> removedModules, bool destroyBase,
        IReadOnlyList<PreparedProductionProviderRemoval> productionRemovals,
        IReadOnlyList<PreparedConstructionProviderRemoval> constructionRemovals,
        IReadOnlyList<PreparedProductionInputLoss> productionInputs,
        IReadOnlyList<PreparedConstructionInputLoss> constructionInputs,
        IReadOnlyList<PreparedTransportRemovalCancellation> transports,
        IReadOnlyList<InventoryCommitmentRelease> releases)
    {
        Owner = owner; Station = station; RemovedModules = removedModules; DestroyBase = destroyBase;
        ProductionRemovals = productionRemovals; ConstructionRemovals = constructionRemovals;
        ProductionInputs = productionInputs; ConstructionInputs = constructionInputs;
        Transports = transports; Releases = releases; Cancellations = CaptureCancellations();
    }
    internal SessionEconomyOwner Owner { get; }
    internal StationId Station { get; }
    internal IReadOnlyList<StationModuleId> RemovedModules { get; }
    internal bool DestroyBase { get; }
    internal IReadOnlyList<PreparedProductionProviderRemoval> ProductionRemovals { get; }
    internal IReadOnlyList<PreparedConstructionProviderRemoval> ConstructionRemovals { get; }
    internal IReadOnlyList<PreparedProductionInputLoss> ProductionInputs { get; }
    internal IReadOnlyList<PreparedConstructionInputLoss> ConstructionInputs { get; }
    internal IReadOnlyList<PreparedTransportRemovalCancellation> Transports { get; }
    public IReadOnlyList<InventoryCommitmentRelease> Releases { get; }
    internal IReadOnlyList<StationProviderAgendaEvent> Cancellations { get; }
    private ReadOnlyCollection<StationProviderAgendaEvent> CaptureCancellations() => Array.AsReadOnly(
        ProductionRemovals.SelectMany(value => value.Jobs.Where(job => job.Status == ProductionJobStatus.Running
            && job.Completion is not null).Select(job => new StationProviderAgendaEvent(job.Completion!.Value,
                job.Generation, new EconomicEvent.ProductionComplete(value.Owner.FacilityId, job.Job.Id))))
        .Concat(ConstructionRemovals.SelectMany(value => value.Jobs.Where(job => job.Status == ConstructionOrderStatus.Running
            && job.Completion is not null).Select(job => new StationProviderAgendaEvent(job.Completion!.Value,
                job.Generation, new EconomicEvent.ConstructionComplete(value.Owner.FacilityId, job.Order.Id)))))
        .Concat(Transports.Where(value => value.TransitionAt is not null).Select(value => new StationProviderAgendaEvent(
            value.TransitionAt!.Value, value.Generation, new EconomicEvent.Transport(value.Status switch
            {
                TransportJobStatus.TravelingToSource => new TransportEvent.Arrive(value.Job.Id, value.Generation, TravelTarget.Source),
                TransportJobStatus.TravelingToDestination => new TransportEvent.Arrive(value.Job.Id, value.Generation, TravelTarget.Destination),
                TransportJobStatus.Loading => new TransportEvent.FinishLoading(value.Job.Id, value.Generation),
                TransportJobStatus.Unloading => new TransportEvent.FinishUnloading(value.Job.Id, value.Generation),
                _ => throw new InvalidOperationException("Waiting transport retains an unexpected pending transition."),
            })))).ToArray());
    internal bool CanCommitWithoutSourceLoss() => Owner.CanCommitStationProviderRemoval(this);
    internal void ApplyWithoutSourceLoss() => Owner.ApplyStationProviderRemoval(this);
    public bool CanCommit(PreparedCargoSourceLoss loss) => Owner.CanCommitStationLoss(this, loss);
    public void ApplyAfterSourceLoss(PreparedCargoSourceLoss loss) => Owner.ApplyStationLoss(this, loss);
}

internal sealed partial class SessionEconomyOwner
{
    /// <summary>Discovers forced releases first; a second preparation joins the exact additional loss claims.</summary>
    internal PreparedStationEconomyLoss? PrepareStationDestruction(StationId station,
        IReadOnlyList<StationModuleId> removedModules, bool destroyBase,
        IReadOnlyList<InventoryCommitmentRelease> required)
    {
        var removed = removedModules.ToHashSet();
        var facilities = _facilities.Values.Where(facility => facility.StationBinding is { } binding
            && binding.StationId == station && (destroyBase || removed.Contains(binding.ModuleId))).ToArray();
        var production = new List<PreparedProductionProviderRemoval>();
        var construction = new List<PreparedConstructionProviderRemoval>();
        foreach (var facility in facilities)
        {
            Inventory? inventory = _lifecycleInventories.Get(facility.InventoryId);
            if (inventory is null) return null;
            if (_productionLines.TryGetValue(facility.FacilityId, out var line))
            {
                var effect = line.PrepareProviderRemoval(inventory);
                if (effect is null) return null;
                production.Add(effect);
            }
            if (_constructionProcesses.TryGetValue(facility.FacilityId, out var process))
            {
                var effect = process.PrepareProviderRemoval(inventory);
                if (effect is null) return null;
                construction.Add(effect);
            }
        }
        var releases = production.SelectMany(effect => effect.Releases)
            .Concat(construction.SelectMany(effect => effect.Releases)).ToList();
        if (required.Any(release => !ReferenceEquals(_lifecycleInventories.Get(release.Inventory.Id), release.Inventory) || !release.CanCommit())) return null;
        var unresolved = required.Where(release => !releases.Any(forced => SameRelease(forced, release))).ToList();
        var productionInputs = new List<PreparedProductionInputLoss>();
        var constructionInputs = new List<PreparedConstructionInputLoss>();
        var transports = new List<PreparedTransportRemovalCancellation>();

        // A base loss also cancels in-flight work referring to its departing storage.
        var removedInventories = destroyBase ? facilities.Concat(CaptureRetiredStationFacilities()
            .Where(facility => facility.StationBinding?.StationId == station)).Select(facility => facility.InventoryId).ToHashSet() : [];
        var transportIds = unresolved.Select(ReservationOwnerOf).OfType<ReservationOwner.TransportJob>()
            .Select(owner => owner.JobId).ToHashSet();
        foreach (var job in _transportBoard.Jobs.Where(job => job.Status is not (TransportJobStatus.Completed
            or TransportJobStatus.Cancelled or TransportJobStatus.FailedBeforeLoading)))
            if (removedInventories.Contains(job.SourceInventoryId) || removedInventories.Contains(job.DestinationInventoryId))
                transportIds.Add(job.Id);
        foreach (var id in transportIds.OrderBy(id => id.Value))
        {
            TransportJob? job = _transportBoard.GetJob(id);
            if (job is null || !_transportBoard.TryPrepareCancellationForEntityRemoval(job,
                _ships.GetFreighter(job.ShipId), _lifecycleInventories, out var effect)) return null;
            transports.Add(effect!);
            foreach (var release in effect!.Releases)
                if (!releases.Any(existing => SameRelease(existing, release))) releases.Add(release);
        }
        unresolved.RemoveAll(release => releases.Any(existing => SameRelease(existing, release)));
        foreach (var group in unresolved.GroupBy(ReservationOwnerOf))
        {
            if (group.Any(release => release is not InventoryCommitmentRelease.Material)) return null;
            var material = group.Cast<InventoryCommitmentRelease.Material>().ToArray();
            switch (group.Key)
            {
                case ReservationOwner.ProductionJob owner:
                    ProductionLine? line = _productionLines.Values.SingleOrDefault(value => value.GetJob(owner.JobId) is not null);
                    var productionEffect = line?.PrepareInputLoss(material);
                    if (productionEffect is null) return null;
                    productionInputs.Add(productionEffect);
                    break;
                case ReservationOwner.ConstructionOrder owner:
                    ConstructionProcess? process = _constructionProcesses.Values.SingleOrDefault(value => value.GetOrder(owner.OrderId) is not null);
                    var constructionEffect = process?.PrepareInputLoss(material);
                    if (constructionEffect is null) return null;
                    constructionInputs.Add(constructionEffect);
                    break;
                default: return null;
            }
            releases.AddRange(material);
        }
        if (!TransportCancellationArithmeticFits(transports)) return null;
        return new(this, station, Array.AsReadOnly(removed.OrderBy(id => id.Value).ToArray()), destroyBase,
            production.AsReadOnly(), construction.AsReadOnly(), productionInputs.AsReadOnly(),
            constructionInputs.AsReadOnly(), transports.AsReadOnly(), releases.AsReadOnly());
    }

    internal bool CanCommitStationLoss(PreparedStationEconomyLoss effect, PreparedCargoSourceLoss loss) =>
        ReferenceEquals(effect.Owner, this) && loss.CanApply()
        && effect.ProductionRemovals.All(value => value.Owner.CanApplyProviderRemoval(value, loss))
        && effect.ConstructionRemovals.All(value => value.Owner.CanApplyProviderRemoval(value, loss))
        && effect.ProductionInputs.All(value => value.Owner.CanCommitInputLoss(value, loss))
        && effect.ConstructionInputs.All(value => value.Owner.CanCommitInputLoss(value, loss))
        && effect.Transports.All(value => _transportBoard.CanCommitRemovalCancellation(value, _lifecycleInventories))
        && TransportCancellationArithmeticFits(effect.Transports);

    internal void ApplyStationLoss(PreparedStationEconomyLoss effect, PreparedCargoSourceLoss loss)
    {
        if (!ReferenceEquals(effect.Owner, this) || !loss.WasApplied
            || effect.ProductionRemovals.Any(value => !value.Owner.CanApplyProviderRemoval(value, loss))
            || effect.ConstructionRemovals.Any(value => !value.Owner.CanApplyProviderRemoval(value, loss))
            || effect.Transports.Any(value => !_transportBoard.CanCommitRemovalAfterSourceLoss(value, _lifecycleInventories, loss)))
            throw new InvalidOperationException("Station economic loss changed after preparation.");
        foreach (var value in effect.ProductionInputs) value.Owner.ApplyInputLossAfterSourceLoss(value, loss);
        foreach (var value in effect.ConstructionInputs) value.Owner.ApplyInputLossAfterSourceLoss(value, loss);
        foreach (var value in effect.ProductionRemovals) value.Owner.ApplyProviderRemoval(value, loss);
        foreach (var value in effect.ConstructionRemovals) value.Owner.ApplyProviderRemoval(value, loss);
        foreach (var value in effect.Transports)
            if (!_transportBoard.CancelOrInterrupt(value.Job.Id, value.Freighter, _lifecycleInventories))
                throw new InvalidOperationException("Station transport became terminal during loss commit.");
        ArchiveStationFacilities(effect.Station, effect.RemovedModules, effect.DestroyBase);
        _stationProviderRevision++;
    }

    internal bool CanCommitStationProviderRemoval(PreparedStationEconomyLoss effect) =>
        ReferenceEquals(effect.Owner, this) && effect.ProductionInputs.Count == 0
        && effect.ConstructionInputs.Count == 0
        && effect.Transports.All(value => _transportBoard.CanCommitRemovalCancellation(value, _lifecycleInventories))
        && TransportCancellationArithmeticFits(effect.Transports)
        && effect.ProductionRemovals.All(value => value.Owner.CanApplyProviderRemoval(value))
        && effect.ConstructionRemovals.All(value => value.Owner.CanApplyProviderRemoval(value));

    internal void ApplyStationProviderRemoval(PreparedStationEconomyLoss effect)
    {
        if (!CanCommitStationProviderRemoval(effect)) throw new InvalidOperationException("Station provider removal changed after preparation.");
        foreach (var value in effect.ProductionRemovals) value.Owner.ApplyProviderRemoval(value);
        foreach (var value in effect.ConstructionRemovals) value.Owner.ApplyProviderRemoval(value);
        foreach (var value in effect.Transports)
            if (!_transportBoard.CancelOrInterrupt(value.Job.Id, value.Freighter, _lifecycleInventories))
                throw new InvalidOperationException("Station transport became terminal during provider removal.");
        ArchiveStationFacilities(effect.Station, effect.RemovedModules, effect.DestroyBase);
        _stationProviderRevision++;
    }

    private static ReservationOwner? ReservationOwnerOf(InventoryCommitmentRelease release) => release switch
    {
        InventoryCommitmentRelease.Material material => material.Reservation.Owner,
        InventoryCommitmentRelease.Capacity capacity => capacity.Reservation.Owner,
        InventoryCommitmentRelease.Physical physical => physical.Reservation.Owner,
        _ => null,
    };

    private static bool SameRelease(InventoryCommitmentRelease left, InventoryCommitmentRelease right) =>
        ReferenceEquals(left.Inventory, right.Inventory) && (left, right) switch
        {
            (InventoryCommitmentRelease.Material first, InventoryCommitmentRelease.Material second) => ReferenceEquals(first.Reservation, second.Reservation),
            (InventoryCommitmentRelease.Capacity first, InventoryCommitmentRelease.Capacity second) => ReferenceEquals(first.Reservation, second.Reservation),
            (InventoryCommitmentRelease.Physical first, InventoryCommitmentRelease.Physical second) => ReferenceEquals(first.Reservation, second.Reservation),
            _ => false,
        };

    private static bool TransportCancellationArithmeticFits(IReadOnlyList<PreparedTransportRemovalCancellation> values) =>
        values.GroupBy(value => value.Demand).All(group => group.Aggregate((UInt128)group.Key.Remaining.Units,
            (sum, value) => sum + value.Job.Quantity.Units) <= ulong.MaxValue)
        && values.Where(value => value.Supply is not null).GroupBy(value => value.Supply!).All(group =>
            group.Aggregate((UInt128)group.Key.Remaining.Units, (sum, value) => sum + value.Job.Quantity.Units) <= ulong.MaxValue);
}
