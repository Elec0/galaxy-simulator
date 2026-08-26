using System.Globalization;

namespace GalaxyCommand.GodotClient;

/// <summary>
/// Loads the temporary text-backed running-speed ladder used until the project
/// defines how mod-supplied configuration is discovered and composed.
/// </summary>
internal static class PacingSpeedLadderFile
{
    /// <summary>
    /// Reads one invariant-culture multiplier from each nonblank line and
    /// returns pacing only after the complete ladder passes its accepted
    /// validation contract.
    /// </summary>
    internal static ApplicationPacingController Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string[] lines = File.ReadAllLines(path);
        var multipliers = new List<double>(lines.Length);
        for (int index = 0; index < lines.Length; index++)
        {
            string value = lines[index].Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (!double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double multiplier) ||
                !double.IsFinite(multiplier))
            {
                throw new InvalidDataException(
                    $"Pacing speed ladder '{path}' has an invalid multiplier on line {index + 1}.");
            }

            multipliers.Add(multiplier);
        }

        try
        {
            return new ApplicationPacingController(multipliers);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"Pacing speed ladder '{path}' does not satisfy the required running-speed ladder rules.",
                exception);
        }
    }
}
