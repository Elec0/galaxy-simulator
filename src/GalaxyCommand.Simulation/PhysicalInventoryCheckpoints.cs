using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

public sealed partial class Inventory
{
    /// <summary>
    /// Resolves and restores generalized holdings before applying reservations,
    /// so every commitment is validated against complete physical state.
    /// </summary>
    private static CheckpointValidationFailure? RestorePhysicalState(
        Inventory restored,
        InventoryCheckpoint checkpoint,
        PhysicalDefinitionCatalog? definitions,
        string path)
    {
        bool hasPhysicalState = checkpoint.FungibleHoldings.Count != 0 ||
            checkpoint.DiscreteItems.Count != 0 ||
            checkpoint.PhysicalReservations.Count != 0;
        if (!hasPhysicalState)
        {
            return null;
        }

        if (definitions is null)
        {
            return new CheckpointValidationFailure(
                $"{path}.fungibleHoldings",
                "Generalized inventory state requires a compatible physical-definition catalog.");
        }

        if (restored.Custody is null)
        {
            return new CheckpointValidationFailure(
                $"{path}.custody",
                "Generalized inventory state requires explicit custody.");
        }

        CheckpointValidationFailure? holdingFailure = RestoreFungibleHoldings(
            restored,
            checkpoint,
            definitions,
            path);
        if (holdingFailure is not null)
        {
            return holdingFailure;
        }

        CheckpointValidationFailure? itemFailure = RestoreDiscreteItems(
            restored,
            checkpoint,
            definitions,
            path);
        return itemFailure ?? RestorePhysicalReservations(restored, checkpoint, path);
    }

    /// <summary>
    /// Restores unique positive fungible holdings through exact compatible
    /// definitions while deriving used capacity from resolved costs.
    /// </summary>
    private static CheckpointValidationFailure? RestoreFungibleHoldings(
        Inventory restored,
        InventoryCheckpoint checkpoint,
        PhysicalDefinitionCatalog definitions,
        string path)
    {
        var keys = new HashSet<QualifiedContentKey>();
        for (int index = 0; index < checkpoint.FungibleHoldings.Count; index++)
        {
            InventoryFungibleCheckpoint? holding = checkpoint.FungibleHoldings[index];
            string holdingPath = $"{path}.fungibleHoldings[{index}]";
            if (holding is null || holding.DefinitionKey is null ||
                holding.Quantity == Quantity.Zero ||
                !keys.Add(holding.DefinitionKey))
            {
                return new CheckpointValidationFailure(
                    holdingPath,
                    "A fungible holding is missing, duplicated, or non-positive.");
            }

            PhysicalDefinition? definition = definitions.Get(holding.DefinitionKey);
            if (definition?.HoldingKind != PhysicalHoldingKind.Fungible)
            {
                return new CheckpointValidationFailure(
                    $"{holdingPath}.definitionKey",
                    "The fungible holding definition is missing or incompatible.");
            }

            InventoryStorageResult result = restored.StoreFungible(
                definition,
                holding.Quantity);
            if (!result.IsAccepted)
            {
                return new CheckpointValidationFailure(
                    holdingPath,
                    $"The fungible holding is invalid: {result.RejectionReason}.");
            }
        }

        return null;
    }

    /// <summary>
    /// Restores unique discrete identities through exact compatible definitions
    /// without allocating replacement item identities.
    /// </summary>
    private static CheckpointValidationFailure? RestoreDiscreteItems(
        Inventory restored,
        InventoryCheckpoint checkpoint,
        PhysicalDefinitionCatalog definitions,
        string path)
    {
        var ids = new HashSet<ItemInstanceId>();
        for (int index = 0; index < checkpoint.DiscreteItems.Count; index++)
        {
            InventoryDiscreteItemCheckpoint? item = checkpoint.DiscreteItems[index];
            string itemPath = $"{path}.discreteItems[{index}]";
            if (item is null || item.Id.Value == 0 || item.DefinitionKey is null ||
                !ids.Add(item.Id))
            {
                return new CheckpointValidationFailure(
                    itemPath,
                    "A discrete item is missing, duplicated, or invalid.");
            }

            PhysicalDefinition? definition = definitions.Get(item.DefinitionKey);
            if (definition?.HoldingKind != PhysicalHoldingKind.Discrete)
            {
                return new CheckpointValidationFailure(
                    $"{itemPath}.definitionKey",
                    "The discrete item definition is missing or incompatible.");
            }

            InventoryStorageResult result = restored.StoreDiscrete(definition, item.Id);
            if (!result.IsAccepted)
            {
                return new CheckpointValidationFailure(
                    itemPath,
                    $"The discrete item is invalid: {result.RejectionReason}.");
            }
        }

        return null;
    }

    /// <summary>
    /// Restores every generalized reservation only after all referenced
    /// holdings and used capacity are present.
    /// </summary>
    private static CheckpointValidationFailure? RestorePhysicalReservations(
        Inventory restored,
        InventoryCheckpoint checkpoint,
        string path)
    {
        for (int index = 0; index < checkpoint.PhysicalReservations.Count; index++)
        {
            PhysicalReservation? reservation = checkpoint.PhysicalReservations[index];
            string reservationPath = $"{path}.physicalReservations[{index}]";
            if (reservation is null || reservation.InventoryId != checkpoint.Id ||
                reservation.Subject is null || reservation.Owner is null)
            {
                return new CheckpointValidationFailure(
                    reservationPath,
                    "A physical reservation is missing or names the wrong inventory.");
            }

            InventoryReservationResult result = restored.ReservePhysical(
                reservation.Id,
                reservation.Subject,
                reservation.Owner);
            if (!result.IsAccepted)
            {
                return new CheckpointValidationFailure(
                    reservationPath,
                    $"The physical reservation is invalid: {result.RejectionReason}.");
            }
        }

        return null;
    }
}
