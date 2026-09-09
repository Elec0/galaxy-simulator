using GalaxyCommand.Simulation;

namespace GalaxyCommand.GodotClient;

/// <summary>
/// Converts disposable map selection state into one explicit gameplay command
/// without retaining selection in authoritative simulation state.
/// </summary>
internal static class MapSelectionCommandFactory
{
    /// <summary>
    /// Uses a captured multi-selection only for replacement moves. Append
    /// retains the existing focused-ship behavior because group append is not
    /// part of the initial group-command contract.
    /// </summary>
    internal static GameplayCommand? CreateMove(
        IReadOnlyList<ShipId> selectedShipIds,
        ShipId? focusedShipId,
        SystemPosition destination,
        OrderPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(selectedShipIds);
        if (selectedShipIds.Count > 1 && placement == OrderPlacement.ReplaceAll)
        {
            return new MoveShipGroupCommand(selectedShipIds, destination);
        }

        return focusedShipId is { } shipId
            ? new MoveShipCommand(
                shipId,
                new NavigationDestination.Position(destination),
                placement)
            : null;
    }

    /// <summary>
    /// Uses a captured multi-selection for current-order cancellation, letting
    /// the authoritative group command treat idle members as no-ops.
    /// </summary>
    internal static GameplayCommand? CreateCancel(
        IReadOnlyList<ShipId> selectedShipIds,
        ShipId focusedShipId,
        ShipOrderSnapshot? currentFocusedOrder)
    {
        ArgumentNullException.ThrowIfNull(selectedShipIds);
        if (selectedShipIds.Count > 1 && selectedShipIds.Contains(focusedShipId))
        {
            return new CancelShipGroupCommand(selectedShipIds);
        }

        return currentFocusedOrder is null
            || currentFocusedOrder.Status is ShipOrderStatus.Completed
                or ShipOrderStatus.Cancelled
                or ShipOrderStatus.Failed
            ? null
            : new CancelShipOrderCommand(focusedShipId, currentFocusedOrder.Id);
    }
}
