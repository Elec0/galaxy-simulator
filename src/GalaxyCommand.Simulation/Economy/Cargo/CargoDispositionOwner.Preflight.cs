namespace GalaxyCommand.Simulation;

internal sealed partial class CargoDispositionOwner
{
    /// <summary>
    /// Reconstructs the full prepared meaning without consuming randomness or IDs.
    /// Lifecycle subset checks prevent duplication; this exact comparison also
    /// prevents omitted recovery, changed receipts, or unrelated field effects.
    /// </summary>
    private bool HasExactPreparedBody(PreparedCargoDisposition prepared)
    {
        if (prepared.Request is null || prepared.SourceLoss is null || prepared.Objects?.Objects is null
            || prepared.Outcomes is null) return false;
        try
        {
            CargoDispositionRequest request = Canonicalize(prepared.Request);
            if (!RequestsMatch(request, prepared.Request) || !PoliciesMatch(_policy, request.Policy)
                || !HasSource(request, out Inventory? source)
                || request.Selected.OfType<CargoDispositionSelection.Discrete>()
                    .Any(item => source!.GetDiscrete(item.InstanceId)?.DefinitionKey != item.DefinitionKey)) return false;

            PhysicalTransferSubject[] selected = request.Selected.Select<CargoDispositionSelection, PhysicalTransferSubject>(item => item switch
            {
                CargoDispositionSelection.Fungible amount => new PhysicalTransferSubject.Fungible(amount.DefinitionKey, amount.Quantity),
                CargoDispositionSelection.Discrete itemInstance => new PhysicalTransferSubject.Discrete(itemInstance.InstanceId),
                _ => throw new InvalidOperationException("Canonical cargo selection is unsupported."),
            }).ToArray();
            PreparedCargoSourceLoss? expectedLoss = CargoSourceLoss.Prepare(source!, selected, _definitions, request.Cleanup).Prepared;
            if (expectedLoss is null || !prepared.SourceLoss.HasSameDeltaAs(expectedLoss)) return false;

            var seeds = new List<CargoObjectSeed>();
            var outcomes = new List<CargoDispositionTypeOutcome>();
            Generate(request, expectedLoss.Groups, seeds, outcomes);
            if (outcomes.Count != prepared.Outcomes.Count
                || !outcomes.Zip(prepared.Outcomes).All(pair => CargoOutcomesMatch(pair.First, pair.Second))
                || seeds.Count != prepared.Objects.Objects.Count
                || !seeds.Zip(prepared.Objects.Objects).All(pair => ExactPublicationMatches(pair.First, pair.Second))) return false;

            SimulationTime expectedExpiry = seeds.Count == 0 ? prepared.PreparedAt
                : CargoDispositionArithmetic.ExpiryAt(prepared.PreparedAt, _policy.Lifetime);
            if (prepared.ExpiresAt != expectedExpiry) return false;
            // A refreshed sequence must still leave room for every generated expiry.
            _agenda.EnsureCreationCapacity(seeds.Count);
            PreparedCargoDebrisMerge? expectedField = request.Cause == CargoLossCause.FunctionalEjection ? null
                : Fields.PrepareOccurrence(request.SourcePosition, _policy.ScatterRadiusMeters,
                    prepared.Objects.Objects.Select(entry => entry.Object.EntityId).ToArray());
            return ExactFieldMatches(expectedField, prepared.Field);
        }
        catch (ArgumentException) { return false; }
        catch (OverflowException) { return false; }
    }

    /// <summary>Requires exact generated position, amount and original instance, before any partial collection is possible.</summary>
    private static bool ExactPublicationMatches(CargoObjectSeed expected, PreparedCargoObject? actual)
    {
        if (actual?.Object is null || actual.Inventory is null || actual.Object.Position != expected.Position) return false;
        InventoryCheckpoint contents = actual.Inventory.CaptureCheckpoint();
        return expected.Subject switch
        {
            PhysicalTransferSubject.Fungible amount => contents.FungibleHoldings.Count == 1 && contents.DiscreteItems.Count == 0
                && contents.FungibleHoldings[0].DefinitionKey == amount.DefinitionKey && contents.FungibleHoldings[0].Quantity == amount.Quantity,
            PhysicalTransferSubject.Discrete item => contents.FungibleHoldings.Count == 0 && contents.DiscreteItems.Count == 1
                && contents.DiscreteItems[0].Id == item.InstanceId && contents.DiscreteItems[0].DefinitionKey == expected.Definition.Key
                && ReferenceEquals(actual.Inventory.GetDiscrete(item.InstanceId), expected.OriginalInstance),
            _ => false,
        };
    }

    /// <summary>Requires the exact merge prepared from the generated publication against the current field revision.</summary>
    private static bool ExactFieldMatches(PreparedCargoDebrisMerge? expected, PreparedCargoDebrisMerge? actual) =>
        expected is null ? actual is null : actual is not null && ReferenceEquals(expected.Owner, actual.Owner)
            && expected.ExpectedRevision == actual.ExpectedRevision && expected.OriginalFieldId == actual.OriginalFieldId
            && expected.Circle == actual.Circle && actual.Members is not null && actual.ParticipatingFields is not null
            && expected.Members.SequenceEqual(actual.Members) && expected.ParticipatingFields.SequenceEqual(actual.ParticipatingFields);
}
