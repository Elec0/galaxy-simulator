using GalaxyCommand.GodotClient;

namespace GalaxyCommand.Godot.Tests;

public sealed class PacingSpeedLadderFileTests
{
    [Fact]
    public void LoadReadsOneInvariantMultiplierFromEachNonblankLine()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"galaxy-command-pacing-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "1\n\n2\n5\n10\n30\n");

            ApplicationPacingController pacing = PacingSpeedLadderFile.Load(path);

            Assert.Equal([1d, 2d, 5d, 10d, 30d], pacing.RunningSpeedMultipliers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadReportsTheLineThatContainsANonfiniteMultiplier()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"galaxy-command-pacing-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "1\nNaN\n");

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                PacingSpeedLadderFile.Load(path));

            Assert.Contains("line 2", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
