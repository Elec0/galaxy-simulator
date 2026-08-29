using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalaxyCommand.GodotClient;

/// <summary>
/// Immutable device-local pacing choices consumed by application presentation.
/// These values never enter a session, checkpoint, or save file.
/// </summary>
internal sealed record PacingPreferenceSnapshot
{
	internal PacingPreferenceSnapshot(
		bool PauseWhenResponseRequiredDialogueOpens,
		IReadOnlyDictionary<
			ApplicationPacingEventCategoryId,
			ApplicationEventPacingAction> eventPacingOverrides)
	{
		ArgumentNullException.ThrowIfNull(eventPacingOverrides);
		this.PauseWhenResponseRequiredDialogueOpens = PauseWhenResponseRequiredDialogueOpens;
		EventPacingOverrides = new ReadOnlyDictionary<
			ApplicationPacingEventCategoryId,
			ApplicationEventPacingAction>(
			new Dictionary<
				ApplicationPacingEventCategoryId,
				ApplicationEventPacingAction>(eventPacingOverrides));
	}

	internal bool PauseWhenResponseRequiredDialogueOpens { get; }

	internal IReadOnlyDictionary<
		ApplicationPacingEventCategoryId,
		ApplicationEventPacingAction> EventPacingOverrides
	{ get; }

	internal static PacingPreferenceSnapshot CreateDefaults() =>
		new(
			PauseWhenResponseRequiredDialogueOpens: true,
			new ReadOnlyDictionary<
				ApplicationPacingEventCategoryId,
				ApplicationEventPacingAction>(
				new Dictionary<
					ApplicationPacingEventCategoryId,
					ApplicationEventPacingAction>()));
}

/// <summary>
/// Immutable local-preference data made available to presentation consumers.
/// Additional category owners extend this shared boundary without changing
/// authoritative simulation state.
/// </summary>
internal sealed record DeviceLocalPreferenceSnapshot(
	PacingPreferenceSnapshot Pacing,
	IReadOnlyDictionary<string, JsonElement> OtherCategories)
{
	internal static DeviceLocalPreferenceSnapshot CreateDefaults() =>
		new(
			PacingPreferenceSnapshot.CreateDefaults(),
			new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>()));
}

/// <summary>
/// Describes why the application used defaults instead of a stored local
/// preference document, so the settings surface can offer an explicit reset.
/// </summary>
internal enum DeviceLocalPreferenceLoadFailureKind
{
	InvalidOrUnsupported,
	StorageAccess,
}

/// <summary>
/// Non-sensitive local diagnostic for a recoverable preference-store failure.
/// It intentionally excludes file contents and authoritative game state.
/// </summary>
internal sealed record DeviceLocalPreferenceLoadFailure(
	DeviceLocalPreferenceLoadFailureKind Kind);

/// <summary>
/// Owns the versioned device-local preference document without making any
/// preference authoritative session, checkpoint, or save state.
/// </summary>
internal sealed class DeviceLocalPreferenceStore
{
	private const string Format = "galaxy-command-device-preferences";
	private const int CurrentSchemaVersion = 1;
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	private readonly string _path;

	/// <summary>
	/// Gets the recoverable failure from the most recent <see cref="Load"/> call,
	/// or <see langword="null"/> when no reset action needs to be offered.
	/// </summary>
	internal DeviceLocalPreferenceLoadFailure? LastLoadFailure { get; private set; }

	internal DeviceLocalPreferenceStore(string directory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		_path = Path.Combine(Path.GetFullPath(directory), "preferences.json");
	}

	/// <summary>
	/// Returns the accepted defaults until an explicit preference write creates
	/// the shared local store. Reading defaults must not create user data.
	/// </summary>
	internal DeviceLocalPreferenceSnapshot Load()
	{
		LastLoadFailure = null;
		if (!File.Exists(_path))
		{
			return DeviceLocalPreferenceSnapshot.CreateDefaults();
		}

		try
		{
			return ReadCurrentStore();
		}
		catch (Exception exception) when (IsRecoverableReadFailure(exception))
		{
			// A read failure must not silently repair user data. Defaults keep
			// the application usable until an explicit reset is requested.
			LastLoadFailure = new DeviceLocalPreferenceLoadFailure(
				exception is IOException or UnauthorizedAccessException
					? DeviceLocalPreferenceLoadFailureKind.StorageAccess
					: DeviceLocalPreferenceLoadFailureKind.InvalidOrUnsupported);
			return DeviceLocalPreferenceSnapshot.CreateDefaults();
		}
	}

	/// <summary>
	/// Replaces only the pacing category after an explicit player preference
	/// change. The stored cap remains a requested multiplier so startup can
	/// compare it with the currently validated speed ladder.
	/// </summary>
	internal void SavePacing(PacingPreferenceSnapshot pacing)
	{
		ArgumentNullException.ThrowIfNull(pacing);
		DeviceLocalPreferenceSnapshot existing = ReadExistingStoreForReplacement();
		Write(pacing, existing.OtherCategories);
	}

	/// <summary>
	/// Replaces one opaque category owned by a presentation subsystem while
	/// preserving pacing and every other category. The payload stays local and
	/// schema ownership remains with the caller's task.
	/// </summary>
	internal void SaveCategory(string category, JsonElement payload)
	{
		ValidateCategory(category);
		if (payload.ValueKind == JsonValueKind.Undefined)
		{
			throw new ArgumentException(
				"A device-local preference category requires a JSON value.",
				nameof(payload));
		}

		DeviceLocalPreferenceSnapshot existing = ReadExistingStoreForReplacement();
		var otherCategories = new Dictionary<string, JsonElement>(
			existing.OtherCategories,
			StringComparer.Ordinal)
		{
			[category] = payload.Clone(),
		};
		Write(existing.Pacing, otherCategories);
	}

	/// <summary>
	/// Restores the pacing category to its accepted defaults while retaining
	/// every independently owned category in the same shared document.
	/// </summary>
	internal void ResetPacing()
	{
		DeviceLocalPreferenceSnapshot existing = ReadExistingStoreForReplacement();
		Write(PacingPreferenceSnapshot.CreateDefaults(), existing.OtherCategories);
	}

	/// <summary>
	/// Removes one opaque category so its owning subsystem falls back to its
	/// own defaults, without changing pacing or unrelated local preferences.
	/// </summary>
	internal void ResetCategory(string category)
	{
		ValidateCategory(category);
		DeviceLocalPreferenceSnapshot existing = ReadExistingStoreForReplacement();
		var otherCategories = new Dictionary<string, JsonElement>(
			existing.OtherCategories,
			StringComparer.Ordinal);
		otherCategories.Remove(category);
		Write(existing.Pacing, otherCategories);
	}

	/// <summary>
	/// Replaces the complete local document with accepted defaults only after
	/// the player explicitly requests reset. This is the sole path that may
	/// replace unreadable or unsupported preference data.
	/// </summary>
	internal void ResetAll()
	{
		Write(
			PacingPreferenceSnapshot.CreateDefaults(),
			DeviceLocalPreferenceSnapshot.CreateDefaults().OtherCategories);
		LastLoadFailure = null;
	}

	/// <summary>
	/// Encodes one complete document after an explicit preference change or
	/// reset. No load path calls this method, preserving invalid user data.
	/// </summary>
	private void Write(
		PacingPreferenceSnapshot pacing,
		IReadOnlyDictionary<string, JsonElement> otherCategories)
	{
		ArgumentNullException.ThrowIfNull(otherCategories);
		PreferenceDocument document = new(
			Format,
			CurrentSchemaVersion,
			new PacingPreferenceDocument(
				pacing.PauseWhenResponseRequiredDialogueOpens,
				pacing.EventPacingOverrides
					.OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
					.Select(entry => ToDocument(entry.Key, entry.Value))
					.ToArray()))
		{
			OtherCategories = otherCategories.ToDictionary(
				entry => entry.Key,
				entry => entry.Value.Clone(),
				StringComparer.Ordinal),
		};
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		File.WriteAllText(_path, JsonSerializer.Serialize(document, JsonOptions));
	}

	/// <summary>
	/// Reads and validates the existing current-schema document. Callers decide
	/// whether a failure means safe defaults or a rejected replacement.
	/// </summary>
	private DeviceLocalPreferenceSnapshot ReadCurrentStore()
	{
		PreferenceDocument document = JsonSerializer.Deserialize<PreferenceDocument>(
			File.ReadAllText(_path),
			JsonOptions)
			?? throw new InvalidDataException("The device-local preference store is empty.");
		if (document.Format != Format || document.SchemaVersion != CurrentSchemaVersion)
		{
			throw new InvalidDataException(
				"The device-local preference store has an unsupported format or schema version.");
		}

		PacingPreferenceDocument? pacing = document.Pacing;
		if (pacing?.EventPacingOverrides is null)
		{
			throw new InvalidDataException(
				"The device-local preference store has no valid pacing category.");
		}

		return new DeviceLocalPreferenceSnapshot(
			new PacingPreferenceSnapshot(
				pacing.PauseWhenResponseRequiredDialogueOpens,
				pacing.EventPacingOverrides.ToDictionary(
					entry => new ApplicationPacingEventCategoryId(entry.Category),
					entry => ToAction(entry))),
			new ReadOnlyDictionary<string, JsonElement>(
				(document.OtherCategories ?? [])
					.ToDictionary(
						entry => entry.Key,
						entry => entry.Value.Clone(),
						StringComparer.Ordinal)));
	}

	/// <summary>
	/// Rejects an ordinary preference save when its source cannot be safely
	/// read. Only <see cref="ResetAll"/> may intentionally replace that file.
	/// </summary>
	private DeviceLocalPreferenceSnapshot ReadExistingStoreForReplacement()
	{
		if (!File.Exists(_path))
		{
			return DeviceLocalPreferenceSnapshot.CreateDefaults();
		}

		try
		{
			return ReadCurrentStore();
		}
		catch (Exception exception) when (IsRecoverableReadFailure(exception))
		{
			throw new InvalidOperationException(
				"The local preference store must be reset before it can be replaced.",
				exception);
		}
	}

	private static bool IsRecoverableReadFailure(Exception exception) =>
		exception is JsonException
			or InvalidDataException
			or IOException
			or UnauthorizedAccessException
			or ArgumentException;

	private static void ValidateCategory(string category)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		if (category is "format" or "schemaVersion" or "pacing")
		{
			throw new ArgumentException(
				$"'{category}' is reserved by the device-local preference envelope.",
				nameof(category));
		}
	}

	private static ApplicationEventPacingAction ToAction(
		EventPacingOverrideDocument entry)
	{
		return entry.Kind switch
		{
			"ignore" when entry.Multiplier is null => new ApplicationEventPacingAction.Ignore(),
			"pause" when entry.Multiplier is null => new ApplicationEventPacingAction.Pause(),
			"cap" when entry.Multiplier is { } multiplier
				&& multiplier > 0d
				&& double.IsFinite(multiplier) => new ApplicationEventPacingAction.Cap(multiplier),
			_ => throw new InvalidDataException(
				$"The pacing action for category '{entry.Category}' is invalid."),
		};
	}

	private static EventPacingOverrideDocument ToDocument(
		ApplicationPacingEventCategoryId category,
		ApplicationEventPacingAction action)
	{
		category.EnsureInitialized(nameof(category));
		ArgumentNullException.ThrowIfNull(action);
		return action switch
		{
			ApplicationEventPacingAction.Ignore => new EventPacingOverrideDocument(
				category.Value,
				"ignore",
				null),
			ApplicationEventPacingAction.Pause => new EventPacingOverrideDocument(
				category.Value,
				"pause",
				null),
			ApplicationEventPacingAction.Cap cap when cap.Multiplier > 0d
				&& double.IsFinite(cap.Multiplier) => new EventPacingOverrideDocument(
					category.Value,
					"cap",
					cap.Multiplier),
			_ => throw new ArgumentException(
				$"The pacing action for category '{category}' is invalid.",
				nameof(action)),
		};
	}

	private sealed record PreferenceDocument(
		string Format,
		int SchemaVersion,
		PacingPreferenceDocument Pacing)
	{
		[JsonExtensionData]
		public Dictionary<string, JsonElement>? OtherCategories { get; init; }
	}

	private sealed record PacingPreferenceDocument(
		bool PauseWhenResponseRequiredDialogueOpens,
		IReadOnlyList<EventPacingOverrideDocument> EventPacingOverrides);

	private sealed record EventPacingOverrideDocument(
		string Category,
		string Kind,
		double? Multiplier);
}
