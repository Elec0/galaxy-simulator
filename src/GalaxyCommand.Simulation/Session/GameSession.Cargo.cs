namespace GalaxyCommand.Simulation;

public sealed partial class GameSession
{
    /// <summary>Internal causal-domain handoff; player collection and voluntary ejection use their own command boundaries.</summary>
    internal CargoDispositionCommitResult CommitCargoDisposition(CargoDispositionRequest request)
    {
        EnsureHealthy();
        ArgumentNullException.ThrowIfNull(request);
        if (request.Key?.Trigger is CargoDispositionTrigger.Command command)
        {
            IdSequenceCheckpoint sequence = _commands.CaptureCheckpoint().Sequences;
            if (command.Sequence.Value == 0 || sequence.NextValue is { } next && command.Sequence.Value >= next)
                return new(null, CargoDispositionFailure.InvalidInput, false);
        }
        try { return _runtime.CommitCargoDisposition(request); }
        finally { CompletePlayerDisclosureIfIdle(); }
    }

    /// <summary>Captures authoritative cargo diagnostics; player-visible views must pass the separate coverage projection.</summary>
    internal IReadOnlyList<CargoLiveObject> CaptureCargoAuthority()
    {
        EnsureHealthy();
        return _runtime.CaptureCargoAuthority();
    }

    /// <summary>Joins cargo's durable causal/item history to admission and inventory allocators owned by the session.</summary>
    private static CheckpointValidationFailure? ValidateCargoAdmissionHistory(GameSessionCheckpoint checkpoint)
    {
        if (checkpoint.Cargo is null) return null;
        const string path = "$.checkpoint.cargo";
        if (checkpoint.InventoryCommit?.ItemInstanceIds is not { } items
            || checkpoint.CommandAdmission?.Sequences is not { } commands)
            return new(path, "Cargo authority requires exact inventory and command allocator continuity.");
        // Sparse setup IDs leave allocator holes. Supported sources require an
        // actual ship lifecycle mapping, even after their bounded facts expire.
        var sourceInventories = checkpoint.Lifecycle.LiveShips
            .ToDictionary(ship => ship!.EntityId, ship => ship!.CargoInventoryId);
        foreach (EntityRemovalReceiptCheckpoint removal in checkpoint.Lifecycle.RemovalReceipts.Select(receipt => receipt!))
            sourceInventories.Add(removal.Request!.EntityId, removal.CargoInventoryId);
        if (checkpoint.Stations is { } stations)
        {
            foreach (StationStateCheckpoint station in stations.Stations) sourceInventories.Add(station.Entity, station.Inventory);
            foreach (StationRemovalReceipt removal in stations.Removals)
                sourceInventories.Add(removal.Station.EntityId, removal.Station.InventoryId);
        }
        HashSet<SystemId> systems = checkpoint.WorldTopology.Systems.Select(system => system!.Id).ToHashSet();
        var destroyedItems = new HashSet<ItemInstanceId>();
        bool Allocated(ulong value, IdSequenceCheckpoint sequence) => value != 0 && (sequence.NextValue is null || value < sequence.NextValue);
        foreach (CargoDispositionReceipt receipt in checkpoint.Cargo.Receipts)
        {
            if (receipt.Request.Key.Trigger is CargoDispositionTrigger.Command command && !Allocated(command.Sequence.Value, commands)
                || receipt.Request.Selected.OfType<CargoDispositionSelection.Discrete>().Any(item => !Allocated(item.InstanceId.Value, items)))
                return new(path, "A cargo receipt references unallocated command or original item identities.");
            if (!systems.Contains(receipt.Request.SourcePosition.SystemId)
                || !sourceInventories.TryGetValue(receipt.Request.Key.SourceEntityId, out InventoryId inventory)
                || inventory != receipt.Request.SourceInventoryId)
                return new(path, "A cargo receipt disagrees with its known source system or immutable source inventory mapping.");

            // Surviving items may move again, but an item destroyed by any
            // occurrence cannot regain live custody under the same identity.
            HashSet<ItemInstanceId> survivors = receipt.Outcomes.SelectMany(outcome => outcome.SurvivingInstances).ToHashSet();
            foreach (CargoDispositionSelection.Discrete selected in receipt.Request.Selected.OfType<CargoDispositionSelection.Discrete>())
                if (!survivors.Contains(selected.InstanceId)) destroyedItems.Add(selected.InstanceId);
        }
        if (checkpoint.Lifecycle.Inventories.Inventories.Any(inventory => inventory.DiscreteItems.Any(item => destroyedItems.Contains(item.Id))))
            return new(path, "A destroyed cargo item identity cannot appear in a live inventory.");
        return null;
    }
}
