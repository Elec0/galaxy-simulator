using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record CargoDispositionOwnerCheckpoint(QualifiedContentKey PolicyKey,
    IReadOnlyList<CargoLiveObject> Objects, IReadOnlyList<CargoDispositionReceipt> Receipts,
    CargoDebrisFieldsCheckpoint Fields);

internal sealed partial class CargoDispositionOwner
{
    /// <summary>Captures authority only at a healthy completed timestamp; spatial indexes remain derived.</summary>
    internal CheckpointResult<CargoDispositionOwnerCheckpoint> CaptureCheckpoint()
    {
        if (!_isHealthy || _agenda.CurrentPhase is not null)
            return CheckpointResult<CargoDispositionOwnerCheckpoint>.Rejected(new("$.checkpoint.cargo",
                "Cargo capture requires healthy authority and a completed timestamp."));
        return CheckpointResult<CargoDispositionOwnerCheckpoint>.Success(new(_policy.Key, CaptureObjects(), CaptureReceipts(), Fields.CaptureCheckpoint()));
    }

    /// <summary>
    /// Restores unpublished cargo authority by proving historical generation and all
    /// live inventory, lifecycle, field and agenda links. No gameplay transaction,
    /// random-stream advancement, identity allocation, or event scheduling is replayed.
    /// </summary>
    internal static CheckpointResult<CargoDispositionOwner> Restore(CargoDispositionOwnerCheckpoint checkpoint,
        EntityLifecycleOwner lifecycle, PhysicalDefinitionCatalog definitions, CargoDispositionPolicy policy,
        DeterministicRandomOwner random, EventAgenda<GameEvent> agenda, GameFactStore facts)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(facts);
        if (checkpoint.PolicyKey != policy.Key || checkpoint.Objects is null || checkpoint.Receipts is null || checkpoint.Fields is null)
            return CargoCheckpointRejected("Cargo policy identity and every state section are required.");
        CheckpointResult<EventAgendaCheckpoint<GameEvent>> agendaCapture = agenda.CaptureCheckpoint();
        if (!agendaCapture.IsSuccess) return CargoCheckpointRejected("Cargo restoration requires a completed timestamp.");
        CheckpointResult<CargoDebrisFields> fields = CargoDebrisFields.Restore(checkpoint.Fields);
        if (!fields.IsSuccess) return CargoCheckpointRejected(fields.Failure!.Message);
        var owner = new CargoDispositionOwner(lifecycle, definitions, policy, random, agenda, facts) { Fields = fields.Value! };
        EntityLifecycleCheckpoint life = lifecycle.CaptureCheckpoint();
        var origins = new Dictionary<EntityId, RestoredCargoOrigin>();
        HashSet<EntityId> nonCargoHistory = life.LiveShips.Select(ship => ship!.EntityId)
            .Concat(life.MaterializationReceipts.Select(receipt => receipt!.EntityId))
            .Concat(life.RemovalReceipts.Select(receipt => receipt!.Request!.EntityId))
            .Concat(life.SensorDeployables.Receipts.Select(receipt => receipt!.Result.Deployed!.EntityId)).ToHashSet();

        foreach (CargoDispositionReceipt? receipt in checkpoint.Receipts)
        {
            string? failure = owner.RestoreCargoReceipt(receipt, life, nonCargoHistory, origins);
            if (failure is not null) return CargoCheckpointRejected(failure);
        }
        if (owner._receipts.Values.Any(receipt => origins.ContainsKey(receipt.Request.Key.SourceEntityId)))
            return CargoCheckpointRejected("Floating cargo cannot be a historical disposition source.");
        string? liveFailure = owner.RestoreLiveCargo(checkpoint.Objects, origins, agendaCapture.Value!);
        if (liveFailure is not null) return CargoCheckpointRejected(liveFailure);
        string? fieldFailure = owner.ValidateCargoFieldHistory(checkpoint.Fields);
        if (fieldFailure is not null) return CargoCheckpointRejected(fieldFailure);
        string? factFailure = owner.ValidateRetainedCargoFacts(origins);
        return factFailure is null ? CheckpointResult<CargoDispositionOwner>.Success(owner) : CargoCheckpointRejected(factFailure);
    }

    /// <summary>
    /// Regenerates canonical historical outcomes from retained selections and exact
    /// compatible definitions. Original item identities are transient evaluation
    /// inputs here; live inventory authority is restored separately and saved once.
    /// </summary>
    private string? RestoreCargoReceipt(CargoDispositionReceipt? saved, EntityLifecycleCheckpoint life,
        HashSet<EntityId> nonCargoHistory, Dictionary<EntityId, RestoredCargoOrigin> origins)
    {
        if (saved?.Request is not { } request || saved.Outcomes is null || saved.ObjectIds is null
            || saved.CommittedAt > _agenda.CurrentTime || request.Policy is null || !PoliciesMatch(request.Policy, _policy))
            return "A cargo receipt requires complete input, compatible policy, outcomes and a committed timestamp.";
        CargoDispositionRequest canonical;
        List<CargoObjectSeed> seeds = [];
        List<CargoDispositionTypeOutcome> expected = [];
        try
        {
            canonical = Canonicalize(request);
            if (!RequestsMatch(canonical, request) || !ValidHistoricalCleanup(canonical.Cleanup))
                return "Historical receipt selection and cleanup must be canonical and valid.";
            CargoSourceLossGroup[] groups = canonical.Selected.GroupBy(selection => selection.DefinitionKey)
                .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal).Select(group =>
                {
                    PhysicalDefinition definition = _definitions.Get(group.Key)!;
                    DiscreteItemInstance[] items = group.OfType<CargoDispositionSelection.Discrete>()
                        .Select(item => new DiscreteItemInstance(item.InstanceId, item.DefinitionKey)).ToArray();
                    Quantity quantity = definition.HoldingKind == PhysicalHoldingKind.Fungible
                        ? ((CargoDispositionSelection.Fungible)group.Single()).Quantity : new((ulong)items.Length);
                    return new CargoSourceLossGroup(definition, quantity, Array.AsReadOnly(items));
                }).ToArray();
            ulong removedCapacity = 0;
            foreach (CargoSourceLossGroup group in groups)
                removedCapacity = checked(removedCapacity + checked(group.Quantity.Units * group.Definition.CapacityCost.Units));
            Generate(canonical, groups, seeds, expected);
            if (seeds.Count > 0) _ = CargoDispositionArithmetic.ExpiryAt(saved.CommittedAt, _policy.Lifetime);
        }
        catch (ArgumentException) { return "A cargo receipt has malformed canonical generation input."; }
        catch (OverflowException) { return "A cargo receipt exceeds supported generation or timeline arithmetic."; }
        if (!WasAllocatedCargoId(canonical.Key.SourceEntityId.Value, life.EntityIds)
            || !WasAllocatedCargoId(canonical.SourceInventoryId.Value, life.InventoryIds))
            return "A disposition source identity exceeds its authoritative allocator history.";
        if (canonical.Key.Trigger is CargoDispositionTrigger.Scheduled scheduled
            && (scheduled.Key.Timestamp != saved.CommittedAt || scheduled.Key.CreationSequence >= _agenda.NextCreationSequence))
            return "A scheduled disposition must retain its full allocated event key at its commit timestamp.";
        if (expected.Count != saved.Outcomes.Count || !expected.Zip(saved.Outcomes).All(pair => CargoOutcomesMatch(pair.First, pair.Second))
            || seeds.Count != saved.ObjectIds.Count)
            return "Saved cargo outcomes or object counts disagree with deterministic generation and conservation.";
        bool needsField = seeds.Count > 0 && canonical.Cause != CargoLossCause.FunctionalEjection;
        if (needsField != (saved.FieldHandle is { Value: > 0 }))
            return "Only nonempty destruction occurrences have an original debris-field handle.";
        var copied = new CargoDispositionReceipt(canonical with { Policy = _policy }, saved.CommittedAt,
            Array.AsReadOnly(expected.ToArray()), Array.AsReadOnly(saved.ObjectIds.ToArray()), saved.FieldHandle);
        if (!_receipts.TryAdd(canonical.Key, copied)) return "Cargo causal receipt identities must be unique.";
        for (int index = 0; index < seeds.Count; index++)
        {
            EntityId id = saved.ObjectIds[index];
            if (!WasAllocatedCargoId(id.Value, life.EntityIds) || nonCargoHistory.Contains(id)
                || index > 0 && (saved.ObjectIds[index - 1].Value == ulong.MaxValue || id.Value != saved.ObjectIds[index - 1].Value + 1)
                || !origins.TryAdd(id, new(copied, seeds[index], index)))
                return "Generated cargo object identities must be allocated, unique, consecutive and distinct from other entity history.";
        }
        return null;
    }

    /// <summary>Joins live metadata exactly to generated origin, remaining inventory and pending expiry authority.</summary>
    private string? RestoreLiveCargo(IReadOnlyList<CargoLiveObject> objects, Dictionary<EntityId, RestoredCargoOrigin> origins,
        EventAgendaCheckpoint<GameEvent> agenda)
    {
        Dictionary<EntityId, CargoObjectSnapshot> physical = _lifecycle.CaptureCargoObjects().ToDictionary(cargo => cargo.EntityId);
        foreach (CargoLiveObject? cargo in objects)
        {
            if (cargo is null || !origins.TryGetValue(cargo.EntityId, out RestoredCargoOrigin? origin)
                || cargo.Origin != origin.Receipt.Request.Key || cargo.CreatedAt != origin.Receipt.CommittedAt
                || cargo.PolicyKey != _policy.Key || cargo.Generation.Value != 0
                || cargo.ExpiresAt != CargoDispositionArithmetic.ExpiryAt(cargo.CreatedAt, _policy.Lifetime)
                || cargo.ExpiresAt <= _agenda.CurrentTime || cargo.OriginalFieldId != origin.Receipt.FieldHandle
                || cargo.ExpiryEvent.Timestamp != cargo.ExpiresAt || cargo.ExpiryEvent.Phase != EventPhase.StateUpdate
                || !physical.TryGetValue(cargo.EntityId, out CargoObjectSnapshot? entity) || entity.Position != origin.Seed.Position
                || _lifecycle.Inventories.Get(entity.InventoryId) is not { } inventory
                || !LiveCargoContentsMatch(inventory, origin.Seed)
                || _agenda.CheckCancellation(cargo.ExpiryEvent, cargo.Generation, new GameEvent.CargoExpiry(cargo.EntityId)) != AgendaCancellationCheck.Matches
                || !_objects.TryAdd(cargo.EntityId, cargo))
                return "Live cargo must match its exact generated origin, physical contents, lifetime and pending expiry.";
        }
        if (_objects.Count != physical.Count) return "Every physical cargo object requires exactly one live cargo metadata row.";
        foreach (ScheduledEvent<GameEvent> entry in agenda.PendingEvents)
            if (entry.Payload is GameEvent.CargoExpiry expiry && (!_objects.TryGetValue(expiry.ObjectId, out CargoLiveObject? cargo)
                || cargo.ExpiryEvent != entry.Key || cargo.Generation != entry.Generation))
                return "The agenda contains an extra or mismatched cargo expiry envelope.";
        return null;
    }

    /// <summary>
    /// Corroborates every field handle and original circle with a destruction receipt,
    /// including merged aliases and empty tombstones. Live membership contains exactly
    /// the remaining physical objects; standalone ejections never become field members.
    /// </summary>
    private string? ValidateCargoFieldHistory(CargoDebrisFieldsCheckpoint checkpoint)
    {
        var receipts = new Dictionary<DebrisFieldId, CargoDispositionReceipt>();
        foreach (CargoDispositionReceipt receipt in _receipts.Values)
            if (receipt.FieldHandle is { } handle && !receipts.TryAdd(handle, receipt))
                return "An original field handle cannot be reused by another disposition occurrence.";
        Dictionary<DebrisFieldId, DebrisFieldId> aliases = checkpoint.Aliases.ToDictionary(alias => alias.Alias, alias => alias.Canonical);
        HashSet<DebrisFieldId> retained = checkpoint.Fields.Select(field => field.Id).Concat(checkpoint.Tombstones)
            .Concat(aliases.Keys).ToHashSet();
        if (!retained.SetEquals(receipts.Keys)) return "Retained field history must account for every and only nonempty destruction occurrence.";
        foreach (CargoDebrisFieldSnapshot field in checkpoint.Fields)
        {
            foreach (CargoDebrisCircle circle in field.Circles)
                if (!receipts.TryGetValue(circle.OriginalFieldId, out CargoDispositionReceipt? receipt)
                    || circle.Center != receipt.Request.SourcePosition || circle.RadiusMeters != _policy.ScatterRadiusMeters)
                    return "An original field circle disagrees with its causal destruction receipt.";
            EntityId[] expected = _objects.Values.Where(cargo => cargo.OriginalFieldId is { } handle && Fields.Resolve(handle) == field.Id)
                .Select(cargo => cargo.EntityId).OrderBy(id => id.Value).ToArray();
            if (!field.Members.OrderBy(id => id.Value).SequenceEqual(expected))
                return "Live field membership disagrees with cargo object origins.";
        }
        foreach (CargoLiveObject cargo in _objects.Values)
            if (cargo.OriginalFieldId is { } handle && Fields.Resolve(handle) is null
                || cargo.OriginalFieldId is null && checkpoint.Fields.Any(field => field.Members.Contains(cargo.EntityId)))
                return "Live cargo cannot belong to an emptied field, and standalone cargo has no field.";

        // Even a geometrically connected group cannot reuse a field that must
        // already have emptied before its later occurrences were committed.
        foreach (var group in receipts.GroupBy(pair => aliases.GetValueOrDefault(pair.Key, pair.Key)))
            if (!HasPossibleContinuousFieldHistory(group.Select(pair => pair.Value)))
                return "A field alias group cannot remain connected across its occurrence lifetimes.";
        return null;
    }

    /// <summary>
    /// Checks the latest possible liveness of each occurrence without inventing
    /// evicted collection history. One synthetic member lasts until the original
    /// expiry; circles remain while merged members live. Equal-time publication
    /// may precede expiry, so only strictly earlier expiries are removed first.
    /// This validation owner allocates no gameplay identity or agenda work.
    /// </summary>
    private bool HasPossibleContinuousFieldHistory(IEnumerable<CargoDispositionReceipt> receipts)
    {
        var proof = new CargoDebrisFields(_policy.MergeBehaviorVersion);
        var expiry = new PriorityQueue<EntityId, ulong>();
        try
        {
            foreach (CargoDispositionReceipt receipt in receipts.OrderBy(item => item.CommittedAt.Milliseconds)
                .ThenBy(item => item.FieldHandle!.Value.Value))
            {
                while (expiry.TryPeek(out EntityId member, out ulong at) && at < receipt.CommittedAt.Milliseconds)
                {
                    expiry.Dequeue();
                    PreparedCargoDebrisRemoval? removal = proof.PrepareRemoval(member);
                    if (removal is null) return false;
                    proof.Apply(removal);
                }
                EntityId marker = new(receipt.FieldHandle!.Value.Value);
                PreparedCargoDebrisMerge? occurrence = proof.PrepareOccurrence(receipt.Request.SourcePosition,
                    _policy.ScatterRadiusMeters, [marker]);
                if (occurrence is null) return false;
                proof.Apply(occurrence);
                expiry.Enqueue(marker, CargoDispositionArithmetic.ExpiryAt(receipt.CommittedAt, _policy.Lifetime).Milliseconds);
            }
            CargoDebrisFieldsCheckpoint state = proof.CaptureCheckpoint();
            return state.Fields.Count == 1 && state.Tombstones.Count == 0;
        }
        catch (OverflowException) { return false; }
    }

    /// <summary>Allows positive partial remainders while retaining the object's exact initial capacity and discrete identity.</summary>
    private static bool LiveCargoContentsMatch(Inventory inventory, CargoObjectSeed seed)
    {
        ulong initialUnits = seed.Subject is PhysicalTransferSubject.Fungible fungible ? fungible.Quantity.Units : 1;
        if (inventory.Capacity.Units != checked(initialUnits * seed.Definition.CapacityCost.Units)) return false;
        InventoryCheckpoint saved = inventory.CaptureCheckpoint();
        return seed.Subject switch
        {
            PhysicalTransferSubject.Fungible amount => saved.DiscreteItems.Count == 0 && saved.FungibleHoldings.Count == 1
                && saved.FungibleHoldings[0].DefinitionKey == amount.DefinitionKey
                && saved.FungibleHoldings[0].Quantity > Quantity.Zero && saved.FungibleHoldings[0].Quantity <= amount.Quantity,
            PhysicalTransferSubject.Discrete item => saved.FungibleHoldings.Count == 0 && saved.DiscreteItems.Count == 1
                && saved.DiscreteItems[0].Id == item.InstanceId && saved.DiscreteItems[0].DefinitionKey == seed.Definition.Key,
            _ => false,
        };
    }

    /// <summary>Requires the full exact type partition and canonical surviving instance identities.</summary>
    private static bool CargoOutcomesMatch(CargoDispositionTypeOutcome expected, CargoDispositionTypeOutcome? actual) =>
        actual is not null && actual.DefinitionKey == expected.DefinitionKey && actual.Kind == expected.Kind
        && actual.Removed == expected.Removed && actual.Destroyed == expected.Destroyed && actual.Recoverable == expected.Recoverable
        && actual.SurvivingInstances is not null && actual.SurvivingInstances.SequenceEqual(expected.SurvivingInstances);

    /// <summary>Historical outgoing cleanup requires exact nonzero workflow identities and no reused reservation ID.</summary>
    private static bool ValidHistoricalCleanup(IReadOnlyList<CargoSourceReservationCleanup> cleanup)
    {
        var ids = new HashSet<ReservationId>();
        return cleanup.All(entry => entry.ReservationId.Value > 0 && ids.Add(entry.ReservationId)
            && (entry.Owner switch
            {
                ReservationOwner.TransportJob value => value.JobId.Value > 0,
                ReservationOwner.ProductionJob value => value.JobId.Value > 0,
                ReservationOwner.ConstructionOrder value => value.OrderId.Value > 0,
                _ => false,
            }));
    }

    /// <summary>
    /// Corroborates retained diagnostic facts against authority without reconstructing
    /// authority from the bounded fact suffix. Missing evicted facts are allowed;
    /// contradictory retained full cause keys, duplicate outcomes or live expiries are not.
    /// </summary>
    private string? ValidateRetainedCargoFacts(Dictionary<EntityId, RestoredCargoOrigin> origins)
    {
        var committed = new HashSet<CargoDispositionKey>();
        var expired = new HashSet<EntityId>();
        var appeared = new HashSet<EntityId>();
        var disappeared = new HashSet<EntityId>();
        var expiryBases = new Dictionary<CargoDispositionKey, ulong>();
        HashSet<ItemInstanceId> liveItems = _lifecycle.Inventories.PhysicalItemLocations.Select(location => location.ItemId).ToHashSet();
        IReadOnlyList<GameFactEnvelope?> retained = _facts.CaptureCheckpoint().RetainedFacts;
        var removalCauses = new Dictionary<EntityId, GameFactEnvelope>();
        foreach (GameFactEnvelope? envelope in retained)
        {
            EntityId? removed = envelope?.Fact switch
            {
                CargoObjectExpiredFact expiry => expiry.EntityId,
                CargoCollectedFact { Outcome: { Retired: true } } collected => collected.TargetId,
                _ => null,
            };
            if (removed is { } id && !removalCauses.TryAdd(id, envelope!))
                return "A cargo object cannot have duplicate retained removal outcomes.";
        }
        foreach (GameFactEnvelope? envelope in retained)
        {
            if (envelope is null) return "A retained cargo fact envelope is missing.";
            switch (envelope.Fact)
            {
                case CargoObjectAppearedFact fact:
                    if (fact.Object is null || !origins.TryGetValue(fact.Object.EntityId, out RestoredCargoOrigin? appearanceOrigin)
                        || !appeared.Add(fact.Object.EntityId) || envelope.Timestamp != appearanceOrigin.Receipt.CommittedAt
                        || envelope.Cause != CargoOriginFactCause(appearanceOrigin.Receipt.Request.Key)
                        || fact.Object != HistoricalCargoAppearance(appearanceOrigin, fact.Object.EntityId))
                        return "A retained appearance fact disagrees with deterministic original cargo contents, position or cause.";
                    break;
                case CargoObjectDisappearedFact fact:
                    if (!origins.TryGetValue(fact.EntityId, out RestoredCargoOrigin? disappearanceOrigin)
                        || !disappeared.Add(fact.EntityId) || _objects.ContainsKey(fact.EntityId)
                        || fact.Position != disappearanceOrigin.Seed.Position
                        || envelope.Timestamp < disappearanceOrigin.Receipt.CommittedAt || envelope.Timestamp > _agenda.CurrentTime
                        || envelope.Timestamp > CargoDispositionArithmetic.ExpiryAt(disappearanceOrigin.Receipt.CommittedAt, _policy.Lifetime)
                        || envelope.Cause is not ScheduledEventFactCause disappearanceCause
                        || disappearanceCause.Key.Timestamp != envelope.Timestamp || disappearanceCause.Key.Phase != EventPhase.StateUpdate
                        || disappearanceCause.Key.CreationSequence >= _agenda.NextCreationSequence)
                        return "A retained disappearance fact contradicts original position, lifetime, retirement or event cause.";
                    if (removalCauses.TryGetValue(fact.EntityId, out GameFactEnvelope? causeEnvelope)
                        && (causeEnvelope.Timestamp != envelope.Timestamp || causeEnvelope.Cause != envelope.Cause))
                        return "A retained disappearance fact disagrees with its retained causal resource outcome.";
                    break;
                case CargoDispositionCommittedFact fact:
                    if (fact.Key is null || !_receipts.TryGetValue(fact.Key, out CargoDispositionReceipt? receipt)
                        || !committed.Add(fact.Key) || envelope.Timestamp != receipt.CommittedAt)
                        return "A retained cargo disposition fact has no unique matching authoritative receipt.";
                    GameFactCause expected = CargoOriginFactCause(fact.Key);
                    if (envelope.Cause != expected)
                        return "A retained cargo disposition fact disagrees with its receipt's full causal key.";
                    break;
                case CargoObjectExpiredFact fact:
                    if (!origins.TryGetValue(fact.EntityId, out RestoredCargoOrigin? origin) || origin.Receipt.Request.Key != fact.Origin
                        || !expired.Add(fact.EntityId) || _objects.ContainsKey(fact.EntityId)
                        || envelope.Timestamp != CargoDispositionArithmetic.ExpiryAt(origin.Receipt.CommittedAt, _policy.Lifetime)
                        || envelope.Timestamp > _agenda.CurrentTime || envelope.Cause is not ScheduledEventFactCause cause
                        || cause.Key.Timestamp != envelope.Timestamp || cause.Key.Phase != EventPhase.StateUpdate
                        || cause.Key.CreationSequence >= _agenda.NextCreationSequence || cause.Key.CreationSequence < (ulong)origin.Ordinal)
                        return "A retained expiry fact contradicts object origin, lifetime, retirement or allocated event cause.";
                    // An expiry is permanent destruction of the remaining item,
                    // so retained evidence cannot coexist with live custody of it.
                    if (origin.Seed.Subject is PhysicalTransferSubject.Discrete item && liveItems.Contains(item.InstanceId))
                        return "A retained expiry outcome contradicts a live discrete item identity.";
                    ulong firstSequence = cause.Key.CreationSequence - (ulong)origin.Ordinal;
                    if (expiryBases.TryGetValue(fact.Origin, out ulong previous) && previous != firstSequence)
                        return "Retained expiry causes disagree with their occurrence's canonical event allocation order.";
                    expiryBases[fact.Origin] = firstSequence;
                    break;
            }
        }
        return null;
    }

    /// <summary>Reconstructs the approved initial view from saved causal generation, never from current remaining inventory.</summary>
    private static CargoObjectView HistoricalCargoAppearance(RestoredCargoOrigin origin, EntityId entityId)
    {
        CargoObjectSeed seed = origin.Seed;
        Quantity quantity = seed.Subject is PhysicalTransferSubject.Fungible amount ? amount.Quantity : new(1);
        return new(entityId, seed.Position, seed.Definition.Key, seed.Definition.HoldingKind, quantity,
            seed.Definition.CapacityCost, new(checked(quantity.Units * seed.Definition.CapacityCost.Units)),
            seed.OriginalInstance, origin.Receipt.FieldHandle);
    }

    /// <summary>Matches complete typed causes without inferring historical keys from current agenda or coverage.</summary>
    private static GameFactCause CargoOriginFactCause(CargoDispositionKey key) => key.Trigger switch
    {
        CargoDispositionTrigger.Command command => new CommandFactCause(command.Sequence),
        CargoDispositionTrigger.Scheduled scheduled => new ScheduledEventFactCause(scheduled.Key),
        _ => throw new InvalidOperationException("Restored canonical receipt cause is unsupported."),
    };

    private static bool WasAllocatedCargoId(ulong value, IdSequenceCheckpoint sequence) =>
        value != 0 && (sequence.NextValue is not { } next || value < next);

    private static CheckpointResult<CargoDispositionOwner> CargoCheckpointRejected(string message) =>
        CheckpointResult<CargoDispositionOwner>.Rejected(new("$.checkpoint.cargo", message));

    private sealed record RestoredCargoOrigin(CargoDispositionReceipt Receipt, CargoObjectSeed Seed, int Ordinal);
}
