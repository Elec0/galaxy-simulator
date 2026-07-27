using GalaxyCommand.Simulation;
using Godot;

namespace GalaxyCommand.GodotClient;

public partial class GalaxyMap : Control
{
	private const float ShipHitRadius = 14.0f;
	private GameSnapshot? _snapshot;

	public event Action<ShipId>? ShipSelected;
	public event Action<SystemPosition, OrderPlacement>? DestinationRequested;
	public event Action<ShipId>? CancelRequested;

	public ShipId? SelectedShipId { get; private set; }

	public override void _Ready()
	{
		Resized += QueueRedraw;
	}

	public void Display(GameSnapshot snapshot)
	{
		_snapshot = snapshot;
		if (SelectedShipId is { } selected
			&& snapshot.Ships.All(ship => ship.Id != selected))
		{
			SelectedShipId = null;
		}

		QueueRedraw();
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (_snapshot is null
			|| @event is not InputEventMouseButton mouse
			|| !mouse.Pressed)
		{
			return;
		}

		if (mouse.ButtonIndex == MouseButton.Right)
		{
			if (SelectedShipId is { } selected)
			{
				CancelRequested?.Invoke(selected);
				AcceptEvent();
			}

			return;
		}

		if (mouse.ButtonIndex != MouseButton.Left)
		{
			return;
		}

		GameShipSnapshot? hit = _snapshot.Ships
			.OrderBy(ship => ship.Id.Value)
			.FirstOrDefault(ship =>
				ToView(ship.Position).DistanceTo(mouse.Position) <= ShipHitRadius);
		if (hit is not null)
		{
			SelectedShipId = hit.Id;
			ShipSelected?.Invoke(hit.Id);
			QueueRedraw();
			AcceptEvent();
			return;
		}

		if (SelectedShipId is not null
			&& _snapshot.Systems.Count == 1)
		{
			OrderPlacement placement = mouse.ShiftPressed
				? OrderPlacement.Append
				: OrderPlacement.ReplaceAll;
			DestinationRequested?.Invoke(
				ToSystemPosition(_snapshot.Systems[0].Id, mouse.Position),
				placement);
			AcceptEvent();
		}
	}

	public override void _Draw()
	{
		if (_snapshot is null)
		{
			return;
		}

		DrawRect(
			new Rect2(Vector2.Zero, Size),
			new Color("071521"));
		DrawLine(
			new Vector2(Size.X / 2, 90),
			new Vector2(Size.X / 2, Size.Y - 100),
			new Color("153146"),
			1.0f);
		DrawLine(
			new Vector2(30, Size.Y / 2),
			new Vector2(Size.X - 30, Size.Y / 2),
			new Color("153146"),
			1.0f);

		if (_snapshot.Systems.Count == 1)
		{
			DrawString(
				ThemeDB.FallbackFont,
				new Vector2(28, 118),
				_snapshot.Systems[0].Name.ToUpperInvariant(),
				fontSize: 13,
				modulate: new Color("668ca5"));
		}

		foreach (GameShipSnapshot ship in _snapshot.Ships)
		{
			Vector2 position = ToView(ship.Position);
			if (ShouldDrawRoute(ship.CurrentOrder)
				&& ship.CurrentOrder!.Destination is NavigationDestination.Position destination)
			{
				Vector2 target = ToView(destination.Value);
				DrawLine(position, target, new Color("375a6d"), 1.0f, true);
				DrawCircle(target, 4.0f, new Color("70c7e8"), false, 1.5f, true);
			}

			bool selected = ship.Id == SelectedShipId;
			if (selected)
			{
				DrawArc(
					position,
					12.0f,
					0,
					Mathf.Tau,
					32,
					new Color("70c7e8"),
					2.0f,
					true);
			}

			DrawCircle(position, 6.0f, new Color("f5c86b"));
			DrawString(
				ThemeDB.FallbackFont,
				position + new Vector2(10, 5),
				$"S{ship.Id.Value}",
				fontSize: 12,
				modulate: new Color("d8bb78"));
		}
	}

	private static bool ShouldDrawRoute(ShipOrderSnapshot? order) =>
		order?.Status is ShipOrderStatus.Active or ShipOrderStatus.Waiting;

	private Vector2 ToView(SystemPosition position) =>
		new(
			(float)(Size.X / 2 + position.Position.X.Units),
			(float)(Size.Y / 2 - position.Position.Y.Units));

	private SystemPosition ToSystemPosition(SystemId systemId, Vector2 position) =>
		new(
			systemId,
			new SpatialPosition(
				new SpatialCoordinate((long)Math.Round(position.X - (Size.X / 2))),
				new SpatialCoordinate((long)Math.Round((Size.Y / 2) - position.Y))));
}
