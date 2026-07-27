using GalaxyCommand.Simulation;
using Godot;

namespace GalaxyCommand.GodotClient;

public partial class Main : Node
{
	private const double SimulationMillisecondsPerRealSecond = 1_000;
	private static readonly SystemId InitialSystemId = new(1);
	private static readonly ShipId InitialShipId = new(1);

	private readonly CommandSource _player = new(
		CommandSourceKind.Player,
		new CommandSourceId("local-player"));
	private GameSession _session = null!;
	private GalaxyMap _map = null!;
	private Label _status = null!;
	private double _targetMilliseconds;
	private string _lastCommandStatus = "Select a ship";

	public override void _Ready()
	{
		_session = CreateSession();
		_map = GetNode<GalaxyMap>("GalaxyMap");
		_status = GetNode<Label>("Interface/StatusPanel/Margin/Status");
		_map.ShipSelected += OnShipSelected;
		_map.DestinationRequested += OnDestinationRequested;
		_map.CancelRequested += OnCancelRequested;
		AdvanceTo(SimulationTime.Zero);
		GD.Print("Galaxy Command clean spatial session ready.");
	}

	public override void _Process(double delta)
	{
		_targetMilliseconds += delta * SimulationMillisecondsPerRealSecond;
		AdvanceTo(new SimulationTime((ulong)_targetMilliseconds));
	}

	private static GameSession CreateSession()
	{
		var position = new SystemPosition(
			InitialSystemId,
			new SpatialPosition(
				new SpatialCoordinate(0),
				new SpatialCoordinate(0)));
		var setup = new GameSessionSetup(
			[new StarSystem(InitialSystemId, "Initial System")],
			[
				new InitialShipSetup(
					InitialShipId,
					position,
					new ActorController(ActorControllerKind.Player, new CommandSourceId("local-player"))),
			]);
		return new GameSession(
			setup,
			new DirectLocalNavigationPlanner(new MapTravelTimeEstimator()));
	}

	private void OnShipSelected(ShipId shipId)
	{
		_lastCommandStatus = $"Selected ship {shipId}";
		RefreshPresentation();
	}

	private void OnDestinationRequested(
		SystemPosition destination,
		OrderPlacement placement)
	{
		if (_map.SelectedShipId is not { } shipId)
		{
			return;
		}

		GameplayCommandRecord record = _session.SubmitCommand(
			_player,
			new MoveShipCommand(
				shipId,
				new NavigationDestination.Position(destination),
				placement));
		_lastCommandStatus = DescribeResult(record);
		RefreshPresentation();
	}

	private void OnCancelRequested(ShipId shipId)
	{
		ShipOrderSnapshot? current = _session.CaptureSnapshot().Ships
			.SingleOrDefault(ship => ship.Id == shipId)
			?.CurrentOrder;
		if (current is null
			|| current.Status is ShipOrderStatus.Completed
				or ShipOrderStatus.Cancelled
				or ShipOrderStatus.Failed)
		{
			_lastCommandStatus = "No active order to cancel";
			RefreshPresentation();
			return;
		}

		GameplayCommandRecord record = _session.SubmitCommand(
			_player,
			new CancelShipOrderCommand(shipId, current.Id));
		_lastCommandStatus = DescribeResult(record);
		RefreshPresentation();
	}

	private void AdvanceTo(SimulationTime target)
	{
		_session.AdvanceTo(target);
		RefreshPresentation();
	}

	private void RefreshPresentation()
	{
		GameSnapshot snapshot = _session.CaptureSnapshot();
		_map.Display(snapshot);

		GameShipSnapshot? selected = _map.SelectedShipId is { } selectedId
			? snapshot.Ships.SingleOrDefault(ship => ship.Id == selectedId)
			: null;
		string order = selected?.CurrentOrder is { } current
			? $"{current.Status} / {current.Reason} / {DescribeDestination(current.Destination)}"
			: "No current order";
		string motion = selected?.Motion is { } activeMotion
			? $"moving until {FormatTime(activeMotion.ArrivesAt)}"
			: "stationary";
		string control = selected is null
			? "No controller"
			: $"{selected.Control.ActiveController.Kind}:{selected.Control.ActiveController.Id}";
		string queue = selected is null
			? "QUEUE 0"
			: $"QUEUE {selected.QueuedOrders.Count}";

		_status.Text =
			$"SIM {FormatTime(snapshot.Time)}   |   SHIPS {snapshot.Ships.Count}   |   " +
			$"{control}   |   {queue}   |   {_lastCommandStatus}   |   " +
			$"{order}   |   {motion}";
	}

	private static string DescribeResult(GameplayCommandRecord record) =>
		record.Result.Status == CommandResultStatus.Accepted
			? $"{record.Envelope.Command.Kind} accepted"
			: $"{record.Result.RejectionCode}: {record.Result.Reason}";

	private static string DescribeDestination(NavigationDestination destination) =>
		destination is NavigationDestination.Position position
			? $"({position.Value.Position.X}, {position.Value.Position.Y})"
			: destination.GetType().Name;

	private static string FormatTime(SimulationTime time)
	{
		TimeSpan elapsed = TimeSpan.FromMilliseconds(time.Milliseconds);
		return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}";
	}

	private sealed class MapTravelTimeEstimator : ILocalTravelTimeEstimator
	{
		private const ulong MillisecondsPerMapUnit = 10;

		public SimulationDuration Estimate(
			ShipId actorId,
			SystemPosition origin,
			SystemPosition destination)
		{
			ulong horizontal = Distance(
				origin.Position.X.Units,
				destination.Position.X.Units);
			ulong vertical = Distance(
				origin.Position.Y.Units,
				destination.Position.Y.Units);
			ulong distance = Math.Max(horizontal, vertical);
			return new SimulationDuration(
				checked(distance * MillisecondsPerMapUnit));
		}

		private static ulong Distance(long first, long second)
		{
			Int128 difference = (Int128)first - second;
			UInt128 magnitude = difference < 0
				? (UInt128)(-difference)
				: (UInt128)difference;
			return checked((ulong)magnitude);
		}
	}
}
