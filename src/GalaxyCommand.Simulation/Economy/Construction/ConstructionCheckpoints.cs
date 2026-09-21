using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

internal sealed class RestoredConstructionOwner
{
    private readonly ReadOnlyDictionary<FacilityId, ConstructionProcess> _processes;

    internal RestoredConstructionOwner(
        ConstructionIdSequences ids,
        IDictionary<FacilityId, ConstructionProcess> processes)
    {
        Ids = ids;
        _processes = new ReadOnlyDictionary<FacilityId, ConstructionProcess>(
            new SortedDictionary<FacilityId, ConstructionProcess>(
                processes,
                EntityIdComparer<FacilityId>.Instance));
    }

    internal ConstructionIdSequences Ids { get; }

    internal IReadOnlyDictionary<FacilityId, ConstructionProcess> Processes => _processes;

    internal ConstructionOwnerCheckpoint CaptureCheckpoint() =>
        new(
            Ids.CaptureCheckpoint(),
            _processes.Values.Select(process => process.CaptureCheckpoint()).ToArray());
}

internal static class ConstructionCheckpointRestore
{
    private const string Path = "$.checkpoint.economy.construction";

    /// <summary>
    /// Validates construction state against shared inventory and content owners,
    /// then restores it without replaying any workflow transition.
    /// </summary>
    internal static CheckpointResult<RestoredConstructionOwner> Restore(
        ConstructionOwnerCheckpoint checkpoint,
        InventoryRegistry inventories,
        ConstructionDesignCatalog designs)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(designs);
        CheckpointResult<ConstructionIdSequences> idsResult =
            ConstructionIdSequences.RestoreCheckpoint(checkpoint.OrderIds);
        if (!idsResult.IsSuccess)
        {
            return Rejected($"{Path}.orderIds.nextValue", idsResult.Failure!.Message);
        }

        var facilityIds = new HashSet<FacilityId>();
        var orderIds = new HashSet<ConstructionOrderId>();
        var processes = new Dictionary<FacilityId, ConstructionProcess>();
        for (int index = 0; index < checkpoint.Processes.Count; index++)
        {
            ConstructionProcessCheckpoint? process = checkpoint.Processes[index];
            string processPath = $"{Path}.processes[{index}]";
            if (process is null || process.FacilityId.Value == 0
                || !facilityIds.Add(process.FacilityId))
            {
                return Rejected(
                    $"{processPath}.facilityId",
                    "The construction facility identity is missing or duplicated.");
            }

            Inventory? inventory = inventories.Get(process.InventoryId);
            if (inventory is null)
            {
                return Rejected(
                    $"{processPath}.inventoryId",
                    "The construction inventory is not restored.");
            }

            if (process.Throughput.UnitsPerSecond == 0)
            {
                return Rejected(
                    $"{processPath}.throughput",
                    "Construction throughput must be positive.");
            }

            CheckpointResult<ConstructionProcess> processResult = RestoreProcess(
                process,
                inventory,
                designs,
                checkpoint.OrderIds,
                orderIds,
                processPath);
            if (!processResult.IsSuccess)
            {
                return CheckpointResult<RestoredConstructionOwner>.Rejected(
                    processResult.Failure!);
            }

            processes.Add(process.FacilityId, processResult.Value!);
        }

        CheckpointValidationFailure? reservationFailure =
            ValidateAllConstructionReservations(processes, inventories);
        if (reservationFailure is not null)
        {
            return CheckpointResult<RestoredConstructionOwner>.Rejected(
                reservationFailure);
        }

        return CheckpointResult<RestoredConstructionOwner>.Success(
            new RestoredConstructionOwner(idsResult.Value!, processes));
    }

    private static CheckpointResult<ConstructionProcess> RestoreProcess(
        ConstructionProcessCheckpoint checkpoint,
        Inventory inventory,
        ConstructionDesignCatalog designs,
        IdSequenceCheckpoint orderSequence,
        HashSet<ConstructionOrderId> globalOrderIds,
        string path)
    {
        var orders = new SortedDictionary<ConstructionOrderId, ConstructionOrder>(
            EntityIdComparer<ConstructionOrderId>.Instance);
        for (int index = 0; index < checkpoint.Orders.Count; index++)
        {
            ConstructionOrderCheckpoint? saved = checkpoint.Orders[index];
            string orderPath = $"{path}.orders[{index}]";
            if (saved is null || !WasAllocated(saved.Id.Value, orderSequence)
                || !globalOrderIds.Add(saved.Id))
            {
                return RejectedProcess(
                    $"{orderPath}.id",
                    "The construction order identity is invalid, duplicated, or unallocated.");
            }

            ConstructionDesign? design = designs.Get(saved.DesignId);
            if (design is null)
            {
                return RejectedProcess(
                    $"{orderPath}.designId",
                    "The construction design is not present in the restored catalog.");
            }

            if (!Enum.IsDefined(saved.Status))
            {
                return RejectedProcess(
                    $"{orderPath}.status",
                    "The construction order status is undefined.");
            }

            bool requiresCompletion = saved.Status is ConstructionOrderStatus.Running
                or ConstructionOrderStatus.AwaitingMaterialization
                or ConstructionOrderStatus.Completed;
            if (requiresCompletion != saved.CompletesAt.HasValue)
            {
                return RejectedProcess(
                    $"{orderPath}.completesAt",
                    "The construction completion time disagrees with its status.");
            }

            var order = new ConstructionOrder(saved.Id, design)
            {
                Status = saved.Status,
                CompletesAt = saved.CompletesAt,
                Generation = saved.Generation,
            };
            CheckpointValidationFailure? reservationFailure = RestoreReservations(
                saved,
                order,
                inventory,
                orderPath);
            if (reservationFailure is not null)
            {
                return CheckpointResult<ConstructionProcess>.Rejected(reservationFailure);
            }

            if (saved.Status != ConstructionOrderStatus.WaitingForInputs
                && saved.Reservations.Count != 0)
            {
                return RejectedProcess(
                    $"{orderPath}.reservations",
                    "Only an order waiting for inputs may retain reservations.");
            }

            orders.Add(order.Id, order);
        }

        CheckpointValidationFailure? inventoryFailure =
            ValidateCompleteReservationLinks(orders, inventory, path);
        if (inventoryFailure is not null)
        {
            return CheckpointResult<ConstructionProcess>.Rejected(inventoryFailure);
        }

        CheckpointValidationFailure? assignmentFailure = ValidateAssignments(
            checkpoint,
            orders,
            path);
        if (assignmentFailure is not null)
        {
            return CheckpointResult<ConstructionProcess>.Rejected(assignmentFailure);
        }

        CheckpointResult<IReadOnlyList<ConstructionMaterializationEffect>> pendingResult =
            RestorePending(checkpoint, orders, path);
        if (!pendingResult.IsSuccess)
        {
            return CheckpointResult<ConstructionProcess>.Rejected(pendingResult.Failure!);
        }

        CheckpointResult<IReadOnlyList<ConstructionMaterializationReceipt>> receiptResult =
            RestoreReceipts(checkpoint, orders, path);
        if (!receiptResult.IsSuccess)
        {
            return CheckpointResult<ConstructionProcess>.Rejected(receiptResult.Failure!);
        }

        HashSet<ConstructionOrderId> pendingIds = pendingResult.Value!
            .Select(effect => effect.OrderId)
            .ToHashSet();
        HashSet<ConstructionOrderId> receiptIds = receiptResult.Value!
            .Select(receipt => receipt.Effect.OrderId)
            .ToHashSet();
        if (orders.Values.Any(order =>
                order.Status == ConstructionOrderStatus.AwaitingMaterialization
                    != pendingIds.Contains(order.Id))
            || orders.Values.Any(order =>
                order.Status == ConstructionOrderStatus.Completed
                    != receiptIds.Contains(order.Id)))
        {
            return RejectedProcess(
                $"{path}.orders",
                "Materialization state does not completely partition awaiting and completed orders.");
        }

        return CheckpointResult<ConstructionProcess>.Success(
            ConstructionProcess.RestoreDirect(
                checkpoint.FacilityId,
                checkpoint.InventoryId,
                checkpoint.Throughput,
                orders,
                checkpoint.ActiveOrderId,
                checkpoint.QueuedOrderIds,
                pendingResult.Value!,
                receiptResult.Value!));
    }

    private static CheckpointValidationFailure? ValidateAssignments(
        ConstructionProcessCheckpoint checkpoint,
        IReadOnlyDictionary<ConstructionOrderId, ConstructionOrder> orders,
        string path)
    {
        if (checkpoint.ActiveOrderId is { } activeId
            && (!orders.TryGetValue(activeId, out ConstructionOrder? active)
                || active.Status is not (
                    ConstructionOrderStatus.WaitingForInputs
                    or ConstructionOrderStatus.Running)))
        {
            return new CheckpointValidationFailure(
                $"{path}.activeOrderId",
                "The active construction order is missing or cannot be active.");
        }

        if (checkpoint.ActiveOrderId is null && checkpoint.QueuedOrderIds.Count != 0)
        {
            return new CheckpointValidationFailure(
                $"{path}.activeOrderId",
                "A non-empty construction queue requires its promoted active order.");
        }

        var queued = new HashSet<ConstructionOrderId>();
        for (int index = 0; index < checkpoint.QueuedOrderIds.Count; index++)
        {
            ConstructionOrderId queuedId = checkpoint.QueuedOrderIds[index];
            if (!orders.TryGetValue(queuedId, out ConstructionOrder? queuedOrder)
                || queuedOrder.Status != ConstructionOrderStatus.WaitingForInputs
                || queuedId == checkpoint.ActiveOrderId
                || !queued.Add(queuedId))
            {
                return new CheckpointValidationFailure(
                    $"{path}.queuedOrderIds[{index}]",
                    "A queued order is missing, duplicated, active, or not waiting.");
            }
        }

        foreach (ConstructionOrder order in orders.Values)
        {
            bool assigned = order.Id == checkpoint.ActiveOrderId || queued.Contains(order.Id);
            bool assignable = order.Status is ConstructionOrderStatus.WaitingForInputs
                or ConstructionOrderStatus.Running;
            if (assigned != assignable)
            {
                return new CheckpointValidationFailure(
                    $"{path}.orders",
                    "Every live order must be active or queued and settled orders cannot be assigned.");
            }
        }

        return null;
    }

    private static CheckpointResult<IReadOnlyList<ConstructionMaterializationEffect>>
        RestorePending(
            ConstructionProcessCheckpoint checkpoint,
            SortedDictionary<ConstructionOrderId, ConstructionOrder> orders,
            string path)
    {
        var effects = new SortedDictionary<ConstructionOrderId, ConstructionMaterializationEffect>(
            EntityIdComparer<ConstructionOrderId>.Instance);
        for (int index = 0; index < checkpoint.PendingMaterializations.Count; index++)
        {
            ConstructionMaterializationEffect? effect =
                checkpoint.PendingMaterializations[index];
            string effectPath = $"{path}.pendingMaterializations[{index}]";
            if (effect is null || !orders.TryGetValue(effect.OrderId, out ConstructionOrder? order)
                || order.Status != ConstructionOrderStatus.AwaitingMaterialization
                || !EffectMatches(checkpoint, order, effect)
                || !effects.TryAdd(effect.OrderId, effect))
            {
                return RejectedEffects(
                    effectPath,
                    "The pending materialization is invalid, duplicated, or disagrees with its order.");
            }
        }

        return CheckpointResult<IReadOnlyList<ConstructionMaterializationEffect>>.Success(
            effects.Values.ToArray());
    }

    private static CheckpointResult<IReadOnlyList<ConstructionMaterializationReceipt>>
        RestoreReceipts(
            ConstructionProcessCheckpoint checkpoint,
            SortedDictionary<ConstructionOrderId, ConstructionOrder> orders,
            string path)
    {
        var receipts = new SortedDictionary<ConstructionOrderId, ConstructionMaterializationReceipt>(
            EntityIdComparer<ConstructionOrderId>.Instance);
        for (int index = 0; index < checkpoint.MaterializationReceipts.Count; index++)
        {
            ConstructionMaterializationReceiptCheckpoint? saved =
                checkpoint.MaterializationReceipts[index];
            ConstructionMaterializationEffect? effect = saved?.Effect;
            string receiptPath = $"{path}.materializationReceipts[{index}]";
            if (saved is null || effect is null
                || !orders.TryGetValue(effect.OrderId, out ConstructionOrder? order)
                || order.Status != ConstructionOrderStatus.Completed
                || !EffectMatches(checkpoint, order, effect)
                || receipts.ContainsKey(effect.OrderId))
            {
                return RejectedReceipts(
                    receiptPath,
                    "The materialization receipt is invalid, duplicated, or disagrees with its order.");
            }

            ConstructionMaterializationIdentity? identity = null;
            if (saved.ShipIdentity is { } ship)
            {
                if (ship.EntityId.Value == 0 || ship.ShipId.Value == 0
                    || ship.CargoInventoryId.Value == 0)
                {
                    return RejectedReceipts(
                        $"{receiptPath}.shipIdentity",
                        "The materialized ship identity is invalid.");
                }

                identity = new ConstructionMaterializationIdentity.Ship(
                    ship.EntityId,
                    ship.ShipId,
                    ship.CargoInventoryId);
            }

            receipts.Add(
                effect.OrderId,
                new ConstructionMaterializationReceipt(effect, identity));
        }

        return CheckpointResult<IReadOnlyList<ConstructionMaterializationReceipt>>.Success(
            receipts.Values.ToArray());
    }

    private static bool EffectMatches(
        ConstructionProcessCheckpoint process,
        ConstructionOrder order,
        ConstructionMaterializationEffect effect) =>
        effect.FacilityId == process.FacilityId
        && effect.OrderId == order.Id
        && effect.DesignId == order.DesignId
        && effect.Generation == order.Generation
        && effect.CompletedAt >= order.CompletesAt!.Value;

    private static CheckpointValidationFailure? RestoreReservations(
        ConstructionOrderCheckpoint checkpoint,
        ConstructionOrder order,
        Inventory inventory,
        string path)
    {
        var ids = new HashSet<ReservationId>();
        foreach ((ConstructionReservationLinkCheckpoint? link, int index) in
                 checkpoint.Reservations.Select((value, index) => (value, index)))
        {
            Reservation? reservation = link is null
                ? null
                : inventory.GetReservation(link.ReservationId);
            if (link is null || link.MaterialId.Value == 0 || !ids.Add(link.ReservationId)
                || reservation is null
                || reservation.MaterialId != link.MaterialId
                || reservation.Owner is not ReservationOwner.ConstructionOrder owner
                || owner.OrderId != order.Id
                || !order.Design.Recipe.Inputs.ContainsKey(link.MaterialId))
            {
                return new CheckpointValidationFailure(
                    $"{path}.reservations[{index}]",
                    "The order reservation link disagrees with shared inventory authority.");
            }

            order.AddReservation(link.MaterialId, link.ReservationId);
        }

        if (order.Design.Recipe.Inputs.Any(input =>
                order.ReservedInput(inventory, input.Key) > input.Value))
        {
            return new CheckpointValidationFailure(
                $"{path}.reservations",
                "The order reserves more input than its construction recipe requires.");
        }

        return null;
    }

    /// <summary>
    /// Proves that every construction reservation in the shared inventory owner
    /// names a restored order, its configured inventory, and an explicit link.
    /// </summary>
    private static CheckpointValidationFailure? ValidateAllConstructionReservations(
        IReadOnlyDictionary<FacilityId, ConstructionProcess> processes,
        InventoryRegistry inventories)
    {
        var owners = new Dictionary<ConstructionOrderId, (InventoryId, HashSet<ReservationId>)>();
        foreach (ConstructionProcess process in processes.Values)
        {
            foreach (ConstructionOrderCheckpoint? order in process.CaptureCheckpoint().Orders)
            {
                owners.Add(
                    order!.Id,
                    (
                        process.InventoryId,
                        order.Reservations.Select(link => link!.ReservationId).ToHashSet()));
            }
        }

        foreach (InventoryCheckpoint? inventory in inventories.CaptureCheckpoint().Inventories)
        {
            foreach (Reservation? reservation in inventory!.Reservations)
            {
                if (reservation?.Owner is ReservationOwner.ConstructionOrder owner
                    && (!owners.TryGetValue(owner.OrderId, out var expected)
                        || expected.Item1 != inventory.Id
                        || !expected.Item2.Contains(reservation.Id)))
                {
                    return new CheckpointValidationFailure(
                        $"{Path}.processes",
                        "A shared construction reservation is orphaned or bound to the wrong process inventory.");
                }
            }
        }

        return null;
    }

    private static CheckpointValidationFailure? ValidateCompleteReservationLinks(
        IReadOnlyDictionary<ConstructionOrderId, ConstructionOrder> orders,
        Inventory inventory,
        string path)
    {
        HashSet<ReservationId> linked = orders.Values
            .SelectMany(order => order.AllReservationIds())
            .ToHashSet();
        foreach (Reservation reservation in inventory.CaptureCheckpoint().Reservations)
        {
            if (reservation.Owner is ReservationOwner.ConstructionOrder owner
                && orders.ContainsKey(owner.OrderId)
                && !linked.Contains(reservation.Id))
            {
                return new CheckpointValidationFailure(
                    $"{path}.orders",
                    "A construction-owned reservation is missing from its order links.");
            }
        }

        return null;
    }

    private static bool WasAllocated(ulong value, IdSequenceCheckpoint sequence) =>
        value != 0 && (sequence.NextValue is not { } next || value < next);

    private static CheckpointResult<T> Reject<T>(string path, string message)
        where T : class =>
        CheckpointResult<T>.Rejected(new CheckpointValidationFailure(path, message));

    private static CheckpointResult<RestoredConstructionOwner> Rejected(
        string path,
        string message) => Reject<RestoredConstructionOwner>(path, message);

    private static CheckpointResult<ConstructionProcess> RejectedProcess(
        string path,
        string message) => Reject<ConstructionProcess>(path, message);

    private static CheckpointResult<IReadOnlyList<ConstructionMaterializationEffect>>
        RejectedEffects(string path, string message) =>
            Reject<IReadOnlyList<ConstructionMaterializationEffect>>(path, message);

    private static CheckpointResult<IReadOnlyList<ConstructionMaterializationReceipt>>
        RejectedReceipts(string path, string message) =>
            Reject<IReadOnlyList<ConstructionMaterializationReceipt>>(path, message);
}
