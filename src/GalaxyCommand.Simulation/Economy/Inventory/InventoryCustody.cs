namespace GalaxyCommand.Simulation;

/// <summary>
/// Stable typed reference to the domain object that physically contains an
/// inventory. Session entities share <see cref="EntityId"/>, while facilities
/// retain their accepted non-entity identity.
/// </summary>
public abstract record InventoryOwnerReference
{
    private InventoryOwnerReference()
    {
    }

    /// <summary>
    /// Identifies an inventory owned by any object registered as a live
    /// session entity.
    /// </summary>
    public sealed record SessionEntity : InventoryOwnerReference
    {
        /// <summary>Creates a reference to one non-zero session entity.</summary>
        public SessionEntity(EntityId entityId)
        {
            ArgumentOutOfRangeException.ThrowIfZero(entityId.Value);
            EntityId = entityId;
        }

        public EntityId EntityId { get; }
    }

    /// <summary>
    /// Identifies an inventory owned by an economy facility that is not a
    /// session entity.
    /// </summary>
    public sealed record Facility : InventoryOwnerReference
    {
        /// <summary>Creates a reference to one non-zero facility.</summary>
        public Facility(FacilityId facilityId)
        {
            ArgumentOutOfRangeException.ThrowIfZero(facilityId.Value);
            FacilityId = facilityId;
        }

        public FacilityId FacilityId { get; }
    }
}

/// <summary>
/// Immutable physical owner and controlling principal assigned to one
/// inventory.
/// </summary>
public sealed record InventoryCustody
{
    /// <summary>
    /// Creates custody metadata without asserting that the referenced owner
    /// is currently registered. Session composition validates that link.
    /// </summary>
    public InventoryCustody(
        InventoryOwnerReference physicalOwner,
        PrincipalId controllingPrincipalId)
    {
        ArgumentNullException.ThrowIfNull(physicalOwner);
        ArgumentOutOfRangeException.ThrowIfZero(controllingPrincipalId.Value);
        PhysicalOwner = physicalOwner;
        ControllingPrincipalId = controllingPrincipalId;
    }

    public InventoryOwnerReference PhysicalOwner { get; }

    public PrincipalId ControllingPrincipalId { get; }
}
