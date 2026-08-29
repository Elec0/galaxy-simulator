using GalaxyCommand.GodotClient;
using System.Text.Json;

namespace GalaxyCommand.Godot.Tests;

public sealed class DeviceLocalPreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"galaxy-command-preferences-{Guid.NewGuid():N}");

    [Fact]
    public void MissingStoreReturnsTheAcceptedDefaultPacingPreferences()
    {
        var store = new DeviceLocalPreferenceStore(_directory);

        DeviceLocalPreferenceSnapshot preferences = store.Load();

        Assert.True(preferences.Pacing.PauseWhenResponseRequiredDialogueOpens);
        Assert.Empty(preferences.Pacing.EventPacingOverrides);
        Assert.False(File.Exists(Path.Combine(_directory, "preferences.json")));
    }

    [Fact]
    public void PacingSnapshotDoesNotRetainACallersMutableOverrideDictionary()
    {
        var overrides = new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
        {
            [ApplicationPacingEventCategories.InformationalDialogueForegrounded] =
                new ApplicationEventPacingAction.Ignore(),
        };

        var snapshot = new PacingPreferenceSnapshot(
            PauseWhenResponseRequiredDialogueOpens: true,
            overrides);
        overrides.Clear();

        Assert.Single(snapshot.EventPacingOverrides);
    }

    [Fact]
    public void SavePacingPersistsOnlyDeviceLocalPacingChoices()
    {
        var store = new DeviceLocalPreferenceStore(_directory);
        var expected = new PacingPreferenceSnapshot(
            PauseWhenResponseRequiredDialogueOpens: false,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
            {
                [ApplicationPacingEventCategories.InformationalDialogueForegrounded] =
                    new ApplicationEventPacingAction.Cap(2d),
                [ApplicationPacingEventCategories.PlayerAssetOffscreenCombatStarted] =
                    new ApplicationEventPacingAction.Ignore(),
            });

        store.SavePacing(expected);

        DeviceLocalPreferenceSnapshot actual = store.Load();
        Assert.False(actual.Pacing.PauseWhenResponseRequiredDialogueOpens);
        Assert.Equal(expected.EventPacingOverrides, actual.Pacing.EventPacingOverrides);
    }

    [Fact]
    public void InvalidStoreRemainsUntouchedWhileLoadUsesDefaults()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "preferences.json");
        const string invalidDocument = "not valid JSON";
        File.WriteAllText(path, invalidDocument);
        var store = new DeviceLocalPreferenceStore(_directory);

        DeviceLocalPreferenceSnapshot preferences = store.Load();

        Assert.True(preferences.Pacing.PauseWhenResponseRequiredDialogueOpens);
        Assert.Empty(preferences.Pacing.EventPacingOverrides);
        Assert.Equal(invalidDocument, File.ReadAllText(path));
        Assert.NotNull(store.LastLoadFailure);
    }

    [Fact]
    public void ResetAllExplicitlyReplacesAnInvalidStoreWithDefaults()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path, "not valid JSON");
        var store = new DeviceLocalPreferenceStore(_directory);

        store.ResetAll();

        DeviceLocalPreferenceSnapshot preferences = store.Load();
        Assert.True(preferences.Pacing.PauseWhenResponseRequiredDialogueOpens);
        Assert.Empty(preferences.Pacing.EventPacingOverrides);
        Assert.NotEqual("not valid JSON", File.ReadAllText(path));
    }

    [Fact]
    public void SavePacingDoesNotReplaceAnInvalidStoreWithoutReset()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "preferences.json");
        const string invalidDocument = "not valid JSON";
        File.WriteAllText(path, invalidDocument);
        var store = new DeviceLocalPreferenceStore(_directory);

        Assert.Throws<InvalidOperationException>(() => store.SavePacing(
            new PacingPreferenceSnapshot(
                PauseWhenResponseRequiredDialogueOpens: false,
                new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>())));

        Assert.Equal(invalidDocument, File.ReadAllText(path));
    }

    [Fact]
    public void SavePacingPreservesAnOpaquePresentationCategory()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path, """
            {
              "format": "galaxy-command-device-preferences",
              "schemaVersion": 1,
              "pacing": {
                "pauseWhenResponseRequiredDialogueOpens": true,
                "eventPacingOverrides": []
              },
              "presentation": {
                "activeOverlay": "sensors"
              }
            }
            """);
        var store = new DeviceLocalPreferenceStore(_directory);

        store.SavePacing(new PacingPreferenceSnapshot(
            PauseWhenResponseRequiredDialogueOpens: false,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>()));

        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(
            "sensors",
            saved.RootElement
                .GetProperty("presentation")
                .GetProperty("activeOverlay")
                .GetString());
    }

    [Fact]
    public void StructurallyIncompleteStoreRemainsUntouchedWhileLoadUsesDefaults()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "preferences.json");
        const string incompleteDocument = """
            {
              "format": "galaxy-command-device-preferences",
              "schemaVersion": 1
            }
            """;
        File.WriteAllText(path, incompleteDocument);
        var store = new DeviceLocalPreferenceStore(_directory);

        DeviceLocalPreferenceSnapshot preferences = store.Load();

        Assert.True(preferences.Pacing.PauseWhenResponseRequiredDialogueOpens);
        Assert.Equal(incompleteDocument, File.ReadAllText(path));
    }

    [Fact]
    public void SaveCategoryPersistsAnOpaquePresentationPayload()
    {
        var store = new DeviceLocalPreferenceStore(_directory);
        using JsonDocument payload = JsonDocument.Parse("""
            {
              "activeOverlay": "sensors"
            }
            """);

        store.SaveCategory("presentation", payload.RootElement);

        DeviceLocalPreferenceSnapshot preferences = store.Load();
        Assert.Equal(
            "sensors",
            preferences.OtherCategories["presentation"]
                .GetProperty("activeOverlay")
                .GetString());
    }

    [Fact]
    public void ResetPacingRestoresItsDefaultsWithoutRemovingPresentation()
    {
        var store = new DeviceLocalPreferenceStore(_directory);
        using JsonDocument payload = JsonDocument.Parse("""
            {
              "activeOverlay": "sensors"
            }
            """);
        store.SaveCategory("presentation", payload.RootElement);
        store.SavePacing(new PacingPreferenceSnapshot(
            PauseWhenResponseRequiredDialogueOpens: false,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>()));

        store.ResetPacing();

        DeviceLocalPreferenceSnapshot preferences = store.Load();
        Assert.True(preferences.Pacing.PauseWhenResponseRequiredDialogueOpens);
        Assert.Equal(
            "sensors",
            preferences.OtherCategories["presentation"]
                .GetProperty("activeOverlay")
                .GetString());
    }

    [Fact]
    public void ResetCategoryRemovesOnlyTheNamedOpaqueCategory()
    {
        var store = new DeviceLocalPreferenceStore(_directory);
        using JsonDocument presentation = JsonDocument.Parse("""
            {
              "activeOverlay": "sensors"
            }
            """);
        using JsonDocument localization = JsonDocument.Parse("""
            {
              "locale": "en-US"
            }
            """);
        store.SaveCategory("presentation", presentation.RootElement);
        store.SaveCategory("localization", localization.RootElement);

        store.ResetCategory("presentation");

        DeviceLocalPreferenceSnapshot preferences = store.Load();
        Assert.DoesNotContain("presentation", preferences.OtherCategories.Keys);
        Assert.Equal(
            "en-US",
            preferences.OtherCategories["localization"].GetProperty("locale").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
