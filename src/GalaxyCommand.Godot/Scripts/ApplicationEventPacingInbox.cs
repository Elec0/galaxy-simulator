namespace GalaxyCommand.GodotClient;

/// <summary>
/// Application-owned boundary for disclosed event pacing notices. Domain
/// adapters remain responsible for deciding which typed occurrences may enter.
/// </summary>
internal sealed class ApplicationEventPacingInbox
{
    private readonly Queue<ApplicationPacingEventNotice> _pending = [];
    private readonly ApplicationEventPacingController _controller;

    internal int Count => _pending.Count;

    internal ApplicationEventPacingInbox(ApplicationEventPacingController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
    }

    /// <summary>
    /// Accepts one notice only after its owning adapter has applied the domain's
    /// disclosure and presentation-safe classification rules.
    /// </summary>
    internal void Enqueue(ApplicationPacingEventNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        notice.Category.EnsureInitialized(nameof(notice));
        notice.Subject.EnsureInitialized(nameof(notice));
        _pending.Enqueue(notice);
    }

    /// <summary>
    /// Applies all notices pending at one completed simulation timestamp
    /// boundary, or returns no result when no notice was available.
    /// </summary>
    internal ApplicationEventPacingBatchResult? ApplyPendingAtBoundary(
        TimeSpan monotonicNow)
    {
        if (_pending.Count == 0)
        {
            return null;
        }

        ApplicationPacingEventNotice[] batch = _pending.ToArray();
        ApplicationEventPacingBatchResult result = _controller.ApplyAtBoundary(
            batch,
            monotonicNow);

        // Clear only after successful evaluation so a rejected boundary cannot
        // silently discard already disclosed presentation events.
        for (int index = 0; index < batch.Length; index++)
        {
            _pending.Dequeue();
        }

        return result;
    }
}
