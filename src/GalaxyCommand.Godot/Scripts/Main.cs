using GalaxyCommand.Simulation;
using Godot;

namespace GalaxyCommand.GodotClient;

public partial class Main : Node
{
	private const double SimulationMillisecondsPerRealSecond = 30_000;

	private readonly GameSession _session = new();
	private GalaxyMap _map = null!;
	private Label _status = null!;
	private double _targetMilliseconds;

	public override void _Ready()
	{
		_map = GetNode<GalaxyMap>("GalaxyMap");
		_status = GetNode<Label>("Interface/StatusPanel/Margin/Status");
		AdvanceTo(SimulationTime.Zero);
		GD.Print("Galaxy Command graphics foundation ready; live simulation snapshot connected.");
	}

	public override void _Process(double delta)
	{
		_targetMilliseconds += delta * SimulationMillisecondsPerRealSecond;
		AdvanceTo(new SimulationTime((ulong)_targetMilliseconds));
	}

	private void AdvanceTo(SimulationTime target)
	{
		_session.AdvanceTo(target);
		PhaseOneSnapshot snapshot = _session.CaptureSnapshot();
		_map.Display(snapshot);

		_status.Text =
			$"RUNNING 30×   |   SIM {FormatTime(snapshot.Time)}   |   " +
			$"SHIPS {snapshot.Ships.Count}   |   EVENTS {_session.EventRecords.Count}";
	}

	private static string FormatTime(SimulationTime time)
	{
		TimeSpan elapsed = TimeSpan.FromMilliseconds(time.Milliseconds);
		return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}";
	}
}
