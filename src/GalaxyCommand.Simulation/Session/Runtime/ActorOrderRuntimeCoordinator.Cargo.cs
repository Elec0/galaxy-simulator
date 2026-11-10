namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    private CargoDispositionOwner? _cargo;
    private readonly PhysicalDefinitionCatalog _cargoDefinitions;
    private readonly HashSet<SystemId> _cargoSystemIds;

    /// <summary>Attaches fresh cargo authority only for an explicitly selected policy and declared physical definitions.</summary>
    internal void InitializeCargoAuthority(DeterministicRandomOwner random)
    {
        if (_cargoPolicy is not null)
        {
            _cargo = new CargoDispositionOwner(_lifecycle, _cargoDefinitions, _cargoPolicy, random, _agenda, _facts);
            _cargoAttempts = new CargoCollectionAttemptOwner(_lifecycle, _cargo, _cargoPolicy, _inventoryCommit, _movement, _agenda);
        }
        _stations?.BindDestruction(_cargo, _cargoPolicy, _cargoDefinitions, random, PrepareStationEconomicLoss);
    }

    /// <summary>Joins privately restored cargo, lifecycle and agenda authority before publishing the assembled session.</summary>
    internal CheckpointValidationFailure? RestoreCargoAuthority(DeterministicRandomOwner random, CargoDispositionOwnerCheckpoint? checkpoint,
        CargoCollectionAttemptOwnerCheckpoint? attempts, IReadOnlyList<CargoCollectionTarget>? targets)
    {
        if (_cargoPolicy is null)
        {
            _stations?.BindDestruction(null, null, _cargoDefinitions, random, PrepareStationEconomicLoss);
            return checkpoint is null && attempts is null && (targets is null || targets.Count == 0) && _lifecycle.CaptureCargoObjects().Count == 0
                ? _stations?.ValidateDestructionCargoHistory()
                : new CheckpointValidationFailure("$.checkpoint.cargo", "Cargo state requires an explicitly selected policy.");
        }
        if (checkpoint is null) return new CheckpointValidationFailure("$.checkpoint.cargo", "A cargo-enabled session requires its cargo authority checkpoint.");
        CheckpointResult<CargoDispositionOwner> restored = CargoDispositionOwner.Restore(checkpoint, _lifecycle,
            _cargoDefinitions, _cargoPolicy, random, _agenda, _facts);
        if (!restored.IsSuccess) return restored.Failure;
        _cargo = restored.Value;
        if (attempts is null || targets is null)
            return new("$.checkpoint.cargoCollectionAttempts", "Cargo-enabled sessions require retained collection authority.");
        CheckpointResult<CargoCollectionAttemptOwner> restoredAttempts = CargoCollectionAttemptOwner.Restore(
            attempts, _lifecycle, _cargo!, _cargoPolicy, _inventoryCommit, _movement, _agenda);
        if (!restoredAttempts.IsSuccess) return restoredAttempts.Failure;
        _cargoAttempts = restoredAttempts.Value;
        foreach (CargoCollectionTarget target in targets)
            if (target is null || !_cargoTargets.TryAdd(target.OrderId, target))
                return new("$.checkpoint.cargoCollectionTargets", "Collection targets must be non-null and unique by order.");
        _stations?.BindDestruction(_cargo, _cargoPolicy, _cargoDefinitions, random, PrepareStationEconomicLoss);
        return _stations?.ValidateDestructionCargoHistory();
    }

    /// <summary>Routes only economic claims to their owner; station recovery claims retain station authority.</summary>
    private IPreparedStationWorkflowLoss? PrepareStationEconomicLoss(StationId station,
        IReadOnlyList<StationModuleId> modules, bool destroyBase, IReadOnlyList<InventoryCommitmentRelease> releases)
    {
        if (_economy is null) return new EmptyStationWorkflowLoss();
        PreparedStationEconomyLoss? effect = _economy.PrepareStationDestruction(station, modules, destroyBase, releases.Where(release => release switch
        {
            InventoryCommitmentRelease.Material material => IsEconomicOwner(material.Reservation.Owner),
            InventoryCommitmentRelease.Capacity capacity => IsEconomicOwner(capacity.Reservation.Owner),
            InventoryCommitmentRelease.Physical physical => IsEconomicOwner(physical.Reservation.Owner),
            _ => false,
        }).ToArray());
        if (effect is null) return null;
        var cancellations = new List<StationProviderCancellation>();
        foreach (StationProviderAgendaEvent entry in effect.Cancellations)
        {
            if (!_agenda.TryFindPending(entry.Timestamp, EventPhase.PhysicalCompletion, entry.Generation,
                new GameEvent.Economic(entry.Event), out EventKey key)) return null;
            cancellations.Add(new(key, entry));
        }
        return new PreparedStationWorkflowAgendaLoss(_agenda, effect, cancellations.AsReadOnly());
    }

    private static bool IsEconomicOwner(ReservationOwner owner) => owner is ReservationOwner.ProductionJob
        or ReservationOwner.ConstructionOrder or ReservationOwner.TransportJob;

    /// <summary>
    /// Commits a causal owner's prepared loss through the session health guard. This
    /// internal domain handoff is not a voluntary-ejection or ship-destruction command.
    /// Station accounting and workflow invalidation join at their causal owner.
    /// </summary>
    internal CargoDispositionCommitResult CommitCargoDisposition(CargoDispositionRequest request)
    {
        ThrowIfUnhealthy();
        if (_cargo is null) return new(null, CargoDispositionFailure.PolicyMismatch, false);
        try
        {
            CargoDispositionPreparation preparation = _cargo.Prepare(request);
            if (preparation.Receipt is not null) return new(preparation.Receipt, null, false);
            if (preparation.Prepared is { } prepared && (!_cargoSystemIds.Contains(prepared.Request.SourcePosition.SystemId)
                || _lifecycle.Entities.GetShipId(prepared.Request.Key.SourceEntityId) is { } ship
                    && _movement.PositionAt(ship, CurrentTime) != prepared.Request.SourcePosition))
                return new(null, CargoDispositionFailure.InvalidInput, false);
            return preparation.Prepared is null ? new(null, preparation.Failure, false) : _cargo.Commit(preparation.Prepared);
        }
        catch { _isPoisoned = true; throw; }
    }

    internal IReadOnlyList<CargoLiveObject> CaptureCargoAuthority() => _cargo?.CaptureObjects() ?? Array.Empty<CargoLiveObject>();
}
