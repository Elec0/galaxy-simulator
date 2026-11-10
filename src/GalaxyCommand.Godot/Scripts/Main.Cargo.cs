using GalaxyCommand.Client;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;
using Godot;
using System.Globalization;

namespace GalaxyCommand.GodotClient;

public partial class Main
{
    private CargoInspectorState _cargoInspector = null!;
    private IReadOnlyDictionary<QualifiedContentKey, string> _contentNames = new Dictionary<QualifiedContentKey, string>();
    private IReadOnlyList<QualifiedContentKey> _knownCargoTypes = [];
    private ItemList _shipList = null!;
    private ItemList _cargoList = null!;
    private ItemList _priorityList = null!;
    private OptionButton _priorityType = null!;
    private OptionButton _collectionPlacement = null!;
    private Label _cargoDetails = null!;
    private Label _fieldDetails = null!;
    private Label _collectionProgress = null!;
    private Label _collectionResult = null!;
    private Button _collectObject = null!;
    private Button _collectField = null!;
    private Button _cancelCollection = null!;
    private Button _priorityRemove = null!;
    private Button _priorityAdd = null!;
    private Button _priorityUp = null!;
    private Button _priorityDown = null!;
    private readonly List<ShipId> _shipRows = [];
    private readonly List<EntityId> _cargoRows = [];
    private readonly List<QualifiedContentKey> _priorityRows = [];
    private readonly List<QualifiedContentKey> _priorityChoices = [];
    private bool _refreshingCargoUi;
    private bool _cargoCollectedThisRefresh;
    private string _cargoResultText = "";

    /// <summary>Builds keyboard-operable native semantic lists and controls using the shared locale and existing FIFO.</summary>
    private void ConfigureCargoControls()
    {
        _cargoInspector = new(_playerPrincipalId, _player);
        var panel = GetNode<PanelContainer>("Interface/CargoPanel");
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("0d1e2b") });
        panel.AccessibilityRegion = true;
        panel.AccessibilityName = UiLocalization.Text("ui.cargo.heading");
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 12);
        panel.AddChild(margin);
        var scroll = new ScrollContainer { FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        margin.AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(content);
        AddCargoLabel(content, "ui.cargo.heading");
        AddCargoLabel(content, "ui.map.ships");
        _shipList = AddSemanticList(content, "ui.map.ships", 82);
        _shipList.ItemSelected += index =>
        {
            if (!_refreshingCargoUi && index >= 0 && index < _shipRows.Count) _map.SelectShipFromList(_shipRows[(int)index]);
        };
        AddCargoLabel(content, "ui.map.cargo");
        _cargoList = AddSemanticList(content, "ui.map.cargo", 102);
        _cargoList.ItemSelected += index =>
        {
            if (!_refreshingCargoUi && index >= 0 && index < _cargoRows.Count) SelectCargo(_cargoRows[(int)index]);
        };
        _cargoDetails = AddCargoLabel(content, "ui.cargo.select");
        _fieldDetails = AddCargoLabel(content, "ui.cargo.no_field");
        _collectionPlacement = new OptionButton { FocusMode = Control.FocusModeEnum.All, AccessibilityName = UiLocalization.Text("ui.cargo.placement") };
        _collectionPlacement.AddItem(UiLocalization.Text("ui.cargo.replace"), (int)OrderPlacement.ReplaceAll);
        _collectionPlacement.AddItem(UiLocalization.Text("ui.cargo.append"), (int)OrderPlacement.Append);
        AddCargoFocusStyle(_collectionPlacement);
        content.AddChild(_collectionPlacement);
        _collectObject = AddCargoButton(content, "ui.cargo.collect_object", () => EnqueueCargo(_cargoInspector.CreateObjectCommand(_presentation.Selection.FocusedShip, CollectionPlacement)));
        _collectField = AddCargoButton(content, "ui.cargo.collect_field", () => EnqueueCargo(_cargoInspector.CreateFieldCommand(_presentation.Selection.FocusedShip, CollectionPlacement)));
        _cancelCollection = AddCargoButton(content, "ui.cargo.cancel", () => EnqueueCargo(_cargoInspector.CreateCancelCommand(_presentation.Selection.FocusedShip)));
        AddCargoLabel(content, "ui.cargo.priorities");
        AddCargoLabel(content, "ui.cargo.priority_help");
        _priorityList = AddSemanticList(content, "ui.cargo.priorities", 78);
        _priorityList.ItemSelected += _ => RefreshPriorityButtons();
        _priorityType = new OptionButton { FocusMode = Control.FocusModeEnum.All, AccessibilityName = UiLocalization.Text("ui.cargo.priority_type") };
        AddCargoFocusStyle(_priorityType);
        _priorityType.ItemSelected += _ => RefreshPriorityButtons();
        content.AddChild(_priorityType);
        _priorityAdd = AddCargoButton(content, "ui.cargo.priority_add", () =>
        {
            if (_priorityType.Selected >= 0 && _priorityType.Selected < _priorityChoices.Count) _cargoInspector.AddPriority(_priorityChoices[_priorityType.Selected]);
            RefreshCargoControls();
        });
        _priorityRemove = AddCargoButton(content, "ui.cargo.priority_remove", () => EditPriority(key => _cargoInspector.RemovePriority(key)));
        _priorityUp = AddCargoButton(content, "ui.cargo.priority_up", () => EditPriority(key => _cargoInspector.MovePriorityUp(key)));
        _priorityDown = AddCargoButton(content, "ui.cargo.priority_down", () => EditPriority(key => _cargoInspector.MovePriorityDown(key)));
        _collectionProgress = AddCargoLabel(content, "ui.cargo.no_progress");
        _collectionResult = AddCargoLabel(content, "ui.cargo.ready");
        _collectionResult.AccessibilityLive = AccessibilityServer.AccessibilityLiveMode.Polite;
        _map.CargoSelected += SelectCargo;
    }

    private OrderPlacement CollectionPlacement => (OrderPlacement)_collectionPlacement.GetSelectedId();

    /// <summary>Preserves focused ship selection while synchronizing cargo selection between map, list and inspector.</summary>
    private void SelectCargo(EntityId id)
    {
        if (!_cargoInspector.Select(id)) return;
        _map.SetCargoSelection(id);
        RefreshCargoControls();
    }

    /// <summary>Captures already validated UI intent in the application's common arrival-order buffer.</summary>
    private void EnqueueCargo(GameplayCommand? command)
    {
        if (command is null) return;
        _input.EnqueueGameplay(_player, command);
        _cargoResultText = UiLocalization.Text("ui.cargo.queued");
        RefreshCargoControls();
    }

    /// <summary>Edits only the client draft; commands already captured retain their own priority copy.</summary>
    private void EditPriority(Func<QualifiedContentKey, bool> edit)
    {
        int[] selected = _priorityList.GetSelectedItems();
        if (selected.Length > 0 && selected[0] < _priorityRows.Count) edit(_priorityRows[selected[0]]);
        RefreshCargoControls();
    }

    /// <summary>Refreshes permitted data without recreating controls, stealing focus, caching hidden cargo or canonicalizing field handles.</summary>
    private void RefreshCargoControls()
    {
        _refreshingCargoUi = true;
        try
        {
            Control? focusedControl = GetViewport().GuiGetFocusOwner();
            EntityId? priorCargo = _cargoInspector.SelectedCargoId;
            _cargoInspector.Refresh(_presentation.World.CargoObjects);
            _map.SetCargoSelection(_cargoInspector.SelectedCargoId);
            if (priorCargo is not null && _cargoInspector.SelectedCargoId is null && !_cargoCollectedThisRefresh)
                _cargoResultText = UiLocalization.Text("ui.cargo.unavailable");
            GameObservedShipSnapshot[] ships = _presentation.World.Ships.OrderBy(ship => SystemName(ship.Position?.SystemId), StringComparer.Create(UiLocalization.Culture, false))
                .ThenBy(ship => ship.Id.Value).ToArray();
            ShipId? shipFallback = RefreshSemanticList(_shipList, _shipRows, ships.Select(ship => ship.Id).ToArray(),
                ships.Select(ship => UiLocalization.Text("ui.map.ship_row", ("system", SystemName(ship.Position?.SystemId)), ("ship", Id(ship.Id.Value)))).ToArray(), _map.FocusedShipId);
            if (shipFallback is { } nextShip) Callable.From(() => _map.SelectShipFromList(nextShip)).CallDeferred();
            CargoObjectView[] cargo = _cargoInspector.VisibleCargo.OrderBy(view => SystemName(view.Position.SystemId), StringComparer.Create(UiLocalization.Culture, false))
                .ThenBy(view => CargoName(view.DefinitionKey), StringComparer.Create(UiLocalization.Culture, false)).ThenBy(view => view.EntityId.Value).ToArray();
            EntityId? cargoFallback = RefreshSemanticList(_cargoList, _cargoRows, cargo.Select(view => view.EntityId).ToArray(),
                cargo.Select(view => UiLocalization.Text("ui.map.cargo_row", ("system", SystemName(view.Position.SystemId)), ("type", CargoName(view.DefinitionKey)), ("quantity", view.Quantity), ("object", Id(view.EntityId.Value)))).ToArray(), _cargoInspector.SelectedCargoId);
            if (cargoFallback is { } nextCargo) { _cargoInspector.Select(nextCargo); _map.SetCargoSelection(nextCargo); }
            CargoObjectView? selected = _cargoInspector.SelectedCargo;
            _cargoDetails.Text = selected is null ? UiLocalization.Text("ui.cargo.select") : DescribeCargo(selected);
            IReadOnlyList<CargoObjectView> field = selected?.OriginalFieldId is { } handle ? _session.CaptureVisibleCargoField(handle) : [];
            _fieldDetails.Text = selected?.OriginalFieldId is null ? UiLocalization.Text("ui.cargo.no_field") : DescribeVisibleField(field);
            _collectObject.Disabled = _cargoInspector.CreateObjectCommand(_presentation.Selection.FocusedShip, CollectionPlacement) is null;
            _collectField.Disabled = _cargoInspector.CreateFieldCommand(_presentation.Selection.FocusedShip, CollectionPlacement) is null;
            _cancelCollection.Disabled = _cargoInspector.CreateCancelCommand(_presentation.Selection.FocusedShip) is null;
            RefreshPriorityDraft();
            _collectionProgress.Text = DescribeCollectionProgress();
            _collectionResult.Text = UiLocalization.Text("ui.cargo.order_and_result", ("state", DescribeCargoOrderResult()), ("result", _cargoResultText));
            RestoreCargoControlFocus(focusedControl);
        }
        finally { _refreshingCargoUi = false; }
    }

    /// <summary>Updates a stable semantic list in place; removal keeps keyboard focus at the next available row.</summary>
    private static T? RefreshSemanticList<T>(ItemList list, List<T> retained, T[] ids, string[] labels, T? selection) where T : struct
    {
        int prior = list.GetSelectedItems().FirstOrDefault(-1);
        bool changed = !retained.SequenceEqual(ids);
        if (changed)
        {
            list.Clear();
            retained.Clear();
            retained.AddRange(ids);
            foreach (string label in labels) list.AddItem(label);
        }
        else for (int index = 0; index < labels.Length; index++) if (list.GetItemText(index) != labels[index]) list.SetItemText(index, labels[index]);
        int selected = selection is { } id ? Array.IndexOf(ids, id) : -1;
        if (selected >= 0) list.Select(selected);
        else
        {
            list.DeselectAll();
            if (changed && list.HasFocus() && ids.Length > 0)
            {
                int fallback = Math.Clamp(prior, 0, ids.Length - 1);
                list.Select(fallback);
                return ids[fallback];
            }
        }
        return null;
    }

    /// <summary>Keeps draft type choices independent from live cargo and preserves the selected draft key across reordering.</summary>
    private void RefreshPriorityDraft()
    {
        QualifiedContentKey? selected = _priorityList.GetSelectedItems().FirstOrDefault(-1) is var index && index >= 0 && index < _priorityRows.Count ? _priorityRows[index] : null;
        IReadOnlyList<QualifiedContentKey> draft = _cargoInspector.PriorityDraft;
        if (!_priorityRows.SequenceEqual(draft))
        {
            _priorityList.Clear();
            _priorityRows.Clear();
            _priorityRows.AddRange(draft);
            foreach (QualifiedContentKey key in draft) _priorityList.AddItem(CargoName(key));
            int next = selected is null ? -1 : _priorityRows.IndexOf(selected);
            if (next < 0 && selected is not null && _priorityRows.Count > 0) next = Math.Clamp(index, 0, _priorityRows.Count - 1);
            if (next >= 0) _priorityList.Select(next);
        }
        QualifiedContentKey[] choices = _knownCargoTypes.Concat(_presentation.World.CargoObjects.Select(view => view.DefinitionKey))
            .Distinct().OrderBy(CargoName, StringComparer.Create(UiLocalization.Culture, false)).ThenBy(key => key.ToString(), StringComparer.Ordinal).ToArray();
        if (!_priorityChoices.SequenceEqual(choices))
        {
            QualifiedContentKey? priorChoice = _priorityType.Selected >= 0 && _priorityType.Selected < _priorityChoices.Count
                ? _priorityChoices[_priorityType.Selected] : null;
            _priorityType.Clear();
            _priorityChoices.Clear();
            _priorityChoices.AddRange(choices);
            foreach (QualifiedContentKey key in choices) _priorityType.AddItem(CargoName(key));
            if (choices.Length > 0) _priorityType.Select(Math.Max(0, priorChoice is null ? 0 : _priorityChoices.IndexOf(priorChoice)));
        }
        RefreshPriorityButtons();
    }

    /// <summary>Disables unavailable draft operations without inventing wrapping or duplicate priority semantics.</summary>
    private void RefreshPriorityButtons()
    {
        int index = _priorityList.GetSelectedItems().FirstOrDefault(-1);
        _priorityAdd.Disabled = _priorityType.Selected < 0 || _priorityType.Selected >= _priorityChoices.Count
            || _cargoInspector.PriorityDraft.Contains(_priorityChoices[_priorityType.Selected]);
        _priorityRemove.Disabled = index < 0;
        _priorityUp.Disabled = index <= 0;
        _priorityDown.Disabled = index < 0 || index >= _priorityRows.Count - 1;
    }

    /// <summary>Moves focus to the next logical enabled control when a refresh disables the current action.</summary>
    private void RestoreCargoControlFocus(Control? prior)
    {
        if (prior is not BaseButton { Disabled: true }) return;
        Control[] order = [_shipList, _cargoList, _collectionPlacement, _collectObject, _collectField, _cancelCollection,
            _priorityList, _priorityType, _priorityAdd, _priorityRemove, _priorityUp, _priorityDown];
        int index = Array.IndexOf(order, prior);
        if (index < 0) return;
        Control? next = order.Skip(index + 1).Concat(order.Take(index).Reverse())
            .FirstOrDefault(control => control is not BaseButton { Disabled: true });
        (next ?? _shipList).GrabFocus();
    }

    /// <summary>Uses only current covered object fields and the original stable item identity.</summary>
    private string DescribeCargo(CargoObjectView cargo) => UiLocalization.Text("ui.cargo.object_details", ("object", Id(cargo.EntityId.Value)),
        ("type", CargoName(cargo.DefinitionKey)), ("quantity", cargo.Quantity), ("storage", cargo.OccupiedStorage),
        ("item", cargo.DiscreteItem is { } item ? Id(item.Id.Value) : UiLocalization.Text("ui.cargo.fungible")));

    /// <summary>Labels totals as visible and aggregates only the facade's currently covered field subset.</summary>
    private string DescribeVisibleField(IReadOnlyList<CargoObjectView> field) => UiLocalization.Text("ui.cargo.visible_field", ("contents",
        string.Join("\n", field.GroupBy(view => view.DefinitionKey).OrderBy(group => CargoName(group.Key), StringComparer.Create(UiLocalization.Culture, false))
            .Select(group => UiLocalization.Text("ui.cargo.visible_type", ("type", CargoName(group.Key)),
                ("quantity", group.Aggregate(0m, (sum, view) => sum + view.Quantity.Units)),
                ("storage", group.Aggregate(0m, (sum, view) => sum + view.OccupiedStorage.Units)))))));

    /// <summary>Displays simulation-time progress from the focused owned attempt; local inspection never adds a map marker.</summary>
    private string DescribeCollectionProgress()
    {
        CargoCollectionProgressView? progress = _presentation.World.CargoCollectionProgress.FirstOrDefault(item => item.ShipId == _map.FocusedShipId);
        if (progress is null) return UiLocalization.Text("ui.cargo.no_progress");
        ulong now = _presentation.World.Time.Milliseconds;
        ulong elapsed = now > progress.StartedAt.Milliseconds ? Math.Min(now - progress.StartedAt.Milliseconds, progress.DueAt.Milliseconds - progress.StartedAt.Milliseconds) : 0;
        return UiLocalization.Text("ui.cargo.progress", ("ship", Id(progress.ShipId.Value)), ("type", CargoName(progress.Target.DefinitionKey)),
            ("elapsed", new SimulationDuration(elapsed)), ("duration", new SimulationDuration(progress.DueAt.Milliseconds - progress.StartedAt.Milliseconds)),
            ("quantity", progress.ReservedQuantity), ("available", progress.Target.Quantity), ("storage", progress.Target.OccupiedStorage),
            ("item", progress.Target.DiscreteItem is { } item ? Id(item.Id.Value) : UiLocalization.Text("ui.cargo.fungible")));
    }

    /// <summary>Translates only the focused owned cargo order's already disclosed state/reason.</summary>
    private string DescribeCargoOrderResult()
    {
        ShipOrderSnapshot? order = _presentation.Selection.FocusedShip?.OwnedDetail?.CurrentOrder;
        return order?.Action is not ShipActionIntent.Cargo ? UiLocalization.Text("ui.cargo.ready")
            : UiLocalization.Text("ui.cargo.order_state", ("state", UiLocalization.Text("ui.cargo.reason." + order.Reason)));
    }

    private string CargoName(QualifiedContentKey key) => UiLocalization.ContentName(key, _contentNames);
    private string SystemName(SystemId? id) => _presentation.World.Systems.FirstOrDefault(system => system.Id == id)?.Name ?? UiLocalization.Text("ui.map.transit");
    private static string Id(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Creates a wrapping native label; progress is not a live region and does not announce every simulation tick.</summary>
    private static Label AddCargoLabel(BoxContainer parent, string key)
    {
        var label = new Label { Text = UiLocalization.Text(key), AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        parent.AddChild(label);
        return label;
    }

    /// <summary>Creates a native single-selection list with standard keyboard navigation and semantic name.</summary>
    private static ItemList AddSemanticList(BoxContainer parent, string key, float height)
    {
        var list = new ItemList { CustomMinimumSize = new(0, height), FocusMode = Control.FocusModeEnum.All,
            AccessibilityName = UiLocalization.Text(key), AllowReselect = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("102635") });
        AddCargoFocusStyle(list);
        parent.AddChild(list);
        return list;
    }

    /// <summary>Creates a text-labelled native action; the engine supplies its button role, keyboard action and disabled state.</summary>
    private static Button AddCargoButton(BoxContainer parent, string key, Action pressed)
    {
        string text = UiLocalization.Text(key);
        var button = new Button { Text = text, AccessibilityName = text, FocusMode = Control.FocusModeEnum.All };
        AddCargoFocusStyle(button);
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    /// <summary>Keeps keyboard focus visible with a persistent non-color outline against the opaque control surface.</summary>
    private static void AddCargoFocusStyle(Control control)
    {
        var focus = new StyleBoxFlat { DrawCenter = false, BorderColor = Colors.White };
        focus.SetBorderWidthAll(2);
        control.AddThemeStyleboxOverride("focus", focus);
    }
}
