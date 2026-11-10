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
/// Immutable physical custody. Unclaimed cargo is explicit and never represented
/// by missing custody or a fabricated principal identity.
/// </summary>
public abstract record InventoryCustody
{
    private InventoryCustody(InventoryOwnerReference physicalOwner, PrincipalId? controllingPrincipalId)
    {
        PhysicalOwner = physicalOwner;
        ControllingPrincipalId = controllingPrincipalId;
    }

    public InventoryOwnerReference PhysicalOwner { get; }

    public PrincipalId? ControllingPrincipalId { get; }

    /// <summary>Physical custody controlled by one nonzero principal.</summary>
    public sealed record PrincipalControlled : InventoryCustody
    {
        /// <summary>
        /// Records a physical owner and controlling principal. Session composition
        /// validates the registered owner link.
        /// </summary>
        public PrincipalControlled(InventoryOwnerReference physicalOwner, PrincipalId controllingPrincipalId)
            : base(physicalOwner, controllingPrincipalId)
        {
            ArgumentNullException.ThrowIfNull(physicalOwner);
            ArgumentOutOfRangeException.ThrowIfZero(controllingPrincipalId.Value);
        }
    }

    /// <summary>Unclaimed physical cargo; live admission must validate its cargo-object owner.</summary>
    public sealed record Unclaimed : InventoryCustody
    {
        /// <summary>Identifies a physical cargo object without granting collection rights to any principal.</summary>
        public Unclaimed(EntityId cargoObjectId)
            : base(new InventoryOwnerReference.SessionEntity(cargoObjectId), null)
        {
        }
    }
}
