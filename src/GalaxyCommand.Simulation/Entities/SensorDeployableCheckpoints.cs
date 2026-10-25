namespace GalaxyCommand.Simulation;

internal sealed partial class EntityLifecycleOwner
{
    /// <summary>Captures immutable live sensors and successful action receipts in deterministic identity order.</summary>
    private SensorDeployableStateCheckpoint CaptureSensorState() => new(
        Array.AsReadOnly(_deployedSensors.Values.ToArray()),
        Array.AsReadOnly(_sensorActionReceipts.Values.OrderBy(receipt => receipt.Request.Command.Value).ToArray()),
        Array.AsReadOnly(_sensorRemovalReceipts.Values.OrderBy(receipt => receipt.Result.Request.EntityId.Value).ToArray()));

    /// <summary>
    /// Restores direct state without replaying inventory/entity transitions. Receipt history must
    /// explain every live or picked-up sensor and cannot contradict consumed inventory identities.
    /// The unpublished owner is discarded on any failure.
    /// </summary>
    private CheckpointValidationFailure? RestoreSensorState(EntityLifecycleCheckpoint checkpoint)
    {
        const string path = "$.checkpoint.lifecycle.sensorDeployables";
        SensorDeployableStateCheckpoint state = checkpoint.SensorDeployables;
        if (state.Live is null || state.Receipts is null)
            return new CheckpointValidationFailure(path, "Live sensors and action receipts are required.");
        var deployed = new Dictionary<EntityId, SensorActionReceipt>();
        var pickedUp = new Dictionary<EntityId, SensorActionReceipt>();
        var consumed = new HashSet<ItemInstanceId>();
        var created = new HashSet<ItemInstanceId>();
        HashSet<EntityId> shipEntities = checkpoint.LiveShips.Select(ship => ship!.EntityId)
            .Concat(checkpoint.MaterializationReceipts.Select(receipt => receipt!.EntityId))
            .Concat(checkpoint.RemovalReceipts.Select(receipt => receipt!.Request!.EntityId)).ToHashSet();
        foreach (SensorActionReceipt? receipt in state.Receipts)
        {
            if (receipt?.Request is not { } request || request.Command.Value == 0
                || !WasAllocated(request.Ship.Value, checkpoint.ShipIds)
                || receipt.Result is not { IsSuccess: true, WasApplied: true, Deployed: { } sensor }
                || !WasAllocated(sensor.EntityId.Value, checkpoint.EntityIds)
                || shipEntities.Contains(sensor.EntityId)
                || _ships.Get(request.Ship) is { } actor && actor.PrincipalId != sensor.PrincipalId
                || sensor.PrincipalId.Value == 0 || sensor.Position.SystemId.Value == 0
                || sensor.DefinitionKey is null || !_sensorDefinitions.ContainsKey(sensor.DefinitionKey)
                || !_sensorActionReceipts.TryAdd(request.Command, receipt))
                return new CheckpointValidationFailure(path, "A sensor action receipt is invalid or duplicated.");

            bool valid = request switch
            {
                SensorActionRequest.Deploy deployment => deployment.Item.Value != 0 && receipt.Result.Item is null
                    && deployment.Position == sensor.Position && deployed.TryAdd(sensor.EntityId, receipt)
                    && consumed.Add(deployment.Item),
                SensorActionRequest.Pickup pickup => pickup.Target == sensor.EntityId
                    && receipt.Result.Item is { Id.Value: not 0 } item && item.DefinitionKey == sensor.DefinitionKey
                    && pickedUp.TryAdd(sensor.EntityId, receipt) && created.Add(item.Id),
                _ => false,
            };
            if (!valid) return new CheckpointValidationFailure(path, "An action receipt disagrees with its request or reuses an identity.");
        }

        foreach (SensorRemovalReceipt? removal in state.Removals ?? [])
        {
            if (removal?.Result is not { Request: { } request, Sensor: { } sensor } result || request.EntityId != sensor.EntityId
                || !deployed.TryGetValue(sensor.EntityId, out SensorActionReceipt? origin)
                || origin.Result.Deployed != sensor || removal.RemovedAt < origin.ExecutedAt
                || pickedUp.ContainsKey(sensor.EntityId)
                || !_sensorRemovalReceipts.TryAdd(sensor.EntityId, removal))
                return new CheckpointValidationFailure(path, "A sensor removal has no unique matching deployment.");
        }
        foreach (DeployedSensorSnapshot? sensor in state.Live)
        {
            if (sensor is null || !_entities.CanAddSensorDeployable(sensor.EntityId)
                || !deployed.TryGetValue(sensor.EntityId, out SensorActionReceipt? origin)
                || origin.Result.Deployed != sensor || pickedUp.ContainsKey(sensor.EntityId)
                || _sensorRemovalReceipts.ContainsKey(sensor.EntityId))
                return new CheckpointValidationFailure(path, "A live sensor has no unique matching deployment receipt.");
            _deployedSensors.Add(sensor.EntityId, sensor);
            _entities.ApplyAddSensorDeployable(sensor.EntityId);
        }

        foreach ((EntityId entity, SensorActionReceipt pickup) in pickedUp)
            if (!deployed.TryGetValue(entity, out SensorActionReceipt? origin)
                || origin.Result.Deployed != pickup.Result.Deployed || pickup.ExecutedAt < origin.ExecutedAt)
                return new CheckpointValidationFailure(path, "A pickup receipt has no matching earlier deployment.");
        foreach (EntityId entity in deployed.Keys)
            if (!_deployedSensors.ContainsKey(entity) && !pickedUp.ContainsKey(entity) && !_sensorRemovalReceipts.ContainsKey(entity))
                return new CheckpointValidationFailure(path, "A deployment has no live or removed disposition.");
        if (_inventories.CaptureCheckpoint().Inventories.SelectMany(inventory => inventory.DiscreteItems)
            .Any(item => consumed.Contains(item.Id)))
            return new CheckpointValidationFailure(path, "A consumed deployment item is still stored in an inventory.");
        return null;
    }
}
