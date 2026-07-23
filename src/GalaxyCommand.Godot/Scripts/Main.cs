using GalaxyCommand.Simulation;
using Godot;

namespace GalaxyCommand.GodotClient;

public partial class Main : Node
{
	private const double SimulationMillisecondsPerRealSecond = 30_000;

	private readonly PhaseOneScenario _scenario = new();
	private GalaxyMap _map = null!;
	private Label _status = null!;
	private double _targetMilliseconds;
	private bool _running = true;

	public override void _Ready()
	{
		_map = GetNode<GalaxyMap>("GalaxyMap");
		_status = GetNode<Label>("Interface/StatusPanel/Margin/Status");
		AdvanceTo(SimulationTime.Zero);
		GD.Print("Galaxy Command graphics foundation ready; live simulation snapshot connected.");
	}

	public override void _Process(double delta)
	{
		if (!_running)
		{
			return;
		}

		_targetMilliseconds += delta * SimulationMillisecondsPerRealSecond;
		AdvanceTo(new SimulationTime((ulong)_targetMilliseconds));
	}

	private void AdvanceTo(SimulationTime target)
	{
		PhaseOneReport report = _scenario.RunUntilFirstShip(target);
		PhaseOneSnapshot snapshot = _scenario.CaptureSnapshot();
		_map.Display(snapshot);

		string state = report.ConstructedShipId is null ? "RUNNING 30×" : "SHIP CONSTRUCTED";
		_status.Text =
			$"{state}   |   SIM {FormatTime(snapshot.Time)}   |   " +
			$"SHIPS {snapshot.Ships.Count}   |   EVENTS {_scenario.EventRecords.Count}";

		if (report.ConstructedShipId is not null)
		{
			_running = false;
			GD.Print(
				$"Simulation complete; final digest={report.FinalStateDigest:x16}");
		}
	}

	private static string FormatTime(SimulationTime time)
	{
		TimeSpan elapsed = TimeSpan.FromMilliseconds(time.Milliseconds);
		return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}";
	}
}
