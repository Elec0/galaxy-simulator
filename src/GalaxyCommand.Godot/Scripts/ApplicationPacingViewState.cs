namespace GalaxyCommand.GodotClient;

/// <summary>
/// Immutable local pacing data for one application redraw. It contains no
/// authoritative session state and leaves displayed wording to the client UI.
/// </summary>
internal sealed record ApplicationPacingViewState(
    bool IsPaused,
    double SelectedSpeedMultiplier,
    IReadOnlyList<double> RunningSpeedMultipliers)
{
    /// <summary>
    /// Captures the pacing state and full configured ladder together so the UI
    /// can redraw controls without hard-coding a number of speed presets.
    /// </summary>
    internal static ApplicationPacingViewState Create(ApplicationPacingController pacing)
    {
        ArgumentNullException.ThrowIfNull(pacing);
        return new ApplicationPacingViewState(
            pacing.IsPaused,
            pacing.SelectedSpeedMultiplier,
            pacing.RunningSpeedMultipliers);
    }
}
