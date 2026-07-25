namespace GalaxyCommand.Simulation;

/// <summary>
/// Contract implemented by strongly typed, non-zero simulation identifiers.
/// </summary>
public interface IEntityId<TSelf> where TSelf : struct, IEntityId<TSelf>
{
    static abstract TSelf Create(ulong value);

    ulong Value { get; }
}

public readonly record struct LocationId : IEntityId<LocationId>
{
    public LocationId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static LocationId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct RouteId : IEntityId<RouteId>
{
    public RouteId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static RouteId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct FacilityId : IEntityId<FacilityId>
{
    public FacilityId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static FacilityId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct InventoryId : IEntityId<InventoryId>
{
    public InventoryId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static InventoryId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct MaterialId : IEntityId<MaterialId>
{
    public MaterialId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static MaterialId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ShipId : IEntityId<ShipId>
{
    public ShipId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ShipId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct OrganizationId : IEntityId<OrganizationId>
{
    public OrganizationId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static OrganizationId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ConstructionDesignId : IEntityId<ConstructionDesignId>
{
    public ConstructionDesignId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ConstructionDesignId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ReservationId : IEntityId<ReservationId>
{
    public ReservationId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ReservationId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct CapacityReservationId : IEntityId<CapacityReservationId>
{
    public CapacityReservationId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static CapacityReservationId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ProductionJobId : IEntityId<ProductionJobId>
{
    public ProductionJobId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ProductionJobId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ConstructionOrderId : IEntityId<ConstructionOrderId>
{
    public ConstructionOrderId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static ConstructionOrderId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct TransportJobId : IEntityId<TransportJobId>
{
    public TransportJobId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static TransportJobId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct SupplyOfferId : IEntityId<SupplyOfferId>
{
    public SupplyOfferId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static SupplyOfferId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct DemandRequestId : IEntityId<DemandRequestId>
{
    public DemandRequestId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static DemandRequestId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Deterministic sequential allocator for one identifier domain.
/// </summary>
public sealed class IdSequence<TId> where TId : struct, IEntityId<TId>
{
    private ulong? _next = 1;

    public TId Allocate()
    {
        ulong value = _next ?? throw new InvalidOperationException("Identifier sequence exhausted.");
        _next = value == ulong.MaxValue ? null : value + 1;
        return TId.Create(value);
    }
}
