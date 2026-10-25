namespace GalaxyCommand.Simulation;

public sealed partial class GameSession
{
    /// <summary>
    /// Joins sensor receipts to the aggregate time, command and item allocators.
    /// Component restore validates lifecycle history; it cannot validate these independently owned boundaries.
    /// </summary>
    private static CheckpointValidationFailure? ValidateSensorCheckpoint(GameSessionCheckpoint checkpoint)
    {
        const string path = "$.checkpoint.lifecycle.sensorDeployables";
        SimulationTime now = checkpoint.Engine.Agenda.CurrentTime;
        bool Allocated(ulong id, IdSequenceCheckpoint sequence) => id != 0 && (sequence.NextValue is null || id < sequence.NextValue);
        HashSet<PrincipalId> principals = checkpoint.Relationships.Principals.Select(principal => principal!.Id).ToHashSet();
        HashSet<SystemId> systems = checkpoint.WorldTopology.Systems.Select(system => system!.Id).ToHashSet();
        bool ValidSubject(DeployedSensorSnapshot sensor) => principals.Contains(sensor.PrincipalId) && systems.Contains(sensor.Position.SystemId);
        SensorDeployableStateCheckpoint state = checkpoint.Lifecycle.SensorDeployables;
        var ships = checkpoint.Lifecycle.LiveShips.ToDictionary(ship => ship!.ShipId, ship => ship!);
        var definitions = (checkpoint.RuntimePolicies.SensorDeployableDefinitions ?? []).Select(definition => definition!.Key!).ToHashSet();
        if ((checkpoint.RuntimePolicies.SensorDeployableDefinitions?.Count ?? 0) > 0 && checkpoint.InventoryCommit is null)
            return new CheckpointValidationFailure("$.checkpoint.inventoryCommit", "Sensor actions require retained inventory allocator continuity.");
        if ((checkpoint.RuntimePolicies.SensorDeployableDefinitions?.Count ?? 0) > 0
            && (checkpoint.SpatialInteractions!.EvaluationDomains ?? []).Contains(ActorOrderRuntimeCoordinator.SensorDomainKind))
            return new CheckpointValidationFailure("$.checkpoint.spatialInteractions.evaluationDomains",
                "Managed sensor action evaluation cannot require an external evaluator binding.");
        foreach (SensorActionReceipt? receipt in state.Receipts)
        {
            if (receipt is null || receipt.ExecutedAt > now || !Allocated(receipt.Request.Command.Value, checkpoint.CommandAdmission.Sequences)
                || receipt.Result.Deployed is not { } sensor || !ValidSubject(sensor) || checkpoint.InventoryCommit is null)
                return new CheckpointValidationFailure(path, "A sensor receipt has an invalid subject, time or command identity.");
            ItemInstanceId? item = receipt.Request is SensorActionRequest.Deploy deployment ? deployment.Item : receipt.Result.Item?.Id;
            if (item is null || !Allocated(item.Value.Value, checkpoint.InventoryCommit.ItemInstanceIds))
                return new CheckpointValidationFailure(path, "A sensor receipt item exceeds the inventory allocator boundary.");
        }
        if ((state.Removals ?? []).Any(removal => removal is null || removal.RemovedAt > now || !ValidSubject(removal.Result.Sensor)))
            return new CheckpointValidationFailure(path, "A sensor removal has an invalid subject or future timestamp.");
        Dictionary<CommandSequence, SensorActionReceipt> receipts = state.Receipts.ToDictionary(receipt => receipt!.Request.Command, receipt => receipt!);
        foreach (GameFactEnvelope? envelope in checkpoint.Facts.RetainedFacts)
        {
            (CommandSequence Command, ShipId Ship, DeployedSensorSnapshot Sensor, bool Deployment)? success = envelope!.Fact switch
            {
                SensorDeployedFact deployed => (deployed.SourceCommand, deployed.ActingShipId, deployed.Sensor, true),
                SensorPickedUpFact pickedUp => (pickedUp.SourceCommand, pickedUp.ActingShipId, pickedUp.Sensor, false),
                _ => null,
            };
            if (success is { } fact && (!receipts.TryGetValue(fact.Command, out SensorActionReceipt? receipt)
                || receipt.ExecutedAt != envelope.Timestamp || receipt.Request.Ship != fact.Ship
                || receipt.Result.Deployed != fact.Sensor || fact.Deployment != (receipt.Request is SensorActionRequest.Deploy)
                || envelope.Cause is not ScheduledEventFactCause cause || cause.Key.Timestamp != envelope.Timestamp
                || cause.Key.CreationSequence >= checkpoint.Engine.Agenda.NextCreationSequence))
                return new CheckpointValidationFailure("$.checkpoint.facts", "A sensor success fact disagrees with its atomic action receipt or triggering event.");
            if (envelope.Fact is SensorRemovedFact removed && !(state.Removals ?? []).Any(removal =>
                removal!.Result.Sensor == removed.Sensor && removal.RemovedAt == envelope.Timestamp
                && removal.Result.Request.Reason == removed.Reason
                && envelope.Cause is EntityRemovalFactCause cause && cause.Request == removal.Result.Request))
                return new CheckpointValidationFailure("$.checkpoint.facts", "A sensor removal fact disagrees with its lifecycle receipt.");
        }
        var commands = new HashSet<CommandSequence>();
        var retainedOrders = new Dictionary<ShipOrderId, (ShipId Ship, ShipActionIntent? Action)>();
        foreach (ShipActorOrdersCheckpoint? actor in checkpoint.Orders.Actors)
        {
            EntityLifecycleShipCheckpoint ship = ships[actor!.ShipId];
            foreach (ShipOrderWorkSetCheckpoint? work in new[] { actor!.Base, actor.Override })
            {
                if (work is null) continue;
                foreach (ShipOrderCheckpoint? order in work.Queue.Append(work.Active).Append(work.LastTerminal))
                {
                    if (order is null) continue;
                    retainedOrders.Add(order.Id, (ship.ShipId, order.Action));
                    if (order.Action is not { } action) continue;
                    bool destinationMatches = action switch
                    {
                        ShipActionIntent.DeploySensor deployment => order.Destination is NavigationDestination.Position position
                            && position.Value == deployment.Position && systems.Contains(deployment.Position.SystemId)
                            && checkpoint.InventoryCommit is { } inventory
                            && Allocated(deployment.ItemId.Value, inventory.ItemInstanceIds),
                        ShipActionIntent.PickupSensor pickup => order.Destination is NavigationDestination.Entity entity
                            && entity.EntityId == pickup.TargetEntityId && Allocated(entity.EntityId.Value, checkpoint.Lifecycle.EntityIds),
                        _ => false,
                    };
                    if (!destinationMatches || action.CargoInventoryId != ship.CargoInventoryId || order.RequestedHeading is not null
                        || !Allocated(action.OriginCommandSequence.Value, checkpoint.CommandAdmission.Sequences)
                        || !commands.Add(action.OriginCommandSequence)
                        || !definitions.Contains(action.DefinitionKey))
                        return new CheckpointValidationFailure("$.checkpoint.orders", "Sensor action intent disagrees with its order or retained owners.");
                    bool succeeded = receipts.TryGetValue(action.OriginCommandSequence, out SensorActionReceipt? outcome);
                    bool requestMatches = (action, outcome?.Request) switch
                    {
                        (ShipActionIntent.DeploySensor deployment, SensorActionRequest.Deploy applied) =>
                            deployment.ItemId == applied.Item && deployment.Position == applied.Position,
                        (ShipActionIntent.PickupSensor pickup, SensorActionRequest.Pickup applied) => pickup.TargetEntityId == applied.Target,
                        _ => false,
                    };
                    if (succeeded != (order.Status == ShipOrderStatus.Completed)
                        || succeeded && (!requestMatches || outcome!.Request.Ship != ship.ShipId
                            || outcome.Result.Deployed!.DefinitionKey != action.DefinitionKey))
                        return new CheckpointValidationFailure("$.checkpoint.orders", "A completed sensor action must match its unique successful atomic receipt.");
                }
            }
        }
        foreach (ScheduledEvent<GameEvent> scheduled in checkpoint.Engine.Agenda.PendingEvents)
            if (scheduled.Payload is GameEvent.SensorActionReevaluation reevaluation
                && reevaluation.Work.Any(work => !Allocated(work.Command.Value, checkpoint.CommandAdmission.Sequences)
                    || !Allocated(work.Ship.Value, checkpoint.Lifecycle.ShipIds)
                    || retainedOrders.TryGetValue(work.Order, out var order)
                        && (order.Ship != work.Ship || order.Action?.OriginCommandSequence != work.Command)))
                return new CheckpointValidationFailure("$.checkpoint.engine.agenda", "A sensor wake references an unallocated command or ship.");
        return null;
    }
}
