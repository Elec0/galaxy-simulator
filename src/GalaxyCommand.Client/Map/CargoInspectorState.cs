using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client;

/// <summary>Disposable cargo selection and priority draft, independent from focused ship selection and simulation authority.</summary>
internal sealed class CargoInspectorState
{
    private readonly PrincipalId _playerPrincipal;
    private readonly CommandSource _playerSource;
    private readonly List<QualifiedContentKey> _priorities = [];
    private Dictionary<EntityId, CargoObjectView> _byId = [];
    private IReadOnlyList<CargoObjectView> _visible = Array.Empty<CargoObjectView>();

    /// <summary>Binds local controls to the registered player identity without granting command admission authority.</summary>
    internal CargoInspectorState(PrincipalId playerPrincipal, CommandSource playerSource)
    {
        ArgumentOutOfRangeException.ThrowIfZero(playerPrincipal.Value);
        ArgumentNullException.ThrowIfNull(playerSource);
        if (playerSource.Kind != CommandSourceKind.Player)
            throw new ArgumentException("Cargo inspector actions require the player's configured command source.", nameof(playerSource));
        _playerPrincipal = playerPrincipal;
        _playerSource = playerSource;
    }

    internal EntityId? SelectedCargoId { get; private set; }
    internal CargoObjectView? SelectedCargo => SelectedCargoId is { } id ? _byId.GetValueOrDefault(id) : null;
    internal IReadOnlyList<CargoObjectView> VisibleCargo => _visible;
    internal IReadOnlyList<QualifiedContentKey> PriorityDraft => Array.AsReadOnly(_priorities.ToArray());

    /// <summary>
    /// Copies the latest permitted views and clears an unavailable cargo selection.
    /// Ship focus and the priority draft remain with their respective local owners.
    /// Invalid duplicate input rejects before changing any existing selection or view.
    /// </summary>
    internal void Refresh(IReadOnlyList<CargoObjectView> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        var captured = new Dictionary<EntityId, CargoObjectView>();
        foreach (CargoObjectView view in visible)
            if (view is null || !captured.TryAdd(view.EntityId, view))
                throw new ArgumentException("Visible cargo identities must be nonnull and unique.", nameof(visible));
        _byId = captured;
        _visible = Array.AsReadOnly(captured.Values.OrderBy(view => view.EntityId.Value).ToArray());
        if (SelectedCargoId is { } id && !captured.ContainsKey(id)) SelectedCargoId = null;
    }

    /// <summary>Selects only a currently permitted cargo object without altering focused ship selection.</summary>
    internal bool Select(EntityId id)
    {
        if (!_byId.ContainsKey(id)) return false;
        SelectedCargoId = id;
        return true;
    }

    /// <summary>Adds a unique type to the local draft; exact content admission remains with the session command boundary.</summary>
    internal bool AddPriority(QualifiedContentKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_priorities.Contains(key)) return false;
        _priorities.Add(key);
        return true;
    }

    /// <summary>Removes an existing draft priority without changing commands already captured for submission.</summary>
    internal bool RemovePriority(QualifiedContentKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _priorities.Remove(key);
    }

    /// <summary>Moves an existing priority one position earlier without wrapping at the start.</summary>
    internal bool MovePriorityUp(QualifiedContentKey key) => MovePriority(key, -1);

    /// <summary>Moves an existing priority one position later without wrapping at the end.</summary>
    internal bool MovePriorityDown(QualifiedContentKey key) => MovePriority(key, 1);

    /// <summary>Captures one authorized focused ship and selected visible object; the caller enqueues the immutable command through the shared FIFO.</summary>
    internal CollectCargoObjectCommand? CreateObjectCommand(GameObservedShipSnapshot? focused, OrderPlacement placement) =>
        IsAuthorized(focused) && SelectedCargo is { } cargo ? new(focused!.Id, cargo.EntityId, placement) : null;

    /// <summary>Captures the selected object's original field handle and an independent copy of the current priority draft.</summary>
    internal CollectCargoFieldCommand? CreateFieldCommand(GameObservedShipSnapshot? focused, OrderPlacement placement) =>
        IsAuthorized(focused) && SelectedCargo?.OriginalFieldId is { } field ? new(focused!.Id, field, _priorities, placement) : null;

    /// <summary>Captures only the focused ship's nonterminal current order, even after cargo coverage is lost; no group cancellation is inferred.</summary>
    internal CancelShipOrderCommand? CreateCancelCommand(GameObservedShipSnapshot? focused) =>
        IsAuthorized(focused) && focused!.OwnedDetail!.CurrentOrder is { Status: not (ShipOrderStatus.Completed or ShipOrderStatus.Cancelled or ShipOrderStatus.Failed) } order
            ? new(focused.Id, order.Id) : null;

    /// <summary>Requires permitted owned detail and the exact active player controller; the session revalidates at FIFO admission.</summary>
    private bool IsAuthorized(GameObservedShipSnapshot? focused) => focused?.PrincipalId == _playerPrincipal
        && focused.OwnedDetail is { } detail && detail.Id == focused.Id && detail.PrincipalId == _playerPrincipal
        && detail.Control.ActiveController.Matches(_playerSource);

    /// <summary>Preserves unique priority ordering while treating missing keys and list boundaries as unchanged drafts.</summary>
    private bool MovePriority(QualifiedContentKey key, int offset)
    {
        ArgumentNullException.ThrowIfNull(key);
        int current = _priorities.IndexOf(key);
        int next = current + offset;
        if (current < 0 || next < 0 || next >= _priorities.Count) return false;
        (_priorities[current], _priorities[next]) = (_priorities[next], _priorities[current]);
        return true;
    }
}
