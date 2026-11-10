namespace GalaxyCommand.Simulation;

/// <summary>One immutable retention/intake configuration; object identity guards prepared commits against replacement.</summary>
internal sealed record InventoryCapacityLimits(Quantity Physical, Quantity EnabledIntake, Quantity? ProtectedIntake);

internal sealed record PreparedInventoryCapacityChange(Inventory Inventory, InventoryCapacityLimits Expected,
    Quantity ExpectedUsed, Quantity ExpectedReserved, InventoryCapacityLimits Proposed);

public sealed partial class Inventory
{
    private InventoryCapacityLimits _capacityLimits;

    /// <summary>Gets enabled storage room; disabling capacity does not remove its retained physical storage.</summary>
    public Quantity EnabledIntakeCapacity => _capacityLimits.EnabledIntake;

    /// <summary>Gets a structural project's protected final capacity, when one has been claimed.</summary>
    public Quantity? ProtectedIntakeCapacity => _capacityLimits.ProtectedIntake;

    /// <summary>Gets the lower active intake limit after enabled storage and structural protection are combined.</summary>
    public Quantity EffectiveIntakeCapacity => new(Math.Min(EnabledIntakeCapacity.Units,
        ProtectedIntakeCapacity?.Units ?? Capacity.Units));

    /// <summary>
    /// Prepares capacity authority without mutation. Physical retention and structural
    /// protection must fit current cargo and incoming commitments; enabled storage may
    /// be lower because disabling retains existing cargo and commitments.
    /// </summary>
    internal PreparedInventoryCapacityChange? PrepareCapacityChange(Quantity physicalCapacity,
        Quantity enabledIntakeCapacity, Quantity? protectedIntakeCapacity = null)
    {
        if (enabledIntakeCapacity > physicalCapacity
            || protectedIntakeCapacity is { } protection && protection > physicalCapacity)
            return null;

        UInt128 occupied = (UInt128)UsedCapacity.Units + ReservedCapacity.Units;
        if (occupied > physicalCapacity.Units
            || protectedIntakeCapacity is { } remaining && occupied > remaining.Units)
            return null;

        return new PreparedInventoryCapacityChange(this, _capacityLimits, UsedCapacity, ReservedCapacity,
            new InventoryCapacityLimits(physicalCapacity, enabledIntakeCapacity, protectedIntakeCapacity));
    }

    /// <summary>Rejects a plan when its inventory, active limits, cargo total, or incoming commitments changed.</summary>
    internal bool CanCommitCapacityChange(PreparedInventoryCapacityChange prepared) =>
        ReferenceEquals(prepared.Inventory, this) && ReferenceEquals(prepared.Expected, _capacityLimits)
        && prepared.ExpectedUsed == UsedCapacity && prepared.ExpectedReserved == ReservedCapacity;

    /// <summary>Atomically replaces a revalidated capacity configuration; stale or replayed plans make no change.</summary>
    internal bool ApplyCapacityChange(PreparedInventoryCapacityChange prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!CanCommitCapacityChange(prepared)) return false;
        _capacityLimits = prepared.Proposed;
        return true;
    }

    /// <summary>
    /// Checks a reserved delivery against current intake limits before releasing its claim.
    /// A disabled store cannot use an older reservation to bypass the new-delivery limit.
    /// </summary>
    internal bool CanAcceptReservedIntake(Quantity incoming, Quantity releasedReservation) =>
        releasedReservation <= ReservedCapacity
        && (UInt128)UsedCapacity.Units + ReservedCapacity.Units - releasedReservation.Units + incoming.Units
            <= EffectiveIntakeCapacity.Units;
}
