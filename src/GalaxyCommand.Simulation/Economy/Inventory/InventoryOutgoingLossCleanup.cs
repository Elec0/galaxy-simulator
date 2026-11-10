using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Pure selected-loss cleanup proposal; workflow owners decide their own consequences before causal commit.</summary>
internal sealed class PreparedInventoryOutgoingLossCleanup(PreparedCargoSourceLoss sourceLoss,
    IReadOnlyList<InventoryCommitmentRelease> releases, IReadOnlyList<CargoSourceReservationCleanup> cleanup)
{
    internal PreparedCargoSourceLoss SourceLoss { get; } = sourceLoss;
    internal IReadOnlyList<InventoryCommitmentRelease> Releases { get; } = releases;
    internal IReadOnlyList<CargoSourceReservationCleanup> Cleanup { get; } = cleanup;
    /// <summary>Rejects changed selected stock, admission authority, or any replaced released claim before causal commit.</summary>
    internal bool CanCommit() => SourceLoss.CanApply() && Releases.All(release => release.CanCommit());
}

public sealed partial class Inventory
{
    /// <summary>
    /// Preserves older viable fungible claims by releasing newer affected claims in full
    /// until reservations fit post-loss stock. Discrete loss invalidates only its exact
    /// instance claim. Exact provider-forced releases are removed before calculating
    /// deficits, so independently invalidated older claims cannot cause extra revocation.
    /// This pure plan selects no workflow pause/cancellation behavior.
    /// </summary>
    internal PreparedInventoryOutgoingLossCleanup? PrepareOutgoingLossCleanup(IReadOnlyList<CargoDispositionSelection> selected,
        PhysicalDefinitionCatalog definitions, IReadOnlyList<InventoryCommitmentRelease>? forcedReleases = null)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(definitions);
        try
        {
            var fungible = new Dictionary<QualifiedContentKey, Quantity>();
            var items = new HashSet<ItemInstanceId>();
            foreach (CargoDispositionSelection selection in selected)
            {
                if (selection?.DefinitionKey is null || definitions.Get(selection.DefinitionKey) is not { } definition) return null;
                switch (selection)
                {
                    case CargoDispositionSelection.Fungible quantity
                        when definition.HoldingKind == PhysicalHoldingKind.Fungible && quantity.Quantity != Quantity.Zero:
                        Quantity total = fungible.GetValueOrDefault(quantity.DefinitionKey, Quantity.Zero).Add(quantity.Quantity);
                        if (total > FungibleStored(quantity.DefinitionKey)) return null;
                        fungible[quantity.DefinitionKey] = total;
                        break;
                    case CargoDispositionSelection.Discrete discrete
                        when definition.HoldingKind == PhysicalHoldingKind.Discrete && items.Add(discrete.InstanceId)
                            && GetDiscrete(discrete.InstanceId)?.DefinitionKey == discrete.DefinitionKey:
                        break;
                    default:
                        return null;
                }
            }

            IReadOnlyList<InventoryOutgoingCommitment> claims = CaptureOutgoingCommitments();
            var released = new HashSet<(InventoryOutgoingReservationKind Kind, ulong Id)>();
            foreach (InventoryCommitmentRelease forced in forcedReleases ?? [])
            {
                if (forced is null || !ReferenceEquals(forced.Inventory, this) || !forced.CanCommit()) return null;
                bool valid = forced switch
                {
                    InventoryCommitmentRelease.Material legacy when legacy.Reservation.InventoryId == Id
                        && legacy.Reservation.Id.Value != 0 && IsValidOwner(legacy.Reservation.Owner) =>
                        released.Add((InventoryOutgoingReservationKind.LegacyMaterial, legacy.Reservation.Id.Value)),
                    InventoryCommitmentRelease.Physical physical when physical.Reservation.InventoryId == Id
                        && physical.Reservation.Id.Value != 0 && IsValidOwner(physical.Reservation.Owner)
                        && physical.Reservation.Subject is not PhysicalReservationSubject.IncomingCapacity =>
                        released.Add((InventoryOutgoingReservationKind.Physical, physical.Reservation.Id.Value)),
                    _ => false,
                };
                if (!valid) return null;
            }
            foreach ((QualifiedContentKey definition, Quantity lost) in fungible.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
            {
                InventoryOutgoingCommitment[] affected = claims.Where(claim => OutgoingFungibleDefinition(claim) == definition
                        && !released.Contains((claim.Kind, claim.ReservationValue)))
                    .OrderByDescending(claim => claim.AdmissionSequence).ThenBy(claim => claim.Kind)
                    .ThenBy(claim => claim.ReservationValue).ToArray();
                UInt128 reserved = 0;
                foreach (InventoryOutgoingCommitment claim in affected) reserved += OutgoingFungibleQuantity(claim).Units;
                ulong surviving = FungibleStored(definition).Units - lost.Units;
                foreach (InventoryOutgoingCommitment claim in affected)
                {
                    if (reserved <= surviving) break;
                    released.Add((claim.Kind, claim.ReservationValue));
                    reserved -= OutgoingFungibleQuantity(claim).Units;
                }
            }
            foreach (InventoryOutgoingCommitment.Physical claim in claims.OfType<InventoryOutgoingCommitment.Physical>())
                if (claim.Reservation.Subject is PhysicalReservationSubject.Discrete discrete && items.Contains(discrete.InstanceId))
                    released.Add((claim.Kind, claim.ReservationValue));

            var releases = new List<InventoryCommitmentRelease>();
            var cleanup = new List<CargoSourceReservationCleanup>();
            foreach (InventoryOutgoingCommitment claim in claims.Where(claim => released.Contains((claim.Kind, claim.ReservationValue)))
                .OrderByDescending(claim => claim.AdmissionSequence).ThenBy(claim => claim.Kind).ThenBy(claim => claim.ReservationValue))
            {
                switch (claim)
                {
                    case InventoryOutgoingCommitment.Legacy legacy:
                        releases.Add(new InventoryCommitmentRelease.Material(this, legacy.Reservation));
                        cleanup.Add(new CargoSourceReservationCleanup.Legacy(legacy.Reservation.Id, legacy.Reservation.Owner));
                        break;
                    case InventoryOutgoingCommitment.Physical physical:
                        releases.Add(new InventoryCommitmentRelease.Physical(this, physical.Reservation));
                        cleanup.Add(new CargoSourceReservationCleanup.Physical(physical.Reservation.Id, physical.Reservation.Owner));
                        break;
                }
            }
            PhysicalTransferSubject[] subjects = fungible.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                .Select(pair => (PhysicalTransferSubject)new PhysicalTransferSubject.Fungible(pair.Key, pair.Value))
                .Concat(items.OrderBy(id => id.Value).Select(id => new PhysicalTransferSubject.Discrete(id))).ToArray();
            PreparedCargoSourceLoss? loss = CargoSourceLoss.Prepare(this, subjects, definitions, cleanup).Prepared;
            return loss is null ? null : new(loss, Array.AsReadOnly(releases.ToArray()), Array.AsReadOnly(cleanup.ToArray()));
        }
        catch (ArgumentException) { return null; }
        catch (OverflowException) { return null; }
    }

    /// <summary>Uses only the explicit material bridge to associate legacy source claims with physical definitions.</summary>
    private QualifiedContentKey? OutgoingFungibleDefinition(InventoryOutgoingCommitment claim) => claim switch
    {
        InventoryOutgoingCommitment.Legacy legacy => _materialCompatibility?.Get(legacy.Reservation.MaterialId)?.Key,
        InventoryOutgoingCommitment.Physical { Reservation.Subject: PhysicalReservationSubject.Fungible fungible } => fungible.DefinitionKey,
        _ => null,
    };

    /// <summary>Returns the full committed claim quantity; loss planning never partially resizes reservations.</summary>
    private static Quantity OutgoingFungibleQuantity(InventoryOutgoingCommitment claim) => claim switch
    {
        InventoryOutgoingCommitment.Legacy legacy => legacy.Reservation.Quantity,
        InventoryOutgoingCommitment.Physical { Reservation.Subject: PhysicalReservationSubject.Fungible fungible } => fungible.Quantity,
        _ => Quantity.Zero,
    };
}
