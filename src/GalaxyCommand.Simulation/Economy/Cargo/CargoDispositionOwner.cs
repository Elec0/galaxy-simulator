using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Shared cargo transaction authority. Evaluation is stable and buffered; the serial
/// commit joins source removal, lifecycle, fields, agenda, receipts, and diagnostic facts.
/// </summary>
internal sealed partial class CargoDispositionOwner
{
    private readonly EntityLifecycleOwner _lifecycle;
    private readonly PhysicalDefinitionCatalog _definitions;
    private readonly CargoDispositionPolicy _policy;
    private readonly DeterministicRandomOwner _random;
    private readonly EventAgenda<GameEvent> _agenda;
    private readonly GameFactStore _facts;
    private readonly Dictionary<CargoDispositionKey, CargoDispositionReceipt> _receipts = [];
    private readonly SortedDictionary<EntityId, CargoLiveObject> _objects = new(EntityIdComparer<EntityId>.Instance);
    private bool _isHealthy = true;

    internal CargoDispositionOwner(EntityLifecycleOwner lifecycle, PhysicalDefinitionCatalog definitions,
        CargoDispositionPolicy policy, DeterministicRandomOwner random, EventAgenda<GameEvent> agenda, GameFactStore facts)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _agenda = agenda ?? throw new ArgumentNullException(nameof(agenda));
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        Fields = new CargoDebrisFields(policy.MergeBehaviorVersion);
    }

    /// <summary>
    /// Prepares all cargo-owned participants without mutation. Exact replay is resolved
    /// before source lookup, allowing a removed source to remain replayable forever.
    /// Station accounting and workflow invalidation remain the causal owner's prepared deltas.
    /// </summary>
    internal CargoDispositionPreparation Prepare(CargoDispositionRequest request)
    {
        ThrowIfUnhealthy();
        CargoDispositionRequest canonical;
        try { canonical = Canonicalize(request); }
        catch (ArgumentException) { return new(Failure: CargoDispositionFailure.InvalidInput); }
        catch (OverflowException) { return new(Failure: CargoDispositionFailure.NumericExhaustion); }
        if (_receipts.TryGetValue(canonical.Key, out CargoDispositionReceipt? previous))
            return RequestsMatch(previous.Request, canonical) ? new(Receipt: previous) : new(Failure: CargoDispositionFailure.ConflictingReplay);
        if (!IsActiveCause(canonical)) return new(Failure: CargoDispositionFailure.InvalidInput);
        if (!PoliciesMatch(_policy, canonical.Policy)) return new(Failure: CargoDispositionFailure.PolicyMismatch);
        if (!HasSource(canonical, out Inventory? source)) return new(Failure: CargoDispositionFailure.SourceUnavailable);
        if (canonical.Selected.OfType<CargoDispositionSelection.Discrete>()
            .Any(item => source!.GetDiscrete(item.InstanceId)?.DefinitionKey != item.DefinitionKey))
            return new(Failure: CargoDispositionFailure.SourceLossRejected);
        PhysicalTransferSubject[] selected = canonical.Selected.Select<CargoDispositionSelection, PhysicalTransferSubject>(item => item switch
        {
            CargoDispositionSelection.Fungible fungible => new PhysicalTransferSubject.Fungible(fungible.DefinitionKey, fungible.Quantity),
            CargoDispositionSelection.Discrete discrete => new PhysicalTransferSubject.Discrete(discrete.InstanceId),
            _ => throw new InvalidOperationException("Canonical cargo selection contains an unsupported variant."),
        }).ToArray();
        CargoSourceLossPreparation loss = CargoSourceLoss.Prepare(source!, selected, _definitions, canonical.Cleanup);
        if (loss.Prepared is null) return new(Failure: CargoDispositionFailure.SourceLossRejected);
        if (!_facts.CanCommit(1)) return new(Failure: CargoDispositionFailure.FactCapacityExhausted);
        try
        {
            var seeds = new List<CargoObjectSeed>();
            var outcomes = new List<CargoDispositionTypeOutcome>();
            Generate(canonical, loss.Prepared.Groups, seeds, outcomes);
            if (!_facts.CanCommit(checked(seeds.Count + 1))) return new(Failure: CargoDispositionFailure.FactCapacityExhausted);
            if (!_lifecycle.TryPrepareCargoObjects(seeds, out PreparedCargoObjectBatch? objects))
                return new(Failure: CargoDispositionFailure.IdentifierExhausted);
            SimulationTime expiresAt = seeds.Count == 0 ? _agenda.CurrentTime : CargoDispositionArithmetic.ExpiryAt(_agenda.CurrentTime, _policy.Lifetime);
            _agenda.EnsureCreationCapacity(seeds.Count);
            PreparedCargoDebrisMerge? field = canonical.Cause == CargoLossCause.FunctionalEjection ? null
                : Fields.PrepareOccurrence(canonical.SourcePosition, _policy.ScatterRadiusMeters, objects!.Objects.Select(entry => entry.Object.EntityId).ToArray());
            var prepared = new PreparedCargoDisposition(this, canonical, loss.Prepared, objects!, field,
                Array.AsReadOnly(outcomes.ToArray()), _agenda.CurrentTime, expiresAt, _agenda.NextCreationSequence);
            return CanCommit(prepared) ? new(Prepared: prepared) : new(Failure: CargoDispositionFailure.StalePreparation);
        }
        catch (OverflowException) { return new(Failure: CargoDispositionFailure.NumericExhaustion); }
    }

    /// <summary>Revalidates every cargo-owned participant and shared sequence before any source holding changes.</summary>
    internal bool CanCommit(PreparedCargoDisposition prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return _isHealthy && ReferenceEquals(prepared.Owner, this) && HasExactPreparedBody(prepared)
            && !_receipts.ContainsKey(prepared.Request.Key)
            && prepared.PreparedAt == _agenda.CurrentTime && prepared.AgendaSequence == _agenda.NextCreationSequence
            && IsActiveCause(prepared.Request)
            && HasSource(prepared.Request, out Inventory? source) && ReferenceEquals(source, prepared.SourceLoss.Source)
            && prepared.SourceLoss.CanApply() && _lifecycle.CanCommitCargoObjects(prepared.Objects, prepared.SourceLoss)
            && (prepared.Field is null || Fields.CanCommit(prepared.Field))
            && prepared.Objects.Objects.Count < int.MaxValue && _facts.CanCommit(prepared.Objects.Objects.Count + 1);
    }

    /// <summary>
    /// Applies a fully prevalidated cargo commit. Rejection is mutation-free; an
    /// unexpected post-mutation failure poisons this owner for its enclosing session.
    /// Facts publish last and exact replay never allocates more work or identities.
    /// </summary>
    internal CargoDispositionCommitResult Commit(PreparedCargoDisposition prepared)
    {
        ThrowIfUnhealthy();
        ArgumentNullException.ThrowIfNull(prepared);
        if (!ReferenceEquals(prepared.Owner, this)) return new(null, CargoDispositionFailure.StalePreparation, false);
        if (_receipts.TryGetValue(prepared.Request.Key, out CargoDispositionReceipt? replay))
            return RequestsMatch(replay.Request, prepared.Request) ? new(replay, null, false) : new(null, CargoDispositionFailure.ConflictingReplay, false);
        if (!CanCommit(prepared)) return new(null, CargoDispositionFailure.StalePreparation, false);
        try
        {
            if (!prepared.SourceLoss.Apply()) throw new InvalidOperationException("Prepared source loss changed during cargo commit.");
            _lifecycle.ApplyPreparedCargoObjects(prepared.Objects);
            if (prepared.Field is not null) Fields.Apply(prepared.Field);
            foreach (PreparedCargoObject entry in prepared.Objects.Objects)
            {
                EventKey expiry = _agenda.Schedule(prepared.ExpiresAt, EventPhase.StateUpdate, default,
                    new GameEvent.CargoExpiry(entry.Object.EntityId));
                _objects.Add(entry.Object.EntityId, new CargoLiveObject(entry.Object.EntityId, prepared.Request.Key,
                    prepared.PreparedAt, prepared.ExpiresAt, _policy.Key, expiry, default, prepared.Field?.OriginalFieldId));
            }
            var receipt = new CargoDispositionReceipt(prepared.Request, prepared.PreparedAt, prepared.Outcomes,
                Array.AsReadOnly(prepared.Objects.Objects.Select(entry => entry.Object.EntityId).ToArray()), prepared.Field?.OriginalFieldId);
            _receipts.Add(receipt.Request.Key, receipt);
            GameFactCause cause = prepared.Request.Key.Trigger switch
            {
                CargoDispositionTrigger.Command command => new CommandFactCause(command.Sequence),
                CargoDispositionTrigger.Scheduled scheduled => new ScheduledEventFactCause(scheduled.Key),
                _ => throw new InvalidOperationException("Prepared cargo trigger is unsupported."),
            };
            var proposals = new List<GameFactProposal> { new(new(GameFactCommitCategory.EntityLifecycle,
                prepared.Request.Key.SourceEntityId.Value, prepared.Request.Key.OccurrenceOrdinal, 0),
                new CargoDispositionCommittedFact(prepared.Request.Key)) };
            foreach (PreparedCargoObject entry in prepared.Objects.Objects)
                proposals.Add(new(new(GameFactCommitCategory.EntityLifecycle, entry.Object.EntityId.Value, 0, 0),
                    new CargoObjectAppearedFact(CaptureObjectView(entry.Object.EntityId)
                        ?? throw new InvalidOperationException("Published cargo requires its committed appearance view."))));
            _facts.Commit(prepared.PreparedAt, cause, proposals);
            return new(receipt, null, true);
        }
        catch { _isHealthy = false; throw; }
    }

    internal IReadOnlyList<CargoLiveObject> CaptureObjects() => Array.AsReadOnly(_objects.Values.ToArray());

    /// <summary>Captures receipts in canonical typed-cause order without relying on dictionary enumeration.</summary>
    internal IReadOnlyList<CargoDispositionReceipt> CaptureReceipts() => Array.AsReadOnly(_receipts.Values
        .OrderBy(receipt => receipt.Request.Key, CargoDispositionKeyComparer.Instance).ToArray());

    internal CargoDebrisFields Fields { get; private set; }
    internal bool IsHealthy => _isHealthy;

    /// <summary>Generates each canonical type independently so survival rounding never crosses type or cause boundaries.</summary>
    private void Generate(CargoDispositionRequest request, IReadOnlyList<CargoSourceLossGroup> groups,
        List<CargoObjectSeed> seeds, List<CargoDispositionTypeOutcome> outcomes)
    {
        CargoRecoveryFraction recovery = _policy.RecoveryFor(request.Cause);
        foreach (CargoSourceLossGroup group in groups)
        {
            Quantity surviving = CargoDispositionArithmetic.SurvivalCount(group.Quantity, recovery.Numerator, recovery.Denominator);
            CargoDispositionGeneration generator = request.Key.Trigger switch
            {
                CargoDispositionTrigger.Command command => new(_random, request.Key.SourceEntityId, command.Sequence,
                    request.Key.OccurrenceOrdinal, _policy.GenerationBehaviorVersion, group.Definition.Key),
                CargoDispositionTrigger.Scheduled scheduled => new(_random, request.Key.SourceEntityId, scheduled.Key,
                    request.Key.OccurrenceOrdinal, _policy.GenerationBehaviorVersion, group.Definition.Key),
                _ => throw new InvalidOperationException("Canonical cargo trigger is unsupported."),
            };
            ItemInstanceId[] survivors = [];
            if (group.Definition.HoldingKind == PhysicalHoldingKind.Fungible)
            {
                Quantity[] shares = generator.Partition(surviving, _policy.FungibleObjectCount);
                for (int index = 0; index < shares.Length; index++)
                    seeds.Add(new(generator.Scatter(request.SourcePosition, (long)_policy.ScatterRadiusMeters, (ulong)index),
                        group.Definition, new PhysicalTransferSubject.Fungible(group.Definition.Key, shares[index])));
            }
            else
            {
                survivors = generator.SelectSurvivors(group.DiscreteItems.Select(item => item.Id).ToArray(), checked((int)surviving.Units));
                Dictionary<ItemInstanceId, DiscreteItemInstance> instances = group.DiscreteItems.ToDictionary(item => item.Id);
                foreach (ItemInstanceId id in survivors)
                    seeds.Add(new(generator.Scatter(request.SourcePosition, (long)_policy.ScatterRadiusMeters, id.Value),
                        group.Definition, new PhysicalTransferSubject.Discrete(id), instances[id]));
            }
            outcomes.Add(new(group.Definition.Key, group.Definition.HoldingKind, group.Quantity,
                group.Quantity.Subtract(surviving), surviving, Array.AsReadOnly(survivors)));
        }
    }

    /// <summary>Requires a live registered principal-controlled source without admitting floating cargo as a new emitter.</summary>
    private bool HasSource(CargoDispositionRequest request, out Inventory? source)
    {
        source = _lifecycle.Inventories.Get(request.SourceInventoryId);
        return _lifecycle.Entities.GetKind(request.Key.SourceEntityId) is not null and not EntityKind.CargoObject
            && source?.Custody is InventoryCustody.PrincipalControlled custody
            && custody.PhysicalOwner is InventoryOwnerReference.SessionEntity entity && entity.EntityId == request.Key.SourceEntityId;
    }

    /// <summary>Requires scheduled causes to be the currently executing allocated timestamp/phase identity.</summary>
    private bool IsActiveCause(CargoDispositionRequest request) => request.Key.Trigger is not CargoDispositionTrigger.Scheduled scheduled
        || scheduled.Key.Timestamp == _agenda.CurrentTime && scheduled.Key.Phase == _agenda.CurrentPhase
            && scheduled.Key.CreationSequence < _agenda.NextCreationSequence;

    /// <summary>Copies and normalizes replay inputs independently of live source state and enumeration order.</summary>
    private CargoDispositionRequest Canonicalize(CargoDispositionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Key is null || request.Key.SourceEntityId.Value == 0 || request.SourceInventoryId.Value == 0
            || request.Policy is null || request.Selected is null || request.Cleanup is null || !Enum.IsDefined(request.Cause)
            || request.SourcePosition.SystemId.Value == 0
            || request.SourcePosition.Position.X.Units is < -(1L << 50) or > (1L << 50)
            || request.SourcePosition.Position.Y.Units is < -(1L << 50) or > (1L << 50))
            throw new ArgumentException("Cargo disposition requires complete causal, policy and spatial input.", nameof(request));
        bool triggerValid = request.Key.Trigger switch
        {
            CargoDispositionTrigger.Command command => command.Sequence.Value != 0,
            CargoDispositionTrigger.Scheduled scheduled => Enum.IsDefined(scheduled.Key.Phase),
            _ => false,
        };
        if (!triggerValid) throw new ArgumentException("Cargo disposition trigger is invalid.", nameof(request));
        var totals = new Dictionary<QualifiedContentKey, Quantity>();
        var items = new Dictionary<ItemInstanceId, QualifiedContentKey>();
        foreach (CargoDispositionSelection? selection in request.Selected)
        {
            PhysicalDefinition? definition = selection?.DefinitionKey is { } key ? _definitions.Get(key) : null;
            switch (selection)
            {
                case CargoDispositionSelection.Fungible fungible when definition?.HoldingKind == PhysicalHoldingKind.Fungible && fungible.Quantity != Quantity.Zero:
                    totals[fungible.DefinitionKey] = totals.GetValueOrDefault(fungible.DefinitionKey, Quantity.Zero).Add(fungible.Quantity);
                    break;
                case CargoDispositionSelection.Discrete discrete when definition?.HoldingKind == PhysicalHoldingKind.Discrete && discrete.InstanceId.Value != 0:
                    if (!items.TryAdd(discrete.InstanceId, discrete.DefinitionKey)) throw new ArgumentException("Selected item is duplicated.", nameof(request));
                    break;
                default: throw new ArgumentException("Cargo selection is invalid or incompatible with its exact definition.", nameof(request));
            }
        }
        var normalized = totals.Select(pair => (CargoDispositionSelection)new CargoDispositionSelection.Fungible(pair.Key, pair.Value))
            .Concat(items.Select(pair => new CargoDispositionSelection.Discrete(pair.Value, pair.Key)))
            .OrderBy(item => item.DefinitionKey.ToString(), StringComparer.Ordinal)
            .ThenBy(item => (item as CargoDispositionSelection.Discrete)?.InstanceId.Value ?? 0).ToArray();
        if (request.Cleanup.Any(cleanup => cleanup is null || cleanup.ReservationId.Value == 0 || cleanup.Owner is null))
            throw new ArgumentException("Cargo cleanup input is invalid.", nameof(request));
        return request with { Selected = Array.AsReadOnly(normalized), Cleanup = Array.AsReadOnly(request.Cleanup
            .OrderBy(cleanup => cleanup is CargoSourceReservationCleanup.Physical ? 0 : 1).ThenBy(cleanup => cleanup.ReservationId.Value).ToArray()) };
    }

    /// <summary>Compares canonical semantic input; localized names and container identity do not participate in replay.</summary>
    private static bool RequestsMatch(CargoDispositionRequest left, CargoDispositionRequest right) => left.Key == right.Key
        && left.SourceInventoryId == right.SourceInventoryId && left.SourcePosition == right.SourcePosition
        && left.Cause == right.Cause && PoliciesMatch(left.Policy, right.Policy)
        && left.Selected.SequenceEqual(right.Selected) && left.Cleanup.SequenceEqual(right.Cleanup);

    /// <summary>Requires exact qualified policy meaning rather than accepting an equal display name or a launch default.</summary>
    private static bool PoliciesMatch(CargoDispositionPolicy left, CargoDispositionPolicy right) => left.Key == right.Key
        && left.GenerationBehaviorVersion == right.GenerationBehaviorVersion && left.MergeBehaviorVersion == right.MergeBehaviorVersion
        && left.OrderingBehaviorVersion == right.OrderingBehaviorVersion && left.DurationBehaviorVersion == right.DurationBehaviorVersion
        && left.FungibleObjectCount == right.FungibleObjectCount && left.ScatterRadiusMeters == right.ScatterRadiusMeters
        && left.Lifetime == right.Lifetime && left.MinimumCollectionDuration == right.MinimumCollectionDuration
        && left.MaximumCollectionDuration == right.MaximumCollectionDuration && left.CollectionStorageCap == right.CollectionStorageCap
        && left.RecoveryByCause.SequenceEqual(right.RecoveryByCause);

    /// <summary>Stops authoritative work after an unexpected partially applied transaction.</summary>
    private void ThrowIfUnhealthy()
    {
        if (!_isHealthy) throw new InvalidOperationException("Cargo authority is unhealthy after a failed commit.");
    }
}
