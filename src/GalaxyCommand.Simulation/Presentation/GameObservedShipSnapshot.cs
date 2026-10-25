namespace GalaxyCommand.Simulation;

/// <summary>
/// A current detected ship. Non-owned contacts carry only external kinematics;
/// detailed orders, control and future motion exist only for owned assets.
/// </summary>
public sealed record GameObservedShipSnapshot(
    EntityId EntityId,
    ShipId Id,
    PrincipalId PrincipalId,
    ConstructionDesignId DesignId,
    SystemPosition? Position,
    ShipVelocity Velocity,
    ShipHeading Heading,
    GameShipSnapshot? OwnedDetail);
