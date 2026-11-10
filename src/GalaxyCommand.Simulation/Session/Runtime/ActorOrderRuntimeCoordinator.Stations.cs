namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    /// <summary>Bounds immediate failed/promoted order notices and physical stops; continuation publishes in Decision.</summary>
    private int StationTargetFactCapacity(EntityId station)
    {
        IReadOnlyList<TargetedShipOrder> targets = _orders.PrepareTargetRemoval(station, default);
        // Every target fails once, an active/suspended target can promote once,
        // and every interrupted actor contributes at most one motion-ended fact.
        return checked(targets.Count * 3);
    }

    /// <summary>Checks ship controller authority and its revision before station identity or assignment preparation.</summary>
    private GameplayCommandHandlingResult HandleStationCommand(GameplayCommandEnvelope envelope)
    {
        if (_stations is null) return new(CommandResult.Rejected(new("station-domain-unavailable"), "Station definitions are not configured."));
        if (envelope.Command is not (CreateStationSiteCommand or AssignStationBuilderCommand or ReleaseStationBuilderCommand))
            return _stations.Handle(envelope);
        (ShipId ship, ActorControlRevision revision) = envelope.Command switch
        {
            CreateStationSiteCommand command => (command.BuilderShip, command.ExpectedControlRevision),
            AssignStationBuilderCommand command => (command.BuilderShip, command.ExpectedControlRevision),
            ReleaseStationBuilderCommand command => (command.BuilderShip, command.ExpectedControlRevision),
            _ => throw new InvalidOperationException("Unsupported station command."),
        };
        if (!_control.Contains(ship) || _control.CheckCommand(ship, envelope.Source) != ActorCommandEligibility.Eligible)
            return new(CommandResult.Rejected(new("station-wrong-controller"), "The command source cannot direct this builder."));
        if (_control.Capture(ship).Revision != revision)
            return new(CommandResult.Rejected(CommandRejectionCodes.StaleControlRevision, "The construction ship controller revision changed."));
        return _stations.Handle(envelope);
    }

    /// <summary>Reads explicit ship construction capability and stationary committed location; moving ships supply no work.</summary>
    private StationBuilderState? CaptureStationBuilder(ShipId ship, CommandSource source)
    {
        if (_lifecycle.GetShip(ship) is not { } record
            || _lifecycle.GetOperationalShipDefinition(record.DesignId)?.StationConstructionCapability is not { } capability)
            return null;
        SystemPosition? position = _movement.PositionAt(ship, CurrentTime);
        if (position is null) return null;
        return new(record.PrincipalId, capability, position.Value,
            _movement.GetState(ship) is ShipSpatialState.AtPosition
            && _control.CheckCommand(ship, source) == ActorCommandEligibility.Eligible);
    }
}
