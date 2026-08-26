using GalaxyCommand.Simulation;
using Godot;
using System.Globalization;
using CryptographicRandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace GalaxyCommand.GodotClient;

public partial class Main : Node
{
	private const int MaximumFactCountPerRefresh = 64;
	private const int MaximumRecentFacts = 32;
	private const string PacingSpeedLadderResourcePath = "res://pacing-speeds.txt";
	private static readonly SystemId InitialSystemId = new(1);
	private static readonly EntityId InitialEntityId = new(1);
	private static readonly ShipId InitialShipId = new(1);
	private static readonly InventoryId InitialCargoInventoryId = new(1);
	private static readonly PrincipalId InitialPrincipalId = new(1);
	private static readonly ShipDesign InitialShipDesign = new(
		new ConstructionDesignId(1),
		"Starter Ship",
		new ConstructionRecipe([], new Work(1)),
		new Quantity(100));

	private readonly CommandSource _player = new(
		CommandSourceKind.Player,
		new CommandSourceId("local-player"));
	private GameSession _session = null!;
	private GalaxyMap _map = null!;
	private Label _status = null!;
	private Label _pacingState = null!;
	private Button _pauseOrResume = null!;
	private HBoxContainer _pacingPresets = null!;
	private readonly List<GameFactEnvelope> _recentFacts = [];
	private ApplicationPacingController _pacing = null!;
	private readonly ApplicationInputBuffer _input = new();
	private GameFactSequence? _factCursor;
	private bool _factHistoryTruncated;
	private GamePresentationSnapshot _presentation = null!;
	private string _lastCommandStatus = "Select a ship";

	public override void _Ready()
	{
		_pacing = PacingSpeedLadderFile.Load(
			ProjectSettings.GlobalizePath(PacingSpeedLadderResourcePath));
		_session = CreateSession();
		_map = GetNode<GalaxyMap>("GalaxyMap");
		_status = GetNode<Label>("Interface/StatusPanel/Margin/Content/Status");
		_pacingState = GetNode<Label>(
			"Interface/StatusPanel/Margin/Content/PacingControls/State");
		_pauseOrResume = GetNode<Button>(
			"Interface/StatusPanel/Margin/Content/PacingControls/PauseOrResume");
		_pacingPresets = GetNode<HBoxContainer>(
			"Interface/StatusPanel/Margin/Content/PacingControls/Presets");
		ConfigurePacingControls();
		_map.SelectionChanged += OnSelectionChanged;
		_map.DestinationRequested += OnDestinationRequested;
		_map.CancelRequested += OnCancelRequested;
		AdvanceTo(SimulationTime.Zero);
		GD.Print("Galaxy Command clean spatial session ready.");
	}

	public override void _Process(double delta)
	{
		// The prior frame ended at a completed timestamp, so drained gameplay
		// commands share one authoritative admission time and pacing stays local.
		DrainBufferedInput();
		SimulationTime target = _pacing.Advance(
			_session.CurrentTime,
			TimeSpan.FromSeconds(delta));
		AdvanceTo(target);
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
					InitialEntityId,
					InitialShipId,
					InitialCargoInventoryId,
					InitialPrincipalId,
					InitialShipDesign,
					position,
					new ActorController(ActorControllerKind.Player, new CommandSourceId("local-player"))),
			],
			new RelationshipSetup(
				[
					new PrincipalDefinition(
						InitialPrincipalId,
						new PrincipalContentId("player"),
						"Player"),
				],
				InitialPrincipalId,
				new StandingPolicy(
					new StandingPolicyId("default-standing"),
					new StandingValue(-100),
					new StandingValue(100),
					new StandingValue(0),
					new StandingValue(-50),
					new StandingValue(0),
					new StandingValue(50),
					new StandingValue(90)),
				[]),
			// Resolve entropy in the application shell before constructing authority.
			RandomRootSeed.FromBytes(
				CryptographicRandomNumberGenerator.GetBytes(RandomRootSeed.ByteCount)),
			factRetentionCapacity: 1024);
		return new GameSession(
			setup,
			new DirectLocalNavigationPlanner(
				new ChebyshevLocalTravelTimeEstimator(millisecondsPerMapUnit: 10)));
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

		_input.EnqueueGameplay(
			_player,
			new MoveShipCommand(
				shipId,
				new NavigationDestination.Position(destination),
				placement));
		_lastCommandStatus = "Move order queued";
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

		_input.EnqueueGameplay(
			_player,
			new CancelShipOrderCommand(shipId, current.Id));
		_lastCommandStatus = "Cancel order queued";
		RefreshPresentation();
	}

	/// <summary>
	/// Applies every captured local pacing action and admits every captured
	/// gameplay command at the completed boundary before advancement resumes.
	/// </summary>
	private void DrainBufferedInput()
	{
		foreach (BufferedApplicationInput buffered in _input.Drain())
		{
			switch (buffered)
			{
				case BufferedApplicationInput.Gameplay gameplay:
					GameplayCommandRecord record = _session.SubmitCommand(
						gameplay.Source,
						gameplay.Command);
					_lastCommandStatus = DescribeResult(record);
					break;
				case BufferedApplicationInput.Pacing pacing:
					pacing.Action.Apply(_pacing);
					break;
				default:
					throw new InvalidOperationException(
						$"Unsupported application input {buffered.GetType().Name}.");
			}
		}
	}

	/// <summary>
	/// Connects the local pacing controls and creates one preset button for each
	/// multiplier supplied by the validated running-speed ladder.
	/// </summary>
	private void ConfigurePacingControls()
	{
		_pauseOrResume.Pressed += () => _input.EnqueuePacing(
			_pacing.IsPaused
				? new ApplicationPacingAction.Unpause()
				: new ApplicationPacingAction.Pause());
		GetNode<Button>("Interface/StatusPanel/Margin/Content/PacingControls/Slower")
			.Pressed += () => _input.EnqueuePacing(
				new ApplicationPacingAction.DecreaseSpeed());
		GetNode<Button>("Interface/StatusPanel/Margin/Content/PacingControls/Faster")
			.Pressed += () => _input.EnqueuePacing(
				new ApplicationPacingAction.IncreaseSpeed());

		foreach (double multiplier in _pacing.RunningSpeedMultipliers)
		{
			double capturedMultiplier = multiplier;
			var button = new Button
			{
				Text = $"{FormatPacingMultiplier(capturedMultiplier)}x",
			};
			button.Pressed += () => _input.EnqueuePacing(
				new ApplicationPacingAction.SelectSpeed(capturedMultiplier));
			_pacingPresets.AddChild(button);
		}
	}

	/// <summary>
	/// Updates only client-owned pacing presentation after a completed boundary
	/// has applied every captured input action.
	/// </summary>
	private void RefreshPacingControls()
	{
		ApplicationPacingViewState pacing = ApplicationPacingViewState.Create(_pacing);
		string multiplier = FormatPacingMultiplier(pacing.SelectedSpeedMultiplier);
		_pacingState.Text = pacing.IsPaused
			? $"PACE PAUSED | SELECTED {multiplier}x"
			: $"PACE {multiplier}x";
		_pauseOrResume.Text = pacing.IsPaused ? "Resume" : "Pause";
	}

	/// <summary>
	/// Formats a validated speed multiplier without making locale-dependent text
	/// part of the pacing state or input-routing contract.
	/// </summary>
	private static string FormatPacingMultiplier(double multiplier)
	{
		return multiplier.ToString(CultureInfo.InvariantCulture);
	}

	private void AdvanceTo(SimulationTime target)
	{
		_session.AdvanceTo(target);
		RefreshPresentation();
	}

	private void RefreshPresentation()
	{
		RefreshPacingControls();
		_presentation = _session.CapturePresentation(
			new GamePresentationRequest(
				InitialPrincipalId,
				_map.SelectedShipIds,
				_map.FocusedShipId,
				_factCursor,
				MaximumFactCountPerRefresh));
		_map.Display(_presentation);
		ConsumeFacts(_presentation.Facts);
		_factCursor = _presentation.NextFactCursor;

		GamePresentationWorldSnapshot snapshot = _presentation.World;
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

}
