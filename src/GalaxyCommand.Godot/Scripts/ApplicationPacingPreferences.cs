namespace GalaxyCommand.GodotClient;

/// <summary>
/// Resolved local pacing preferences for one application launch. Stored values
/// remain device-local while unavailable speed caps fall back only in memory.
/// </summary>
internal sealed record ApplicationPacingPreferenceState(
	bool PauseWhenResponseRequiredDialogueOpens,
	ApplicationEventPacingPolicyResolution EventPacing,
	DeviceLocalPreferenceLoadFailure? StoreLoadFailure);

/// <summary>
/// Connects the shared device-local preference boundary to the current
/// validated pacing ladder without granting preferences simulation ownership.
/// </summary>
internal static class ApplicationPacingPreferences
{
	/// <summary>
	/// Loads the local pacing category and resolves its event policies against
	/// this launch's speed ladder. The method never repairs or rewrites a stored
	/// document, leaving explicit reset ownership with the settings surface.
	/// </summary>
	internal static ApplicationPacingPreferenceState Load(
		DeviceLocalPreferenceStore store,
		ApplicationPacingController pacing)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(pacing);

		DeviceLocalPreferenceSnapshot preferences = store.Load();
		return new ApplicationPacingPreferenceState(
			preferences.Pacing.PauseWhenResponseRequiredDialogueOpens,
			ApplicationEventPacingPolicies.Resolve(
				pacing,
				preferences.Pacing.EventPacingOverrides),
			store.LastLoadFailure);
	}

	/// <summary>
	/// Returns the concise local diagnostic presentation should display for a
	/// recoverable preference problem or unavailable stored speed cap. It never
	/// exposes preference file contents or makes the diagnostic authoritative.
	/// </summary>
	internal static string DescribeConfigurationWarning(ApplicationPacingPreferenceState preferences)
	{
		ArgumentNullException.ThrowIfNull(preferences);
		if (preferences.StoreLoadFailure is not null)
		{
			return "PACING PREFERENCES DEFAULTED";
		}

		return preferences.EventPacing.Warnings.Count switch
		{
			0 => string.Empty,
			1 => "PACING CAP FALLBACK: 1 UNAVAILABLE",
			_ => $"PACING CAP FALLBACK: {preferences.EventPacing.Warnings.Count} UNAVAILABLE",
		};
	}
}
