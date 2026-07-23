using GalaxyCommand.Simulation;
using Godot;

namespace GalaxyCommand.GodotClient;

public partial class GalaxyMap : Control
{
	private PhaseOneSnapshot? _snapshot;

	public override void _Ready()
	{
		Resized += QueueRedraw;
	}

	public void Display(PhaseOneSnapshot snapshot)
	{
		_snapshot = snapshot;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_snapshot is null)
		{
			return;
		}

		Dictionary<LocationId, Vector2> locations = LayoutLocations(_snapshot.Locations);
		foreach (RouteSnapshot route in _snapshot.Routes)
		{
			Color color = route.IsEnabled ? new Color("294864") : new Color("603845");
			DrawLine(locations[route.Origin], locations[route.Destination], color, 2.0f, true);
		}

		foreach (LocationSnapshot location in _snapshot.Locations)
		{
			Vector2 position = locations[location.Id];
			DrawCircle(position, 19.0f, new Color("102c45"));
			DrawArc(position, 19.0f, 0, Mathf.Tau, 48, new Color("70c7e8"), 2.0f, true);
			DrawString(
				ThemeDB.FallbackFont,
				position + new Vector2(-42, 43),
				location.Name.ToUpperInvariant(),
				HorizontalAlignment.Center,
				84,
				13,
				new Color("8aaac0"));
		}

		foreach (ShipSnapshot ship in _snapshot.Ships)
		{
			Vector2 position = ShipPosition(ship, locations, _snapshot);
			DrawCircle(position, 6.0f, new Color("f5c86b"));
			DrawString(
				ThemeDB.FallbackFont,
				position + new Vector2(10, 5),
				$"S{ship.Id.Value}",
				fontSize: 12,
				modulate: new Color("d8bb78"));
		}
	}

	private Dictionary<LocationId, Vector2> LayoutLocations(
		IReadOnlyList<LocationSnapshot> snapshots)
	{
		var result = new Dictionary<LocationId, Vector2>();
		for (int index = 0; index < snapshots.Count; index++)
		{
			float horizontal = (index + 1.0f) / (snapshots.Count + 1.0f);
			float vertical = index % 2 == 0 ? 0.48f : 0.34f;
			result.Add(snapshots[index].Id, new Vector2(horizontal, vertical) * Size);
		}

		return result;
	}

	private static Vector2 ShipPosition(
		ShipSnapshot ship,
		Dictionary<LocationId, Vector2> locations,
		PhaseOneSnapshot snapshot)
	{
		Vector2 origin = locations[ship.Location];
		if (ship.CurrentRoute is not { } routeId
			|| ship.DepartedAt is not { } departedAt
			|| ship.ArrivesAt is not { } arrivesAt)
		{
			return origin + ShipOffset(ship.Id);
		}

		RouteSnapshot route = snapshot.Routes.Single(candidate => candidate.Id == routeId);
		ulong duration = arrivesAt.Milliseconds - departedAt.Milliseconds;
		double elapsed = snapshot.Time.Milliseconds - departedAt.Milliseconds;
		float progress = duration == 0
			? 1.0f
			: Mathf.Clamp((float)(elapsed / duration), 0.0f, 1.0f);
		return locations[route.Origin].Lerp(locations[route.Destination], progress)
			+ ShipOffset(ship.Id);
	}

	private static Vector2 ShipOffset(ShipId shipId)
	{
		int slot = (int)((shipId.Value - 1) % 3);
		return new Vector2(0, 12 + (slot * 12));
	}
}
