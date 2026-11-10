namespace GalaxyCommand.Simulation;

internal sealed record StationRemovalReceipt(StationSnapshot Station, SimulationTime CreatedAt,
    SimulationTime RemovedAt, StationRemovalReason Reason, GameFactCause Cause);

internal sealed partial class StationRuntimeOwner
{
    private Func<EntityId, bool>? _canRemoveStationTargets;
    private Func<EntityId, List<GameFactProposal>>? _removeStationTargets;
    private Func<EntityId, int>? _stationTargetFactCapacity;
    private Func<EntityId, int>? _stationTargetAgendaCapacity;

    internal IReadOnlyList<StationRemovalReceipt> CaptureRemovals() => Array.AsReadOnly(_stationRemovals.Values.ToArray());

    /// <summary>Binds the fixed actor-order owner after fresh composition or private restoration.</summary>
    internal void BindRemovalTargets(Func<EntityId, bool> canRemove, Func<EntityId, List<GameFactProposal>> remove,
        Func<EntityId, int>? factCapacity = null, Func<EntityId, int>? agendaCapacity = null)
    {
        _canRemoveStationTargets = canRemove;
        _removeStationTargets = remove;
        _stationTargetFactCapacity = factCapacity;
        _stationTargetAgendaCapacity = agendaCapacity;
    }

    /// <summary>Claims external recovery before making an empty, module-free base unavailable for all new intake.</summary>
    private GameplayCommandHandlingResult DismantleBase(DismantleStationBaseCommand command)
    {
        if (!_stations.TryGetValue(command.Station, out StationState? station)
            || station.Revision != command.ExpectedStationRevision) return Rejected("station-stale-target");
        if (station.Principal != command.RequestingPrincipal) return Rejected("station-wrong-authority");
        if (_canRemoveStationTargets?.Invoke(station.Entity) == false) return Rejected("station-targets-busy");
        if (station.Phase == StationPhase.Dismantling || station.Modules.Count != 0 || station.Inventory.UsedCapacity != Quantity.Zero
            || station.Inventory.HasCommitments || station.Projects.Values.Any(project => project.Status
                is not (StationProjectStatus.Cancelled or StationProjectStatus.Completed)))
            return Rejected("station-base-not-cleared");
        if (command.RecoveryInventory == station.Inventory.Id) return Rejected("station-external-recovery-required");
        Inventory? destination = _lifecycle.Inventories.Get(command.RecoveryInventory);
        if (destination?.Custody is not InventoryCustody.PrincipalControlled) return Rejected("station-recovery-destination-unavailable");
        if (!_projectIds.TryPeek(out StationProjectId id)) return Rejected("station-identifiers-exhausted");
        IReadOnlyDictionary<MaterialId, Quantity> recovery = station.Definition.DismantleRecovery;
        if (station.Phase == StationPhase.Site)
        {
            var consumed = new Dictionary<MaterialId, Quantity>();
            foreach (ProjectState prior in station.Projects.Values.Where(project => project.Kind == StationProjectKind.BaseConstruction))
            foreach ((MaterialId material, Quantity amount) in prior.Consumed)
                consumed[material] = consumed.GetValueOrDefault(material, Quantity.Zero).Add(amount);
            recovery = StationDismantlingRecovery.Evaluate(recovery, consumed);
        }
        var project = new ProjectState(id, new([], station.Definition.DismantleWork))
        { Kind = StationProjectKind.Dismantling, RecoveryInventory = destination.Id };
        foreach ((MaterialId material, Quantity amount) in recovery) project.RecoveryOutputs.Add(material, amount);
        Quantity output = RecoveryCapacity(project);
        ReservationId? reservation = null;
        if (output != Quantity.Zero)
        {
            if (_inventoryCommit is null || !_inventoryCommit.TryPeekStationRecoveryReservationIdentity(out ReservationId next)
                || !_inventoryCommit.CanReserveStationRecoveryCapacity(destination, output, new(station.Id, id), next))
                return Rejected("station-insufficient-recovery-capacity");
            reservation = next;
        }
        PreparedInventoryCapacityChange protection = station.Inventory.PrepareCapacityChange(station.Inventory.Capacity,
            Quantity.Zero, Quantity.Zero) ?? throw new InvalidOperationException("An empty base cannot reject zero intake protection.");
        if (!station.Inventory.ApplyCapacityChange(protection)) throw new InvalidOperationException("Prepared base protection changed during commit.");
        if (reservation is { } claim) _inventoryCommit!.ApplyStationRecoveryCapacityReservation(destination, output, new(station.Id, id), claim);
        project.RecoveryReservation = reservation;
        station.Projects.Add(id, project);
        _projectIds.Allocate();
        station.Phase = StationPhase.Dismantling;
        station.Revision = checked(station.Revision + 1);
        return Changed(station, project);
    }

    /// <summary>Publishes external recovery and final removal together, retaining completed work if its destination is unavailable.</summary>
    private List<GameFactProposal> CompleteBaseDismantling(StationState station, ProjectState project, GameFactCause cause)
    {
        if (!RecoveryAvailable(station, project) || _canRemoveStationTargets?.Invoke(station.Entity) == false
            || !TryPrepareBaseEconomicRemoval(station, out PreparedStationModuleProvider? providers))
        {
            project.Status = StationProjectStatus.Waiting;
            project.Rate = 0;
            project.WorkNumerator = 0;
            return [];
        }
        if (!_lifecycle.CanRemoveEmptyStation(station.Id, station.Entity, station.Inventory))
            throw new InvalidOperationException("Final base removal lost its protected empty storage.");
        Inventory destination = _lifecycle.Inventories.Get(project.RecoveryInventory!.Value)!;
        ApplyModuleProvider(null, providers);
        List<GameFactProposal> facts = _removeStationTargets?.Invoke(station.Entity) ?? [];
        ReleaseRecovery(station, project);
        foreach ((MaterialId material, Quantity amount) in project.RecoveryOutputs)
            if (!destination.StoreFungible(_setup.Materials.GetRequired(material), amount).IsAccepted)
                throw new InvalidOperationException("Prepared base recovery could not be published.");
        project.Status = StationProjectStatus.Completed;
        project.Ships.Clear();
        project.Sources.Clear();
        project.ModuleBuilders.Clear();
        project.Rate = 0;
        StationSnapshot beforeRemoval = Capture().Single(item => item.Id == station.Id);
        _lifecycle.ApplyRemoveEmptyStation(station.Id, station.Entity, station.Inventory);
        _stations.Remove(station.Id);
        _stationRemovals.Add(station.Entity, new(beforeRemoval, station.CreatedAt, _agenda.CurrentTime,
            StationRemovalReason.Dismantled, cause));
        facts.Add(new(new(GameFactCommitCategory.EntityLifecycle, station.Entity.Value, project.Id.Value, 0),
            new StationProjectCompletedFact(station.Id, project.Id)));
        facts.Add(new(new(GameFactCommitCategory.EntityLifecycle, station.Entity.Value, project.Id.Value, 1),
            new StationRemovedFact(beforeRemoval, StationRemovalReason.Dismantled)));
        return facts;
    }
}
