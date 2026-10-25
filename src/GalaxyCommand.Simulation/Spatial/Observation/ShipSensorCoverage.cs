namespace GalaxyCommand.Simulation;

/// <summary>Explicit committed ownership and sensor capability for a scoped ship.</summary>
public sealed record ShipSensorCoverageInput(
    EntityId EntityId,
    ShipId ShipId,
    PrincipalId PrincipalId,
    ulong RadiusUnits);

/// <summary>Immutable identity-only coverage evidence, without presentation or mutation authority.</summary>
public sealed record ShipSensorCoverageSnapshot(
    SimulationTime Timestamp,
    PrincipalId ObserverPrincipalId,
    IReadOnlyList<ShipId> OwnedShipIds,
    IReadOnlyList<ShipId> CoveredShipIds,
    IReadOnlyList<ShipId> VisibleShipIds);

/// <summary>Pure ship-only evaluation for the staged player observation boundary.</summary>
public static class ShipSensorCoverage
{
    /// <summary>
    /// Evaluates exactly the supplied ship scope at the view's timestamp. Only
    /// ships owned by the observer contribute circular local coverage; owned
    /// ships remain visible in transit. No source, index, discovery record,
    /// agenda entry, or presentation state survives this read-only operation.
    /// Strategy and positive batch size change only derived evaluation layout.
    /// Null inputs, zero identities, duplicate entity/ship identities, and ships
    /// absent from the spatial view are rejected before returning any evidence.
    /// Ownership and radius must come from the same owner boundary as the view;
    /// this evaluator cannot validate them against a mutable session owner.
    /// </summary>
    public static ShipSensorCoverageSnapshot Evaluate(InteractionSpatialReadView view,
        PrincipalId observerPrincipalId, IEnumerable<ShipSensorCoverageInput> inputs,
        InteractionPreparationStrategy strategy, int batchSize)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentOutOfRangeException.ThrowIfZero(observerPrincipalId.Value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ShipSensorCoverageInput[] participants = CaptureInputs(view, inputs);
        ShipSensorCoverageInput[] owned = participants.Where(input => input.PrincipalId == observerPrincipalId).ToArray();
        PreparedInteractionProximityView geometry = InteractionProximityQuery.Prepare(view);
        PreparedInteractionProximityScope scope = geometry.PrepareScope(participants.Select(input => input.ShipId), strategy);
        var covered = new HashSet<ShipId>();

        foreach (ShipSensorCoverageInput[] batch in owned.Chunk(batchSize))
        {
            ShipId[] candidates = EvaluateBatch(batch, geometry, scope);
            covered.UnionWith(candidates);
        }

        ShipId[] ownedIds = owned.Select(input => input.ShipId).OrderBy(id => id.Value).ToArray();
        return new ShipSensorCoverageSnapshot(view.Timestamp, observerPrincipalId,
            Array.AsReadOnly(ownedIds),
            Array.AsReadOnly(covered.OrderBy(id => id.Value).ToArray()),
            Array.AsReadOnly(covered.Union(ownedIds).OrderBy(id => id.Value).ToArray()));
    }

    /// <summary>
    /// Copies one explicit scope and rejects ambiguous identity or mismatched
    /// lifetime inputs before geometry preparation. Extra spatial participants
    /// outside this scope cannot become sources or contacts.
    /// </summary>
    private static ShipSensorCoverageInput[] CaptureInputs(InteractionSpatialReadView view,
        IEnumerable<ShipSensorCoverageInput> inputs)
    {
        ShipSensorCoverageInput[] captured = inputs.ToArray();
        var entities = new HashSet<EntityId>();
        var ships = new HashSet<ShipId>();
        foreach (ShipSensorCoverageInput input in captured)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentOutOfRangeException.ThrowIfZero(input.EntityId.Value, nameof(inputs));
            ArgumentOutOfRangeException.ThrowIfZero(input.ShipId.Value, nameof(inputs));
            ArgumentOutOfRangeException.ThrowIfZero(input.PrincipalId.Value, nameof(inputs));
            if (!entities.Add(input.EntityId) || !ships.Add(input.ShipId))
                throw new ArgumentException("Sensor scope must contain distinct entity and ship identities.", nameof(inputs));
            if (view.Get(input.ShipId) is null)
                throw new ArgumentException("Sensor scope references a ship absent from the captured spatial view.", nameof(inputs));
        }
        Array.Sort(captured, (left, right) => left.EntityId.Value.CompareTo(right.EntityId.Value));
        return captured;
    }

    /// <summary>
    /// Produces a private source-batch buffer for deterministic union after evaluation.
    /// Self-coverage uses prepared local geometry so transit and expired curves
    /// cannot contribute coverage through their published rounded positions.
    /// </summary>
    private static ShipId[] EvaluateBatch(ShipSensorCoverageInput[] sources,
        PreparedInteractionProximityView geometry, PreparedInteractionProximityScope scope)
    {
        var covered = new HashSet<ShipId>();
        foreach (ShipSensorCoverageInput source in sources)
        {
            if (geometry.Get(source.ShipId) is null) continue;
            covered.Add(source.ShipId);
            covered.UnionWith(scope.Find(source.ShipId, source.RadiusUnits));
        }
        return covered.OrderBy(id => id.Value).ToArray();
    }
}
