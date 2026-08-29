using System.Globalization;

namespace GalaxyCommand.GodotClient;

/// <summary>
/// Converts one local event-pacing result into disposable presentation text.
/// Stable notice identities remain visible without becoming authoritative.
/// </summary>
internal static class ApplicationEventPacingExplanation
{
    /// <summary>
    /// Describes the effective local response and every disclosed notice that
    /// participated, including ignored and grace-suppressed occurrences.
    /// </summary>
    internal static string Describe(ApplicationEventPacingBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string application = result.PacingChanged ? "APPLIED" : "NO CHANGE";
        string[] notices = result.Notices.Select(DescribeNotice).ToArray();
        string summary = $"PACE EVENT {DescribeAction(result.EffectiveAction)} {application}";
        return notices.Length == 0
            ? summary
            : $"{summary} | {string.Join(" | ", notices)}";
    }

    /// <summary>
    /// Keeps the stable category and subject visible while translating only
    /// this application's local disposition and action vocabulary.
    /// </summary>
    private static string DescribeNotice(ApplicationEventPacingNoticeResult result)
    {
        string identity = $"{result.Notice.Category.Value}/{result.Notice.Subject.Value}";
        return result.Disposition switch
        {
            ApplicationEventPacingNoticeDisposition.Ignored => $"{identity} IGNORED",
            ApplicationEventPacingNoticeDisposition.GraceSuppressed =>
                $"{identity} SUPPRESSED {DescribeAction(result.Action)}",
            ApplicationEventPacingNoticeDisposition.Contributed =>
                $"{identity} REQUESTED {DescribeAction(result.Action)}",
            _ => throw new InvalidOperationException(
                $"Unsupported event pacing disposition {result.Disposition}."),
        };
    }

    /// <summary>
    /// Formats the accepted action vocabulary without allowing locale to alter
    /// a configured multiplier's diagnostic representation.
    /// </summary>
    private static string DescribeAction(ApplicationEventPacingAction action)
    {
        return action switch
        {
            ApplicationEventPacingAction.Ignore => "NO CHANGE",
            ApplicationEventPacingAction.Pause => "PAUSE",
            ApplicationEventPacingAction.Cap cap =>
                $"CAP {cap.Multiplier.ToString(CultureInfo.InvariantCulture)}x",
            _ => throw new InvalidOperationException(
                $"Unsupported event pacing action {action.GetType().Name}."),
        };
    }
}
