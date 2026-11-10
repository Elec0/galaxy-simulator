namespace GalaxyCommand.Simulation;

/// <summary>Queues one explicit retained ship design at the owning station module's exact facility identity.</summary>
public sealed record EnqueueStationShipCommand(PrincipalId RequestingPrincipal, StationId Station,
    ulong ExpectedStationRevision, StationModuleId Module, FacilityId ExpectedFacility, ConstructionDesignId Design)
    : GameplayCommand("enqueue-station-ship");

/// <summary>Private owning-principal queue confirmation; exterior observers receive no work or design details.</summary>
public sealed record StationShipConstructionQueuedFact(StationId StationId, StationModuleId Module,
    FacilityId Facility, ConstructionOrderId Order, ConstructionDesignId Design) : GameFact;

internal sealed record StationShipOrderReceipt(CommandSequence Command, StationId Station, StationModuleId Module,
    FacilityId Facility, ConstructionOrderId Order, ConstructionDesignId Design);

internal sealed partial class StationRuntimeOwner
{
    private readonly SortedDictionary<CommandSequence, StationShipOrderReceipt> _shipOrders = new(Comparer<CommandSequence>.Create((left, right) => left.Value.CompareTo(right.Value)));

    /// <summary>Requires one exact accepted command for every queued order receipt, including providers later retired.</summary>
    private bool RestoreShipOrderReceipts(IReadOnlyList<StationShipOrderReceipt> saved)
    {
        var orders = new HashSet<ConstructionOrderId>();
        foreach (StationShipOrderReceipt? receipt in saved)
            if (receipt is null || receipt.Order.Value == 0 || !orders.Add(receipt.Order)
                || !_receipts.TryGetValue(receipt.Command, out var accepted)
                || accepted.Request.Command is not EnqueueStationShipCommand command
                || receipt.Station != command.Station || receipt.Module != command.Module
                || receipt.Facility != command.ExpectedFacility || receipt.Design != command.Design
                || !_shipOrders.TryAdd(receipt.Command, receipt)) return false;
        return _receipts.Values.Count(receipt => receipt.Request.Command is EnqueueStationShipCommand) == _shipOrders.Count;
    }

    /// <summary>Accepts owned operational shipyards, retaining disabled queues while rejecting intake during dismantling.</summary>
    private GameplayCommandHandlingResult EnqueueShip(GameplayCommandEnvelope envelope, EnqueueStationShipCommand command)
    {
        if (!TryStructuralStation(command.RequestingPrincipal, command.Station, command.ExpectedStationRevision,
            out var station, out var rejection)) return rejection;
        if (!station.Modules.TryGetValue(command.Module, out var module) || !module.IsCompleted
            || module.Phase != StationModulePhase.Operational || module.FacilityId != command.ExpectedFacility
            || module.Definition.ShipConstructionProvider is null)
            return Rejected("station-shipyard-unavailable");
        if (station.Revision == ulong.MaxValue) return Rejected("station-revision-exhausted");
        var prepared = _economicProviders?.PrepareStationShipOrder(command.ExpectedFacility, command.Design);
        if (prepared is null || !prepared.CanCommit()) return Rejected("station-ship-design-unavailable");
        ConstructionOrderId order = prepared.Apply();
        _shipOrders.Add(envelope.Sequence, new(envelope.Sequence, station.Id, module.Id, command.ExpectedFacility, order, command.Design));
        station.Revision = checked(station.Revision + 1);
        return new(CommandResult.Accepted(), [new(new(GameFactCommitCategory.EntityLifecycle,
            station.Entity.Value, order.Value, 3), new StationShipConstructionQueuedFact(station.Id, module.Id,
                command.ExpectedFacility, order, command.Design))]);
    }
}
