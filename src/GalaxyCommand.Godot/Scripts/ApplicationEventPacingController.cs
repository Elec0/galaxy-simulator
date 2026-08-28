using System.Collections.ObjectModel;

namespace GalaxyCommand.GodotClient;

/// <summary>
/// Stable local identity for one event-responsive pacing category. Category
/// values belong to the application policy contract rather than simulation
/// authority or localized presentation.
/// </summary>
internal readonly record struct ApplicationPacingEventCategoryId
{
    internal ApplicationPacingEventCategoryId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    internal string Value { get; }

    internal void EnsureInitialized(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException(
                "An event pacing category identity must be initialized.",
                parameterName);
        }
    }

    public override string ToString() => Value;
}

/// <summary>
/// Stable local subject identity supplied by an owning typed event adapter.
/// This layer deliberately does not infer domain identity from presentation.
/// </summary>
internal readonly record struct ApplicationPacingEventSubjectId
{
    internal ApplicationPacingEventSubjectId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    internal string Value { get; }

    internal void EnsureInitialized(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException(
                "An event pacing subject identity must be initialized.",
                parameterName);
        }
    }

    public override string ToString() => Value;
}

/// <summary>
/// One disclosed, presentation-safe event occurrence offered to local pacing.
/// The owning adapter supplies both stable identities after disclosure.
/// </summary>
internal sealed record ApplicationPacingEventNotice(
    ApplicationPacingEventCategoryId Category,
    ApplicationPacingEventSubjectId Subject);

/// <summary>
/// Stable identifiers for the event categories accepted by TASK-064. Typed
/// domain adapters decide when an occurrence satisfies one of these categories.
/// </summary>
internal static class ApplicationPacingEventCategories
{
    internal static readonly ApplicationPacingEventCategoryId InformationalDialogueForegrounded =
        new("informational-dialogue-foregrounded");

    internal static readonly ApplicationPacingEventCategoryId PlayerAssetOffscreenCombatStarted =
        new("player-asset-offscreen-combat-started");
}

/// <summary>
/// The player-selected local pacing response for one supported category.
/// </summary>
internal abstract record ApplicationEventPacingAction
{
    internal sealed record Ignore : ApplicationEventPacingAction;

    internal sealed record Pause : ApplicationEventPacingAction;

    internal sealed record Cap(double Multiplier) : ApplicationEventPacingAction;
}

/// <summary>
/// Supplies the accepted initial local actions. Response-required dialogue
/// retains its existing dedicated preference and temporary pause ownership.
/// </summary>
internal static class ApplicationEventPacingPolicies
{
    internal static IReadOnlyDictionary<
        ApplicationPacingEventCategoryId,
        ApplicationEventPacingAction> CreateInitialDefaults() =>
        new ReadOnlyDictionary<
            ApplicationPacingEventCategoryId,
            ApplicationEventPacingAction>(
            new Dictionary<
                ApplicationPacingEventCategoryId,
                ApplicationEventPacingAction>
            {
                [ApplicationPacingEventCategories.InformationalDialogueForegrounded] =
                    new ApplicationEventPacingAction.Ignore(),
                [ApplicationPacingEventCategories.PlayerAssetOffscreenCombatStarted] =
                    new ApplicationEventPacingAction.Pause(),
            });

    /// <summary>
    /// Applies stored player overrides to current defaults. An unavailable cap
    /// produces a presentation-safe warning and leaves the stored value
    /// untouched outside this in-memory resolution.
    /// </summary>
    internal static ApplicationEventPacingPolicyResolution Resolve(
        ApplicationPacingController pacing,
        IReadOnlyDictionary<
            ApplicationPacingEventCategoryId,
            ApplicationEventPacingAction> storedOverrides)
    {
        ArgumentNullException.ThrowIfNull(pacing);
        ArgumentNullException.ThrowIfNull(storedOverrides);

        var policies = new Dictionary<
            ApplicationPacingEventCategoryId,
            ApplicationEventPacingAction>(CreateInitialDefaults());
        var warnings = new List<ApplicationEventPacingPolicyWarning>();
        foreach ((ApplicationPacingEventCategoryId category, ApplicationEventPacingAction action)
                 in storedOverrides)
        {
            category.EnsureInitialized(nameof(storedOverrides));
            ArgumentNullException.ThrowIfNull(action);
            if (!policies.ContainsKey(category))
            {
                throw new ArgumentException(
                    $"Unsupported event pacing category {category}.",
                    nameof(storedOverrides));
            }

            if (action is not ApplicationEventPacingAction.Ignore
                and not ApplicationEventPacingAction.Pause
                and not ApplicationEventPacingAction.Cap)
            {
                throw new ArgumentException(
                    $"Unsupported event pacing action {action.GetType().Name}.",
                    nameof(storedOverrides));
            }

            if (action is ApplicationEventPacingAction.Cap cap
                && !pacing.RunningSpeedMultipliers.Contains(cap.Multiplier))
            {
                warnings.Add(new ApplicationEventPacingPolicyWarning(
                    category,
                    cap.Multiplier));
                continue;
            }

            policies[category] = action;
        }

        return new ApplicationEventPacingPolicyResolution(
            new ReadOnlyDictionary<
                ApplicationPacingEventCategoryId,
                ApplicationEventPacingAction>(policies),
            warnings.AsReadOnly());
    }
}

/// <summary>
/// Locale-neutral warning that one stored speed cap is unavailable under the
/// current validated ladder.
/// </summary>
internal sealed record ApplicationEventPacingPolicyWarning(
    ApplicationPacingEventCategoryId Category,
    double UnavailableMultiplier);

/// <summary>
/// In-memory policies for one launch plus warnings presentation should expose.
/// </summary>
internal sealed record ApplicationEventPacingPolicyResolution(
    IReadOnlyDictionary<
        ApplicationPacingEventCategoryId,
        ApplicationEventPacingAction> Policies,
    IReadOnlyList<ApplicationEventPacingPolicyWarning> Warnings);

/// <summary>
/// Explains how one offered occurrence participated in local pacing.
/// </summary>
internal enum ApplicationEventPacingNoticeDisposition
{
    Ignored,
    GraceSuppressed,
    Contributed,
}

/// <summary>
/// Keeps a disclosed notice available for presentation together with its local
/// policy result. It contains no authoritative simulation state.
/// </summary>
internal sealed record ApplicationEventPacingNoticeResult(
    ApplicationPacingEventNotice Notice,
    ApplicationEventPacingAction Action,
    ApplicationEventPacingNoticeDisposition Disposition);

/// <summary>
/// Result of evaluating every event-responsive pacing occurrence available at
/// one completed simulation timestamp boundary.
/// </summary>
internal sealed record ApplicationEventPacingBatchResult(
    ApplicationEventPacingAction EffectiveAction,
    bool PacingChanged,
    IReadOnlyList<ApplicationEventPacingNoticeResult> Notices);

/// <summary>
/// Evaluates disclosed event occurrences against local player policy, applies
/// one strongest pacing response, and retains only transient monotonic grace
/// windows. Event producers never gain pacing ownership through this class.
/// </summary>
internal sealed class ApplicationEventPacingController
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);
    private static readonly ApplicationEventPacingAction.Ignore IgnoreAction = new();

    private readonly ApplicationPacingController _pacing;
    private readonly IReadOnlyDictionary<
        ApplicationPacingEventCategoryId,
        ApplicationEventPacingAction> _policies;
    private readonly Dictionary<ApplicationPacingEventKey, TimeSpan> _lastOccurrences = [];
    private TimeSpan? _lastBoundaryTime;

    internal int ActiveGraceWindowCount => _lastOccurrences.Count;

    internal ApplicationEventPacingController(
        ApplicationPacingController pacing,
        IReadOnlyDictionary<
            ApplicationPacingEventCategoryId,
            ApplicationEventPacingAction> policies)
    {
        ArgumentNullException.ThrowIfNull(pacing);
        ArgumentNullException.ThrowIfNull(policies);

        var copiedPolicies = new Dictionary<
            ApplicationPacingEventCategoryId,
            ApplicationEventPacingAction>();
        foreach ((ApplicationPacingEventCategoryId category, ApplicationEventPacingAction action)
                 in policies)
        {
            category.EnsureInitialized(nameof(policies));
            ArgumentNullException.ThrowIfNull(action);
            ValidateAction(pacing, action);
            copiedPolicies.Add(category, action);
        }

        _pacing = pacing;
        _policies = new ReadOnlyDictionary<
            ApplicationPacingEventCategoryId,
            ApplicationEventPacingAction>(copiedPolicies);
    }

    /// <summary>
    /// Evaluates one completed-boundary batch at the supplied monotonic local
    /// time. A later call cannot move that clock backward.
    /// </summary>
    internal ApplicationEventPacingBatchResult ApplyAtBoundary(
        IEnumerable<ApplicationPacingEventNotice> notices,
        TimeSpan monotonicNow)
    {
        ArgumentNullException.ThrowIfNull(notices);
        ApplicationPacingEventNotice[] noticeBatch = notices.ToArray();
        foreach (ApplicationPacingEventNotice notice in noticeBatch)
        {
            ArgumentNullException.ThrowIfNull(notice);
            notice.Category.EnsureInitialized(nameof(notices));
            notice.Subject.EnsureInitialized(nameof(notices));
        }

        if (monotonicNow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monotonicNow),
                monotonicNow,
                "Monotonic application time cannot be negative.");
        }

        if (_lastBoundaryTime is { } previousBoundary && monotonicNow < previousBoundary)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monotonicNow),
                monotonicNow,
                "Monotonic application time cannot move backward.");
        }

        _lastBoundaryTime = monotonicNow;
        ApplicationPacingEventKey[] expiredKeys = _lastOccurrences
            .Where(entry => monotonicNow - entry.Value >= GracePeriod)
            .Select(entry => entry.Key)
            .ToArray();
        foreach (ApplicationPacingEventKey expiredKey in expiredKeys)
        {
            _lastOccurrences.Remove(expiredKey);
        }

        var results = new List<ApplicationEventPacingNoticeResult>();
        bool pauseRequested = false;
        double? lowestCap = null;

        foreach (ApplicationPacingEventNotice notice in noticeBatch)
        {
            ApplicationEventPacingAction action = _policies.GetValueOrDefault(
                notice.Category,
                IgnoreAction);
            if (action is ApplicationEventPacingAction.Ignore)
            {
                results.Add(new ApplicationEventPacingNoticeResult(
                    notice,
                    action,
                    ApplicationEventPacingNoticeDisposition.Ignored));
                continue;
            }

            var key = new ApplicationPacingEventKey(notice.Category, notice.Subject);
            if (_lastOccurrences.TryGetValue(key, out TimeSpan previousOccurrence)
                && monotonicNow - previousOccurrence < GracePeriod)
            {
                _lastOccurrences[key] = monotonicNow;
                results.Add(new ApplicationEventPacingNoticeResult(
                    notice,
                    action,
                    ApplicationEventPacingNoticeDisposition.GraceSuppressed));
                continue;
            }

            _lastOccurrences[key] = monotonicNow;
            results.Add(new ApplicationEventPacingNoticeResult(
                notice,
                action,
                ApplicationEventPacingNoticeDisposition.Contributed));
            switch (action)
            {
                case ApplicationEventPacingAction.Pause:
                    pauseRequested = true;
                    break;
                case ApplicationEventPacingAction.Cap cap:
                    lowestCap = lowestCap is null
                        ? cap.Multiplier
                        : Math.Min(lowestCap.Value, cap.Multiplier);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported event pacing action {action.GetType().Name}.");
            }
        }

        ApplicationEventPacingAction effectiveAction;
        bool pacingChanged;
        if (pauseRequested)
        {
            effectiveAction = new ApplicationEventPacingAction.Pause();
            pacingChanged = _pacing.ApplyEventPause();
        }
        else if (lowestCap is { } multiplier)
        {
            effectiveAction = new ApplicationEventPacingAction.Cap(multiplier);
            pacingChanged = _pacing.ApplyEventSpeedCap(multiplier);
        }
        else
        {
            effectiveAction = IgnoreAction;
            pacingChanged = false;
        }

        return new ApplicationEventPacingBatchResult(
            effectiveAction,
            pacingChanged,
            results.AsReadOnly());
    }

    /// <summary>
    /// Clears transient grace state when the application starts or replaces a
    /// game session. Policies remain device-local configuration.
    /// </summary>
    internal void ClearGraceWindows()
    {
        _lastOccurrences.Clear();
        _lastBoundaryTime = null;
    }

    private static void ValidateAction(
        ApplicationPacingController pacing,
        ApplicationEventPacingAction action)
    {
        switch (action)
        {
            case ApplicationEventPacingAction.Ignore:
            case ApplicationEventPacingAction.Pause:
                return;
            case ApplicationEventPacingAction.Cap cap
                when pacing.RunningSpeedMultipliers.Contains(cap.Multiplier):
                return;
            case ApplicationEventPacingAction.Cap cap:
                throw new ArgumentOutOfRangeException(
                    nameof(action),
                    cap.Multiplier,
                    "An event pacing cap must name a configured speed multiplier.");
            default:
                throw new ArgumentException(
                    $"Unsupported event pacing action {action.GetType().Name}.",
                    nameof(action));
        }
    }

    private readonly record struct ApplicationPacingEventKey(
        ApplicationPacingEventCategoryId Category,
        ApplicationPacingEventSubjectId Subject);
}
