using System.Collections.ObjectModel;
using System.Globalization;

namespace GalaxyCommand.Simulation;

public enum EntityMaterializationSourceKind
{
    Construction,
}

/// <summary>
/// Semantic record of a fully committed entity becoming publicly live.
/// </summary>
public sealed record EntityMaterializedFact : GameFact
{
    /// <summary>
    /// Creates the semantic record for one fully committed ship materialization.
    /// </summary>
    public EntityMaterializedFact(
        EntityId entityId,
        EntityKind kind,
        ShipId shipId,
        EntityMaterializationSourceKind sourceKind,
        PrincipalId principalId,
        ConstructionDesignId designId,
        SystemPosition initialPosition)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entityId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(principalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(designId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(initialPosition.SystemId.Value);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entity kind.");
        }

        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "Unknown materialization source kind.");
        }

        EntityId = entityId;
        Kind = kind;
        ShipId = shipId;
        SourceKind = sourceKind;
        PrincipalId = principalId;
        DesignId = designId;
        InitialPosition = initialPosition;
    }

    public EntityId EntityId { get; }

    public EntityKind Kind { get; }

    public ShipId ShipId { get; }

    public EntityMaterializationSourceKind SourceKind { get; }

    public PrincipalId PrincipalId { get; }

    public ConstructionDesignId DesignId { get; }

    public SystemPosition InitialPosition { get; }
}

public sealed record EntityRemovedFact : GameFact
{
    public EntityRemovedFact(
        EntityId entityId,
        EntityKind kind,
        ShipId shipId,
        EntityRemovalReason reason,
        EntityCargoDisposition cargoDisposition)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entityId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entity kind.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown removal reason.");
        }

        if (!Enum.IsDefined(cargoDisposition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cargoDisposition),
                cargoDisposition,
                "Unknown cargo disposition.");
        }

        EntityId = entityId;
        Kind = kind;
        ShipId = shipId;
        Reason = reason;
        CargoDisposition = cargoDisposition;
    }

    public EntityId EntityId { get; }

    public EntityKind Kind { get; }

    public ShipId ShipId { get; }

    public EntityRemovalReason Reason { get; }

    public EntityCargoDisposition CargoDisposition { get; }
}

/// <summary>
/// Semantic record of one directional standing value changing after stable
/// contribution reduction.
/// </summary>
