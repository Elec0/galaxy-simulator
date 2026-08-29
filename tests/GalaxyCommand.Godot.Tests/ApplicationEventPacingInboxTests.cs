using GalaxyCommand.GodotClient;

namespace GalaxyCommand.Godot.Tests;

public sealed class ApplicationEventPacingInboxTests
{
    private static readonly ApplicationPacingEventCategoryId CombatStarted =
        ApplicationPacingEventCategories.PlayerAssetOffscreenCombatStarted;

    [Fact]
    public void PendingDisclosedNoticesApplyTogetherAtOneBoundary()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        var controller = new ApplicationEventPacingController(
            pacing,
            ApplicationEventPacingPolicies.CreateInitialDefaults());
        var inbox = new ApplicationEventPacingInbox(controller);
        var first = new ApplicationPacingEventNotice(
            CombatStarted,
            new ApplicationPacingEventSubjectId("ship-17"));
        var second = new ApplicationPacingEventNotice(
            CombatStarted,
            new ApplicationPacingEventSubjectId("ship-18"));

        inbox.Enqueue(first);
        inbox.Enqueue(second);
        ApplicationEventPacingBatchResult? result = inbox.ApplyPendingAtBoundary(
            TimeSpan.FromSeconds(3));

        Assert.NotNull(result);
        Assert.True(pacing.IsPaused);
        Assert.Equal([first, second], result.Notices.Select(item => item.Notice));
        Assert.Equal(0, inbox.Count);
        Assert.Null(inbox.ApplyPendingAtBoundary(TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void RejectedBoundaryDoesNotDiscardPendingDisclosedNotices()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        var controller = new ApplicationEventPacingController(
            pacing,
            ApplicationEventPacingPolicies.CreateInitialDefaults());
        var inbox = new ApplicationEventPacingInbox(controller);
        inbox.Enqueue(new ApplicationPacingEventNotice(
            CombatStarted,
            new ApplicationPacingEventSubjectId("ship-17")));
        inbox.ApplyPendingAtBoundary(TimeSpan.FromSeconds(5));
        var pending = new ApplicationPacingEventNotice(
            CombatStarted,
            new ApplicationPacingEventSubjectId("ship-18"));
        inbox.Enqueue(pending);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            inbox.ApplyPendingAtBoundary(TimeSpan.FromSeconds(4)));

        Assert.Equal(1, inbox.Count);
        ApplicationEventPacingBatchResult? retry = inbox.ApplyPendingAtBoundary(
            TimeSpan.FromSeconds(6));
        Assert.NotNull(retry);
        Assert.Equal(pending, Assert.Single(retry.Notices).Notice);
    }
}

public sealed class ApplicationEventPacingExplanationTests
{
    [Fact]
    public void ExplanationIncludesEveryNoticeDispositionAndRequestedAction()
    {
        ApplicationPacingEventCategoryId combat =
            ApplicationPacingEventCategories.PlayerAssetOffscreenCombatStarted;
        var pause = new ApplicationEventPacingAction.Pause();
        var ignore = new ApplicationEventPacingAction.Ignore();
        var result = new ApplicationEventPacingBatchResult(
            pause,
            PacingChanged: true,
            [
                new ApplicationEventPacingNoticeResult(
                    new ApplicationPacingEventNotice(
                        combat,
                        new ApplicationPacingEventSubjectId("ship-17")),
                    pause,
                    ApplicationEventPacingNoticeDisposition.Contributed),
                new ApplicationEventPacingNoticeResult(
                    new ApplicationPacingEventNotice(
                        combat,
                        new ApplicationPacingEventSubjectId("ship-18")),
                    pause,
                    ApplicationEventPacingNoticeDisposition.GraceSuppressed),
                new ApplicationEventPacingNoticeResult(
                    new ApplicationPacingEventNotice(
                        ApplicationPacingEventCategories.InformationalDialogueForegrounded,
                        new ApplicationPacingEventSubjectId("dialogue-4")),
                    ignore,
                    ApplicationEventPacingNoticeDisposition.Ignored),
            ]);

        string explanation = ApplicationEventPacingExplanation.Describe(result);

        Assert.Contains("PACE EVENT PAUSE", explanation, StringComparison.Ordinal);
        Assert.Contains("ship-17 REQUESTED PAUSE", explanation, StringComparison.Ordinal);
        Assert.Contains("ship-18 SUPPRESSED PAUSE", explanation, StringComparison.Ordinal);
        Assert.Contains("dialogue-4 IGNORED", explanation, StringComparison.Ordinal);
    }
}
