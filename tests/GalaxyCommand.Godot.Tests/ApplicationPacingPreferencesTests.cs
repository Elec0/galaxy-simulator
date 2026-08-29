using GalaxyCommand.GodotClient;

namespace GalaxyCommand.Godot.Tests;

public sealed class ApplicationPacingPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"galaxy-command-pacing-preferences-{Guid.NewGuid():N}");

    [Fact]
    public void LoadUsesStoredDialoguePreferenceAndSafelyFallsBackForAnUnavailableCap()
    {
        var store = new DeviceLocalPreferenceStore(_directory);
        store.SavePacing(new PacingPreferenceSnapshot(
            PauseWhenResponseRequiredDialogueOpens: false,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
            {
                [ApplicationPacingEventCategories.PlayerAssetOffscreenCombatStarted] =
                    new ApplicationEventPacingAction.Cap(10d),
            }));
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);

        ApplicationPacingPreferenceState state = ApplicationPacingPreferences.Load(
            store,
            pacing);

        Assert.False(state.PauseWhenResponseRequiredDialogueOpens);
        Assert.IsType<ApplicationEventPacingAction.Pause>(
            state.EventPacing.Policies[
                ApplicationPacingEventCategories.PlayerAssetOffscreenCombatStarted]);
        Assert.Equal(10d, Assert.Single(state.EventPacing.Warnings).UnavailableMultiplier);
        Assert.Null(state.StoreLoadFailure);
    }

    [Fact]
    public void DescribeConfigurationWarningReportsAnUnavailableStoredCap()
    {
        var store = new DeviceLocalPreferenceStore(_directory);
        store.SavePacing(new PacingPreferenceSnapshot(
            PauseWhenResponseRequiredDialogueOpens: true,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
            {
                [ApplicationPacingEventCategories.PlayerAssetOffscreenCombatStarted] =
                    new ApplicationEventPacingAction.Cap(10d),
            }));
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        ApplicationPacingPreferenceState state = ApplicationPacingPreferences.Load(store, pacing);

        string warning = ApplicationPacingPreferences.DescribeConfigurationWarning(state);

        Assert.Equal("PACING CAP FALLBACK: 1 UNAVAILABLE", warning);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
