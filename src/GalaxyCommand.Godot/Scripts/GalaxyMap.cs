using GalaxyCommand.Simulation;
using Godot;

namespace GalaxyCommand.GodotClient;

/// <summary>
/// Development-only canvas that renders validated static topology alongside
/// immutable observer-safe presentation snapshots. Camera, view context, and
/// selection remain local presentation state and never enter the session.
/// </summary>
public partial class GalaxyMap : Control
{
	private const float SystemHitRadius = 18.0f;
	private const float ShipHitRadius = 14.0f;
	private const float MinimumZoom = 0.25f;
	private const float MaximumZoom = 4.0f;
	private readonly SortedSet<ShipId> _selectedShipIds = new(
		Comparer<ShipId>.Create((first, second) =>
			first.Value.CompareTo(second.Value)));
	private readonly Dictionary<SystemId, Vector2> _galaxyLayout = [];
	private GamePresentationWorldSnapshot? _snapshot;
	private SystemId? _systemContext;
	private Vector2 _galaxyCamera;
	private Vector2 _systemCamera;
	private float _galaxyZoom = 1.0f;
	private float _systemZoom = 1.0f;
	private bool _isPanning;
	private Vector2 _lastPointerPosition;
	private MapScale _scale = MapScale.Galaxy;

	public event Action? SelectionChanged;
	public event Action? ViewChanged;
	public event Action<SystemPosition, OrderPlacement>? DestinationRequested;
	public event Action<ShipId>? CancelRequested;

	public IReadOnlyList<ShipId> SelectedShipIds => _selectedShipIds.ToArray();

	public ShipId? FocusedShipId { get; private set; }

	public string ViewDescription => _scale == MapScale.Galaxy
		? "GALAXY"
		: $"SYSTEM {CurrentSystem()?.Name.ToUpperInvariant() ?? "UNAVAILABLE"}";

	public override void _Ready()
	{
		Resized += QueueRedraw;
	}

	/// <summary>
	/// Accepts the validated presentation-only layout after scenario loading.
	/// It deliberately receives no mutable simulation domain or save data.
	/// </summary>
	public void ConfigureGalaxyLayout(IEnumerable<StaticGalaxyLayoutEntry> layout)
	{
		ArgumentNullException.ThrowIfNull(layout);
		_galaxyLayout.Clear();
		foreach (StaticGalaxyLayoutEntry entry in layout)
		{
			_galaxyLayout.Add(entry.SystemId, new Vector2((float)entry.X, (float)entry.Y));
		}

		QueueRedraw();
	}

	/// <summary>
	/// Refreshes the canvas from one immutable presentation capture and removes
	/// selections that the facade could no longer resolve for this observer.
	/// </summary>
	public void Display(GamePresentationSnapshot presentation)
	{
		ArgumentNullException.ThrowIfNull(presentation);
		_snapshot = presentation.World;
		foreach (ShipId unresolved in presentation.Selection.UnresolvedShipIds)
		{
			_selectedShipIds.Remove(unresolved);
		}

		if (FocusedShipId is not { } focused || !_selectedShipIds.Contains(focused))
		{
			FocusedShipId = _selectedShipIds.Count == 0 ? null : _selectedShipIds.Min;
		}

		if (_systemContext is null || !_snapshot.Systems.Any(system => system.Id == _systemContext))
		{
			_systemContext = _snapshot.Systems.Count == 0
				? null
				: _snapshot.Systems[0].Id;
		}

		QueueRedraw();
	}

	/// <summary>Returns to the complete declared public topology view.</summary>
	public void ShowGalaxy()
	{
		if (_scale == MapScale.Galaxy)
		{
			return;
		}

		_scale = MapScale.Galaxy;
		ViewChanged?.Invoke();
		QueueRedraw();
	}

	/// <summary>
	/// Restores the active view's initial local framing without submitting a
	/// command or making the camera follow later motion automatically.
	/// </summary>
	public void Recenter()
	{
		if (_scale == MapScale.Galaxy)
		{
			_galaxyCamera = Vector2.Zero;
		}
		else
		{
			GameShipSnapshot? focused = FocusedShipId is { } focusedId
				? _snapshot?.Ships.FirstOrDefault(ship => ship.Id == focusedId)
				: null;
			_systemCamera = focused?.Position is { } position && position.SystemId == _systemContext
				? ToVector(position.Position)
				: Vector2.Zero;
		}

		QueueRedraw();
	}

	// Godot declares this override parameter as `event`; preserving that external
	// metadata name is required by CA1725 even though it needs C# escaping.
	public override void _GuiInput(InputEvent @event)
	{
		if (_snapshot is null)
		{
			return;
		}

		if (@event is InputEventMouseMotion motion && _isPanning)
		{
			PanBy(motion.Position - _lastPointerPosition);
			_lastPointerPosition = motion.Position;
			AcceptEvent();
			return;
		}

		if (@event is not InputEventMouseButton mouse)
		{
			return;
		}

		if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
		{
			ZoomAt(mouse.Position, mouse.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1 / 1.2f);
			AcceptEvent();
			return;
		}

		if (mouse.ButtonIndex == MouseButton.Middle)
		{
			_isPanning = mouse.Pressed;
			_lastPointerPosition = mouse.Position;
			AcceptEvent();
			return;
		}

		if (!mouse.Pressed)
		{
			return;
		}

		if (mouse.ButtonIndex == MouseButton.Right)
		{
			if (FocusedShipId is { } focused)
			{
				CancelRequested?.Invoke(focused);
				AcceptEvent();
			}

			return;
		}

		if (mouse.ButtonIndex != MouseButton.Left)
		{
			return;
		}

		if (_scale == MapScale.Galaxy)
		{
			OpenSystemAt(mouse.Position);
			return;
		}

		SelectShipOrRequestMove(mouse);
	}

	public override void _Draw()
	{
		if (_snapshot is null)
		{
			return;
		}

		DrawRect(new Rect2(Vector2.Zero, Size), new Color("071521"));
		if (_scale == MapScale.Galaxy)
		{
			DrawGalaxy();
		}
		else
		{
			DrawSystem();
		}
	}

	private void DrawGalaxy()
	{
		DrawString(ThemeDB.FallbackFont, new Vector2(28, 118), "GALAXY | click a system to inspect | wheel to zoom | middle drag to pan", fontSize: 13, modulate: new Color("668ca5"));
		foreach (TransitConnectionSnapshot connection in _snapshot!.TransitConnections.OrderBy(connection => connection.Id.Value))
		{
			ConnectorEndpointSnapshot? source = _snapshot.ConnectorEndpoints.FirstOrDefault(endpoint => endpoint.Id == connection.SourceEndpointId);
			ConnectorEndpointSnapshot? destination = _snapshot.ConnectorEndpoints.FirstOrDefault(endpoint => endpoint.Id == connection.DestinationEndpointId);
			if (source is null || destination is null
				|| !_galaxyLayout.TryGetValue(source.Position.SystemId, out Vector2 start)
				|| !_galaxyLayout.TryGetValue(destination.Position.SystemId, out Vector2 end))
			{
				continue;
			}

			DrawLine(ToGalaxyView(start), ToGalaxyView(end), new Color("375a6d"), 1.5f, true);
		}

		foreach (GameSystemSnapshot system in _snapshot.Systems.OrderBy(system => system.Id.Value))
		{
			if (!_galaxyLayout.TryGetValue(system.Id, out Vector2 position))
			{
				continue;
			}

			Vector2 view = ToGalaxyView(position);
			DrawCircle(view, SystemHitRadius, new Color("153146"));
			DrawArc(view, SystemHitRadius, 0, Mathf.Tau, 32, new Color("70c7e8"), 1.5f, true);
			DrawString(ThemeDB.FallbackFont, view + new Vector2(24, 5), system.Name, fontSize: 13, modulate: new Color("d8e6ef"));
		}
	}

	private void DrawSystem()
	{
		GameSystemSnapshot? system = CurrentSystem();
		if (system is null)
		{
			return;
		}

		DrawSystemAxes();
		DrawString(ThemeDB.FallbackFont, new Vector2(28, 118), $"{system.Name.ToUpperInvariant()} | Galaxy returns to topology | wheel to zoom | middle drag to pan", fontSize: 13, modulate: new Color("668ca5"));
		foreach (ConnectorEndpointSnapshot endpoint in _snapshot!.ConnectorEndpoints.Where(endpoint => endpoint.Position.SystemId == system.Id).OrderBy(endpoint => endpoint.Id.Value))
		{
			Vector2 position = ToSystemView(endpoint.Position.Position);
			DrawRect(new Rect2(position - new Vector2(6, 6), new Vector2(12, 12)), new Color("b77ad9"));
			DrawString(ThemeDB.FallbackFont, position + new Vector2(10, -8), $"C{endpoint.Id.Value}", fontSize: 11, modulate: new Color("d9b9ef"));
		}

		foreach (GameShipSnapshot ship in _snapshot.Ships.Where(ship => ship.Position?.SystemId == system.Id))
		{
			if (ship.Position is not { } systemPosition)
			{
				continue;
			}

			Vector2 position = ToSystemView(systemPosition.Position);
			if (ShouldDrawRoute(ship.CurrentOrder)
				&& ship.CurrentOrder!.Destination is NavigationDestination.Position destination
				&& destination.Value.SystemId == system.Id)
			{
				Vector2 target = ToSystemView(destination.Value.Position);
				DrawLine(position, target, new Color("375a6d"), 1.0f, true);
				DrawCircle(target, 4.0f, new Color("70c7e8"), false, 1.5f, true);
			}

			if (_selectedShipIds.Contains(ship.Id))
			{
				DrawArc(position, 12.0f, 0, Mathf.Tau, 32, new Color("70c7e8"), 2.0f, true);
			}

			DrawCircle(position, 6.0f, new Color("f5c86b"));
			DrawString(ThemeDB.FallbackFont, position + new Vector2(10, 5), $"S{ship.Id.Value}", fontSize: 12, modulate: new Color("d8bb78"));
		}
	}

	private void DrawSystemAxes()
	{
		Vector2 origin = ToSystemView(new SpatialPosition(new SpatialCoordinate(0), new SpatialCoordinate(0)));
		DrawLine(new Vector2(origin.X, 90), new Vector2(origin.X, Size.Y - 100), new Color("153146"), 1.0f);
		DrawLine(new Vector2(30, origin.Y), new Vector2(Size.X - 30, origin.Y), new Color("153146"), 1.0f);
	}

	private void OpenSystemAt(Vector2 pointer)
	{
		GameSystemSnapshot? hit = _snapshot!.Systems.OrderBy(system => system.Id.Value).FirstOrDefault(system => _galaxyLayout.TryGetValue(system.Id, out Vector2 position) && ToGalaxyView(position).DistanceTo(pointer) <= SystemHitRadius);
		if (hit is null)
		{
			return;
		}

		_systemContext = hit.Id;
		_scale = MapScale.System;
		Recenter();
		ViewChanged?.Invoke();
		AcceptEvent();
	}

	private void SelectShipOrRequestMove(InputEventMouseButton mouse)
	{
		GameSystemSnapshot? system = CurrentSystem();
		if (system is null)
		{
			return;
		}

		GameShipSnapshot? hit = _snapshot!.Ships.Where(ship => ship.Position?.SystemId == system.Id).OrderBy(ship => ship.Id.Value).FirstOrDefault(ship => ship.Position is { } position && ToSystemView(position.Position).DistanceTo(mouse.Position) <= ShipHitRadius);
		if (hit is not null)
		{
			if (mouse.ShiftPressed)
			{
				ToggleSelection(hit.Id);
			}
			else
			{
				SelectOnly(hit.Id);
			}

			SelectionChanged?.Invoke();
			QueueRedraw();
			AcceptEvent();
			return;
		}

		if (FocusedShipId is { } focused)
		{
			SpatialPosition destination = SystemMapCoordinateTransform.ScreenToSystemPosition(
				mouse.Position.X,
				mouse.Position.Y,
				Size.X,
				Size.Y,
				_systemCamera.X,
				_systemCamera.Y,
				_systemZoom);
			DestinationRequested?.Invoke(
				new SystemPosition(system.Id, destination),
				mouse.ShiftPressed ? OrderPlacement.Append : OrderPlacement.ReplaceAll);
			AcceptEvent();
		}
	}

	private void PanBy(Vector2 screenDelta)
	{
		if (_scale == MapScale.Galaxy)
		{
			_galaxyCamera -= screenDelta / _galaxyZoom;
		}
		else
		{
			_systemCamera -= screenDelta / _systemZoom;
		}

		QueueRedraw();
	}

	private void ZoomAt(Vector2 pointer, float factor)
	{
		if (_scale == MapScale.Galaxy)
		{
			Vector2 worldBefore = FromGalaxyView(pointer);
			_galaxyZoom = Mathf.Clamp(_galaxyZoom * factor, MinimumZoom, MaximumZoom);
			_galaxyCamera = worldBefore - ((pointer - (Size / 2)) / _galaxyZoom);
		}
		else
		{
			Vector2 worldBefore = FromSystemView(pointer);
			_systemZoom = Mathf.Clamp(_systemZoom * factor, MinimumZoom, MaximumZoom);
			_systemCamera = worldBefore - ((pointer - (Size / 2)) / _systemZoom);
		}

		QueueRedraw();
	}

	private GameSystemSnapshot? CurrentSystem() => _systemContext is { } context
		? _snapshot?.Systems.FirstOrDefault(system => system.Id == context)
		: null;

	private Vector2 ToGalaxyView(Vector2 position) => (Size / 2) + ((position - _galaxyCamera) * _galaxyZoom);

	private Vector2 FromGalaxyView(Vector2 position) => _galaxyCamera + ((position - (Size / 2)) / _galaxyZoom);

	private Vector2 ToSystemView(SpatialPosition position) => (Size / 2) + ((ToVector(position) - _systemCamera) * _systemZoom);

	private Vector2 FromSystemView(Vector2 position) => _systemCamera + ((position - (Size / 2)) / _systemZoom);

	private static Vector2 ToVector(SpatialPosition position) => new((float)position.X.Units, (float)-position.Y.Units);

	private static bool ShouldDrawRoute(ShipOrderSnapshot? order) => order?.Status is ShipOrderStatus.Active or ShipOrderStatus.Waiting;

	private void SelectOnly(ShipId shipId)
	{
		_selectedShipIds.Clear();
		_selectedShipIds.Add(shipId);
		FocusedShipId = shipId;
	}

	private void ToggleSelection(ShipId shipId)
	{
		if (_selectedShipIds.Remove(shipId))
		{
			if (FocusedShipId == shipId)
			{
				FocusedShipId = _selectedShipIds.Count == 0 ? null : _selectedShipIds.Min;
			}

			return;
		}

		_selectedShipIds.Add(shipId);
		FocusedShipId = shipId;
	}

	private enum MapScale
	{
		Galaxy,
		System,
	}
}
