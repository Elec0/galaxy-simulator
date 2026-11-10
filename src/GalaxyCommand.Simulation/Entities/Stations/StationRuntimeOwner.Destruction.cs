using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

internal sealed partial class StationRuntimeOwner
{
    private CargoDispositionOwner? _destructionCargo;
    private CargoDispositionPolicy? _destructionCargoPolicy;
    private PhysicalDefinitionCatalog? _destructionDefinitions;
    private DeterministicRandomOwner? _destructionRandom;
    private readonly List<StationDestructionReceipt> _destructionReceipts = [];
    private Func<StationId, IReadOnlyList<StationModuleId>, bool, IReadOnlyList<InventoryCommitmentRelease>, IPreparedStationWorkflowLoss?>? _prepareWorkflowLoss;

    /// <summary>Binds the fixed cargo/workflow owners after fresh composition or private restoration.</summary>
    internal void BindDestruction(CargoDispositionOwner? cargo, CargoDispositionPolicy? policy,
        PhysicalDefinitionCatalog definitions, DeterministicRandomOwner random,
        Func<StationId, IReadOnlyList<StationModuleId>, bool, IReadOnlyList<InventoryCommitmentRelease>, IPreparedStationWorkflowLoss?>? prepareWorkflowLoss = null)
    {
        _destructionCargo = cargo;
        _destructionCargoPolicy = policy;
        _destructionDefinitions = definitions;
        _destructionRandom = random;
        _prepareWorkflowLoss = prepareWorkflowLoss;
    }

    /// <summary>Prepares grouped station loss, every inventory claim, and cargo output before committing any owner.</summary>
    internal StationDestructionResult CommitDestruction(StationDestructionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Trigger is null || request.Station.Value == 0 || request.Occurrence == 0 || request.Modules is null
            || request.Modules.Any(module => module.Value == 0) || request.Modules.Distinct().Count() != request.Modules.Count)
            return FailedDestruction(StationDestructionFailure.InvalidInput);
        request = request with { Modules = Array.AsReadOnly(request.Modules.OrderBy(module => module.Value).ToArray()) };
        StationDestructionReceipt? previous = _destructionReceipts.FirstOrDefault(receipt => receipt.Request.Station == request.Station
            && receipt.Request.Trigger == request.Trigger && receipt.Request.Occurrence == request.Occurrence);
        if (previous is not null)
            return DestructionRequestsEqual(previous.Request, request) ? new(false) : FailedDestruction(StationDestructionFailure.ConflictingReplay);
        if (!_stations.TryGetValue(request.Station, out StationState? station) || station.Revision != request.ExpectedRevision)
            return FailedDestruction(StationDestructionFailure.StaleTarget);
        if ((!request.DestroyBase && request.Modules.Count == 0) || request.Modules.Any(module => !station.Modules.ContainsKey(module)))
            return FailedDestruction(StationDestructionFailure.InvalidInput);
        if (station.LossOccurrence == ulong.MaxValue || request.Occurrence != station.LossOccurrence + 1)
            return FailedDestruction(StationDestructionFailure.InvalidInput);
        if (_destructionCargo is null || _destructionCargoPolicy is null || _destructionDefinitions is null || _destructionRandom is null)
            return FailedDestruction(StationDestructionFailure.PolicyUnavailable);
        if (request.DestroyBase && _canRemoveStationTargets?.Invoke(station.Entity) == false)
            return FailedDestruction(StationDestructionFailure.OwnerConflict);

        bool committing = false;
        try
        {
            StationSnapshot before = Capture().Single(item => item.Id == station.Id);
            StationStorageLossWindow beforeWindow = station.LossWindow;
            InventoryCheckpoint stock = station.Inventory.CaptureCheckpoint();
            StationModuleId[] removed = (request.DestroyBase ? station.Modules.Keys.AsEnumerable() : request.Modules).OrderBy(module => module.Value).ToArray();
            ulong destroyedCapacity = 0;
            foreach (StationModuleId module in removed)
                if (station.Modules[module].IsCompleted)
                    destroyedCapacity = checked(destroyedCapacity + station.Modules[module].Definition.StorageCapacity.Units);
            var projected = new StationState(station.Id, station.Entity, station.Inventory, station.Principal, station.Definition, station.Position)
            { Phase = station.Phase, Revision = station.Revision, CreatedAt = station.CreatedAt };
            foreach (ModuleState module in station.Modules.Values.Where(module => !removed.Contains(module.Id))) projected.Modules.Add(module.Id, module);
            ProjectState[] retired = station.Projects.Values.Where(project => request.DestroyBase || project.ModuleId is { } module && removed.Contains(module))
                .Where(project => project.Status is not (StationProjectStatus.Completed or StationProjectStatus.Cancelled)).ToArray();
            _ = checked(station.Revision + 1);
            foreach (ProjectState project in retired) _ = project.Generation.Next();
            foreach (ProjectState project in station.Projects.Values.Where(project => !retired.Contains(project))) projected.Projects.Add(project.Id, project);
            (Quantity physical, Quantity enabled, Quantity? protection) = request.DestroyBase
                ? (Quantity.Zero, Quantity.Zero, null) : CalculateStorageLimits(projected);
            StationStorageLossSelection selection;
            if (request.DestroyBase)
                selection = new(StationStorageLossWindow.Empty, SelectAll(stock), station.Inventory.UsedCapacity);
            else if (destroyedCapacity == 0)
                selection = new(station.LossWindow, [], Quantity.Zero);
            else
            {
                if (_setup.StorageLossPolicy is null) return FailedDestruction(StationDestructionFailure.PolicyUnavailable);
                selection = StationStorageLossSelection.Evaluate(_setup.StorageLossPolicy, station.LossWindow, station.Id,
                    request.Occurrence, new(destroyedCapacity), physical, stock, _destructionDefinitions, _destructionRandom);
            }

            IPreparedStationWorkflowLoss? forced = _prepareWorkflowLoss?.Invoke(station.Id, removed, request.DestroyBase, []);
            if (_prepareWorkflowLoss is not null && forced is null) return FailedDestruction(StationDestructionFailure.OwnerConflict);
            InventoryCommitmentRelease[] forcedSource = (forced?.Releases ?? []).Where(release => ReferenceEquals(release.Inventory, station.Inventory)).ToArray();
            InventoryCommitmentRelease[] forcedOutgoing = forcedSource.Where(IsOutgoingRelease).ToArray();
            PreparedInventoryOutgoingLossCleanup? outgoing = station.Inventory.PrepareOutgoingLossCleanup(selection.Selected,
                _destructionDefinitions, forcedOutgoing);
            if (outgoing is null) return FailedDestruction(StationDestructionFailure.OwnerConflict);
            var incoming = new List<InventoryCommitmentRelease>(forcedSource.Where(release => !IsOutgoingRelease(release)));
            // A retired project's own recovery claim cannot survive its structural removal.
            foreach (ProjectState project in retired)
                if (project.RecoveryInventory == station.Inventory.Id && project.RecoveryReservation is { } recovery
                    && station.Inventory.GetPhysicalReservation(recovery) is { } reservation)
                {
                    var release = new InventoryCommitmentRelease.Physical(station.Inventory, reservation);
                    if (!incoming.Any(existing => SameRelease(existing, release))) incoming.Add(release);
                }
            ulong reserved = station.Inventory.ReservedCapacity.Units;
            foreach (InventoryCommitmentRelease release in incoming) reserved -= IncomingQuantity(release).Units;
            ulong retained = Math.Min(physical.Units, protection?.Units ?? physical.Units);
            ulong used = station.Inventory.UsedCapacity.Units - selection.RemovedCapacity.Units;
            foreach (InventoryIncomingCommitment commitment in station.Inventory.CaptureIncomingCommitments().Reverse())
            {
                InventoryCommitmentRelease release = IncomingRelease(station.Inventory, commitment);
                if (incoming.Any(existing => SameRelease(existing, release))) continue;
                if ((UInt128)used + reserved <= retained) break;
                incoming.Add(release);
                reserved -= commitment.Quantity.Units;
            }
            if ((UInt128)used + reserved > retained) return FailedDestruction(StationDestructionFailure.OwnerConflict);
            InventoryCommitmentRelease[] required = outgoing.Releases.Concat(incoming).ToArray();
            IPreparedStationWorkflowLoss? workflows = _prepareWorkflowLoss?.Invoke(station.Id, removed, request.DestroyBase, required);
            if (_prepareWorkflowLoss is not null && workflows is null) return FailedDestruction(StationDestructionFailure.OwnerConflict);
            if (!CanOwnWorkflowReleases(station, required, workflows)) return FailedDestruction(StationDestructionFailure.OwnerConflict);
            var key = new CargoDispositionKey(request.Trigger, station.Entity, request.Occurrence);
            var cargoRequest = new CargoDispositionRequest(key, station.Inventory.Id, station.Position,
                request.DestroyBase ? CargoLossCause.EntityDestruction : CargoLossCause.StorageModuleLoss,
                _destructionCargoPolicy, selection.Selected, outgoing.Cleanup);
            CargoDispositionPreparation preparedCargo = _destructionCargo.Prepare(cargoRequest);
            if (preparedCargo.Prepared is not { } cargo) return FailedDestruction(DestructionFailureFromCargo(preparedCargo.Failure));
            PreparedStationLossCapacityChange? capacity = station.Inventory.PrepareStationLossCapacity(cargo.SourceLoss,
                _destructionDefinitions, incoming, physical, enabled, protection);
            if (capacity is null || !station.Inventory.CanCommitStationLossCapacity(capacity)
                || workflows is not null && !workflows.CanCommit(cargo.SourceLoss)
                || retired.Any(project => project.Pending is { } pending && _agenda.CheckCancellation(pending, project.Generation,
                    new GameEvent.StationWork(station.Id, project.Id)) != AgendaCancellationCheck.Matches))
                return FailedDestruction(StationDestructionFailure.OwnerConflict);
            var facts = removed.Select(module => new GameFactProposal(new(GameFactCommitCategory.EntityLifecycle,
                station.Entity.Value, module.Value, 2), new StationModuleRemovedFact(station.Id, module))).ToList();
            var invalidated = new List<InventoryCommitmentRelease>(required);
            foreach (InventoryCommitmentRelease release in workflows?.Releases ?? [])
                if (!invalidated.Any(existing => SameRelease(existing, release))) invalidated.Add(release);
            foreach (ProjectState project in retired)
                if (project.RecoveryInventory is { } destination && project.RecoveryReservation is { } claim
                    && _lifecycle.Inventories.Get(destination)?.GetPhysicalReservation(claim) is { } recovery)
                {
                    var release = new InventoryCommitmentRelease.Physical(_lifecycle.Inventories.Get(destination)!, recovery);
                    if (!invalidated.Any(existing => SameRelease(existing, release))) invalidated.Add(release);
                }
            int invalidationOrdinal = 4;
            foreach (InventoryCommitmentRelease release in invalidated.OrderBy(item => item.Inventory.Id.Value)
                .ThenBy(item => CommitmentFact(station, item).Kind).ThenBy(item => CommitmentFact(station, item).ReservationValue))
                facts.Add(new(new(GameFactCommitCategory.EntityLifecycle, station.Entity.Value, 0, invalidationOrdinal++),
                    CommitmentFact(station, release)));
            foreach (ProjectState project in retired)
                facts.Add(new(new(GameFactCommitCategory.EntityLifecycle, station.Entity.Value, project.Id.Value, 3),
                    new StationProjectChangedFact(station.Id, project.Id, StationProjectStatus.Cancelled)));
            if (request.DestroyBase) facts.Add(new(new(GameFactCommitCategory.EntityLifecycle, station.Entity.Value, 0, 3),
                new StationRemovedFact(before, StationRemovalReason.Destroyed)));
            int targetFactCapacity = request.DestroyBase ? _stationTargetFactCapacity?.Invoke(station.Entity) ?? 0 : 0;
            int targetAgendaCapacity = request.DestroyBase ? _stationTargetAgendaCapacity?.Invoke(station.Entity) ?? 0 : 0;
            // Cargo owns source removal and instance-preserving publication. The
            // callback joins all prevalidated causal owners before the single fact publication.
            committing = true;
            CargoDispositionCommitResult result = _destructionCargo.Commit(cargo, facts, () =>
            {
                workflows?.ApplyAfterSourceLoss(cargo.SourceLoss);
                foreach (InventoryCommitmentRelease release in incoming)
                    if (release.CanCommit()) ApplyIncomingRelease(release);
                if (!station.Inventory.ApplyStationLossCapacity(capacity))
                    throw new InvalidOperationException("Prepared station loss capacity did not match the committed source delta.");
                foreach (ProjectState project in retired)
                {
                    CancelPending(station, project);
                    ReleaseRecovery(station, project);
                    project.Status = StationProjectStatus.Cancelled;
                    project.Ships.Clear(); project.Sources.Clear(); project.ModuleBuilders.Clear();
                    project.Rate = 0; project.WorkNumerator = 0;
                }
                foreach (StationModuleId module in removed) station.Modules.Remove(module);
                foreach (ProjectState project in station.Projects.Values) project.ModuleBuilders.ExceptWith(removed);
                station.LossWindow = selection.Window;
                station.LossOccurrence = request.Occurrence;
                station.Revision = checked(station.Revision + 1);
                if (request.DestroyBase)
                {
                    facts.AddRange(_removeStationTargets?.Invoke(station.Entity) ?? []);
                    _lifecycle.ApplyRemoveEmptyStation(station.Id, station.Entity, station.Inventory);
                    _stations.Remove(station.Id);
                    _stationRemovals.Add(station.Entity, new(before, station.CreatedAt, _agenda.CurrentTime,
                        StationRemovalReason.Destroyed, Cause(request.Trigger)));
                }
                _destructionReceipts.Add(new(request, before, stock, beforeWindow, selection.Window, key, _agenda.CurrentTime));
            }, targetFactCapacity, targetAgendaCapacity);
            return result.WasApplied ? new(true) : FailedDestruction(DestructionFailureFromCargo(result.Failure));
        }
        catch (OverflowException) when (!committing) { return FailedDestruction(StationDestructionFailure.NumericExhaustion); }
    }

    /// <summary>Rejects any cleanup without a concrete station recovery intent or its exact owning workflow effect.</summary>
    private bool CanOwnWorkflowReleases(StationState station, IReadOnlyList<InventoryCommitmentRelease> required,
        IPreparedStationWorkflowLoss? workflows)
    {
        foreach (InventoryCommitmentRelease release in required)
        {
            if (workflows?.Releases.Any(candidate => SameRelease(candidate, release)) == true) continue;
            if (release is InventoryCommitmentRelease.Physical { Reservation.Owner: ReservationOwner.StationProject owner } physical
                && _stations.TryGetValue(owner.StationId, out StationState? target)
                && target.Projects.TryGetValue(owner.ProjectId, out ProjectState? project)
                && project.RecoveryInventory == station.Inventory.Id && project.RecoveryReservation == physical.Reservation.Id) continue;
            return false;
        }
        return workflows is not null || station.Modules.Values.All(module => module.FacilityId is null);
    }

    private static ReadOnlyCollection<CargoDispositionSelection> SelectAll(InventoryCheckpoint stock) => Array.AsReadOnly(
        stock.FungibleHoldings.Select(held => (CargoDispositionSelection)new CargoDispositionSelection.Fungible(held.DefinitionKey, held.Quantity))
            .Concat(stock.DiscreteItems.Select(item => new CargoDispositionSelection.Discrete(item.DefinitionKey, item.Id)))
            .OrderBy(selection => selection.DefinitionKey.ToString(), StringComparer.Ordinal).ToArray());

    private static bool IsOutgoingRelease(InventoryCommitmentRelease release) => release is InventoryCommitmentRelease.Material
        || release is InventoryCommitmentRelease.Physical { Reservation.Subject: not PhysicalReservationSubject.IncomingCapacity };

    private static StationCommitmentInvalidatedFact CommitmentFact(StationState station, InventoryCommitmentRelease release) => release switch
    {
        InventoryCommitmentRelease.Material material => new(station.Id, station.Principal, release.Inventory.Id,
            StationCommitmentKind.Material, material.Reservation.Id.Value, material.Reservation.Owner),
        InventoryCommitmentRelease.Capacity capacity => new(station.Id, station.Principal, release.Inventory.Id,
            StationCommitmentKind.Capacity, capacity.Reservation.Id.Value, capacity.Reservation.Owner),
        InventoryCommitmentRelease.Physical physical => new(station.Id, station.Principal, release.Inventory.Id,
            StationCommitmentKind.Physical, physical.Reservation.Id.Value, physical.Reservation.Owner),
        _ => throw new InvalidOperationException("Unknown station commitment kind."),
    };

    private static bool SameRelease(InventoryCommitmentRelease left, InventoryCommitmentRelease right) => ReferenceEquals(left.Inventory, right.Inventory) && (left, right) switch
    {
        (InventoryCommitmentRelease.Material a, InventoryCommitmentRelease.Material b) => ReferenceEquals(a.Reservation, b.Reservation),
        (InventoryCommitmentRelease.Capacity a, InventoryCommitmentRelease.Capacity b) => ReferenceEquals(a.Reservation, b.Reservation),
        (InventoryCommitmentRelease.Physical a, InventoryCommitmentRelease.Physical b) => ReferenceEquals(a.Reservation, b.Reservation),
        _ => false,
    };

    private static InventoryCommitmentRelease IncomingRelease(Inventory inventory, InventoryIncomingCommitment commitment) => commitment switch
    {
        InventoryIncomingCommitment.Legacy legacy => new InventoryCommitmentRelease.Capacity(inventory, legacy.Reservation),
        InventoryIncomingCommitment.Physical physical => new InventoryCommitmentRelease.Physical(inventory, physical.Reservation),
        _ => throw new InvalidOperationException("Unknown incoming station claim."),
    };

    private static Quantity IncomingQuantity(InventoryCommitmentRelease release) => release switch
    {
        InventoryCommitmentRelease.Capacity capacity => capacity.Reservation.Quantity,
        InventoryCommitmentRelease.Physical { Reservation.Subject: PhysicalReservationSubject.IncomingCapacity incoming } => incoming.Quantity,
        _ => throw new InvalidOperationException("Expected an incoming station claim."),
    };

    /// <summary>Releases only incoming claims; their owning workflow consequences were prepared independently.</summary>
    private static void ApplyIncomingRelease(InventoryCommitmentRelease release)
    {
        if (release is InventoryCommitmentRelease.Capacity capacity) capacity.Inventory.ReleaseCapacity(capacity.Reservation.Id);
        else if (release is InventoryCommitmentRelease.Physical physical)
        {
            if (!physical.Inventory.ReleasePhysicalReservation(physical.Reservation.Id, physical.Reservation.Owner).IsAccepted)
                throw new InvalidOperationException("Prepared incoming station release changed during commit.");
        }
        else throw new InvalidOperationException("Expected an incoming station claim.");
    }

    private static GameFactCause Cause(CargoDispositionTrigger trigger) => trigger switch
    {
        CargoDispositionTrigger.Command command => new CommandFactCause(command.Sequence),
        CargoDispositionTrigger.Scheduled scheduled => new ScheduledEventFactCause(scheduled.Key),
        _ => throw new InvalidOperationException("Unknown station destruction cause."),
    };

    private static bool DestructionRequestsEqual(StationDestructionRequest a, StationDestructionRequest b) =>
        a.Trigger == b.Trigger && a.Station == b.Station && a.ExpectedRevision == b.ExpectedRevision && a.DestroyBase == b.DestroyBase
        && a.Occurrence == b.Occurrence && a.Modules.SequenceEqual(b.Modules);

    private static StationDestructionResult FailedDestruction(StationDestructionFailure reason) => new(false, reason);

    private static StationDestructionFailure DestructionFailureFromCargo(CargoDispositionFailure? failure) => failure is
        CargoDispositionFailure.NumericExhaustion or CargoDispositionFailure.IdentifierExhausted or CargoDispositionFailure.FactCapacityExhausted
            ? StationDestructionFailure.NumericExhaustion : StationDestructionFailure.OwnerConflict;
}
