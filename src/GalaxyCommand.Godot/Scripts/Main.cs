using GalaxyCommand.Simulation;
using Godot;

namespace GalaxyCommand.GodotClient;

public partial class Main : Node
{
    public override void _Ready()
    {
        var scenario = new PhaseOneScenario();
        PhaseOneReport report = scenario.RunUntilFirstShip(new SimulationTime(1_000_000));

        Label status = GetNode<Label>("Interface/StatusPanel/Margin/Status");
        status.Text =
            $"SIM {FormatTime(report.EndTime)}   |   " +
            $"SHIPS {report.StartingShipCount} → {report.EndingShipCount}   |   " +
            $"EVENTS {report.EventsProcessed}   |   " +
            $"STATE {report.FinalStateDigest:x16}";

        GD.Print(
            $"Galaxy Command graphics foundation ready; " +
            $"simulation digest={report.FinalStateDigest:x16}");
    }

    private static string FormatTime(SimulationTime time)
    {
        TimeSpan elapsed = TimeSpan.FromMilliseconds(time.Milliseconds);
        return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}";
    }
}
