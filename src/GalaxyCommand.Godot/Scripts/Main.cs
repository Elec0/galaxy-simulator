using GalaxyCommand.Simulation;
using Godot;

namespace GalaxyCommand.GodotClient;

public partial class Main : Node
{
	private const double SimulationMillisecondsPerRealSecond = 1_000;
	private const int MaximumFactCountPerRefresh = 64;
	private const int MaximumRecentFacts = 32;
	private static readonly SystemId InitialSystemId = new(1);
	private static readonly ShipId InitialShipId = new(1);

	private readonly CommandSource _player = new(
		CommandSourceKind.Player,
		new CommandSourceId("local-player"));
	private GameSession _session = null!;
	private GalaxyMap _map = null!;
	private Label _status = null!;
	private readonly List<GameFactEnvelope> _recentFacts = [];
	private double _targetMilliseconds;
	private GameFactSequence? _factCursor;
	private bool _factHistoryTruncated;
	private GamePresentationSnapshot _presentation = null!;
	private string _lastCommandStatus = "Select a ship";

	public override void _Ready()
	{
		_session = CreateSession();
		_map = GetNode<GalaxyMap>("GalaxyMap");
		_status = GetNode<Label>("Interface/StatusPanel/Margin/Status");
		_map.SelectionChanged += OnSelectionChanged;
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
			],
			factRetentionCapacity: 1024);
		return new GameSession(
			setup,
			new DirectLocalNavigationPlanner(new MapTravelTimeEstimator()));
	}

	private void OnSelectionChanged()
	{
		_lastCommandStatus = _map.FocusedShipId is { } focused
			? $"Focused ship {focused}"
			: "Select a ship";
		RefreshPresentation();
	}

	private void OnDestinationRequested(
		SystemPosition destination,
		OrderPlacement placement)
	{
		if (_map.FocusedShipId is not { } shipId)
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
		ShipOrderSnapshot? current = _presentation.Selection.FocusedShip is { } focused
			&& focused.Id == shipId
				? focused.CurrentOrder
				: null;
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
		_presentation = _session.CapturePresentation(
			new GamePresentationRequest(
				_map.SelectedShipIds,
				_map.FocusedShipId,
				_factCursor,
				MaximumFactCountPerRefresh));
		_map.Display(_presentation);
		ConsumeFacts(_presentation.Facts);

		GameSnapshot snapshot = _presentation.World;
		GameShipSnapshot? selected = _presentation.Selection.FocusedShip;
		string order = selected?.CurrentOrder is { } current
			? $"{current.Status} / {current.Reason} / {DescribeDestination(current.Destination)}"
			: "No current order";
		string motion = selected?.Motion is { } activeMotion
			? $"moving until {FormatTime(activeMotion.ArrivesAt)}"
			: selected?.Transit is { } transit
				? $"in transit via C{transit.ConnectionId.Value} until {FormatTime(transit.ArrivesAt)}"
			: "stationary";
		string control = selected is null
			? "No controller"
			: $"{selected.Control.ActiveController.Kind}:{selected.Control.ActiveController.Id}";
		string queue = selected is null
			? "QUEUE 0"
			: $"QUEUE {selected.QueuedOrders.Count}";
		string facts = _factHistoryTruncated
			? "FACT HISTORY INCOMPLETE"
			: $"FACTS {_recentFacts.Count}";

		_status.Text =
			$"SIM {FormatTime(snapshot.Time)}   |   SHIPS {snapshot.Ships.Count}   |   " +
			$"{control}   |   {queue}   |   {_lastCommandStatus}   |   " +
			$"{order}   |   {motion}   |   {facts}";
	}

	private void ConsumeFacts(GameFactReadResult facts)
	{
		if (facts.CursorGap)
		{
			_recentFacts.Clear();
			_factHistoryTruncated = true;
		}

		foreach (GameFactEnvelope fact in facts.Facts)
		{
			_recentFacts.Add(fact);
			_factCursor = fact.Sequence;
		}

		if (_recentFacts.Count > MaximumRecentFacts)
		{
			_recentFacts.RemoveRange(0, _recentFacts.Count - MaximumRecentFacts);
		}
	}

	private static string DescribeResult(GameplayCommandRecord record) =>
		record.Result.Status == CommandResultStatus.Accepted
			? $"{record.Envelope.Command.Kind} accepted"
			: $"{record.Result.RejectionCode}: {record.Result.Reason}";

	private static string DescribeDestination(NavigationDestination destination) =>
		destination switch
		{
			NavigationDestination.Position position =>
				$"({position.Value.Position.X}, {position.Value.Position.Y})",
			NavigationDestination.System system =>
				$"system {system.SystemId}",
			_ => destination.GetType().Name,
		};

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
