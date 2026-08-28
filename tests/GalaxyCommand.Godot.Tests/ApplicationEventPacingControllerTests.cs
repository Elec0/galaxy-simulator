using GalaxyCommand.GodotClient;

namespace GalaxyCommand.Godot.Tests;

public sealed class ApplicationEventPacingControllerTests
{
    private static readonly ApplicationPacingEventCategoryId CombatStarted =
        new("player-asset-offscreen-combat-started");
    private static readonly ApplicationPacingEventSubjectId PlayerAsset =
        new("ship-17");
    private static readonly ApplicationPacingEventCategoryId SlowEvent =
        new("slow-event");

    [Fact]
    public void RecentOccurrenceDoesNotUndoThePlayersHigherPacingOverride()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        var eventPacing = new ApplicationEventPacingController(
            pacing,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
            {
                [CombatStarted] = new ApplicationEventPacingAction.Pause(),
            });
        var notice = new ApplicationPacingEventNotice(CombatStarted, PlayerAsset);

        eventPacing.ApplyAtBoundary([notice], TimeSpan.Zero);
        pacing.SelectSpeed(5d);
        ApplicationEventPacingBatchResult repeated = eventPacing.ApplyAtBoundary(
            [notice],
            TimeSpan.FromSeconds(4));

        Assert.False(pacing.IsPaused);
        Assert.Equal(5d, pacing.SelectedSpeedMultiplier);
        Assert.Equal(
            ApplicationEventPacingNoticeDisposition.GraceSuppressed,
            Assert.Single(repeated.Notices).Disposition);
    }

    [Fact]
    public void InvalidBatchDoesNotConsumeGraceStateBeforeAValidRetry()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        var eventPacing = new ApplicationEventPacingController(
            pacing,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
            {
                [CombatStarted] = new ApplicationEventPacingAction.Pause(),
            });
        var notice = new ApplicationPacingEventNotice(CombatStarted, PlayerAsset);

        Assert.Throws<ArgumentNullException>(() => eventPacing.ApplyAtBoundary(
            [notice, null!],
            TimeSpan.Zero));

        ApplicationEventPacingBatchResult retry = eventPacing.ApplyAtBoundary(
            [notice],
            TimeSpan.Zero);

        Assert.True(pacing.IsPaused);
        Assert.Equal(
            ApplicationEventPacingNoticeDisposition.Contributed,
            Assert.Single(retry.Notices).Disposition);
    }

    [Fact]
    public void ConstructorRejectsAnUninitializedCategoryIdentity()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);

        Assert.Throws<ArgumentException>(() =>
            new ApplicationEventPacingController(
                pacing,
                new Dictionary<
                    ApplicationPacingEventCategoryId,
                    ApplicationEventPacingAction>
                {
                    [default] = new ApplicationEventPacingAction.Pause(),
                }));
    }

    [Fact]
    public void InitialDefaultsIgnoreInformationalDialogueAndPauseOwnedAssetCombat()
    {
        IReadOnlyDictionary<
            ApplicationPacingEventCategoryId,
            ApplicationEventPacingAction> policies =
            ApplicationEventPacingPolicies.CreateInitialDefaults();

        Assert.IsType<ApplicationEventPacingAction.Ignore>(
            policies[ApplicationPacingEventCategories.InformationalDialogueForegrounded]);
        Assert.IsType<ApplicationEventPacingAction.Pause>(
            policies[ApplicationPacingEventCategories.PlayerAssetOffscreenCombatStarted]);
    }

    [Fact]
    public void UnavailableStoredCapWarnsAndUsesTheCategoryDefaultForThisLaunch()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);

        ApplicationEventPacingPolicyResolution resolution =
            ApplicationEventPacingPolicies.Resolve(
                pacing,
                new Dictionary<
                    ApplicationPacingEventCategoryId,
                    ApplicationEventPacingAction>
                {
                    [ApplicationPacingEventCategories.PlayerAssetOffscreenCombatStarted] =
                        new ApplicationEventPacingAction.Cap(10d),
                });

        Assert.IsType<ApplicationEventPacingAction.Pause>(
            resolution.Policies[
                ApplicationPacingEventCategories.PlayerAssetOffscreenCombatStarted]);
        ApplicationEventPacingPolicyWarning warning = Assert.Single(resolution.Warnings);
        Assert.Equal(
            ApplicationPacingEventCategories.PlayerAssetOffscreenCombatStarted,
            warning.Category);
        Assert.Equal(10d, warning.UnavailableMultiplier);
    }

    [Fact]
    public void ExpiredGraceWindowsAreDiscardedAtALaterBoundary()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        var eventPacing = new ApplicationEventPacingController(
            pacing,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
            {
                [CombatStarted] = new ApplicationEventPacingAction.Pause(),
            });

        eventPacing.ApplyAtBoundary(
            [
                new ApplicationPacingEventNotice(
                    CombatStarted,
                    new ApplicationPacingEventSubjectId("ship-17")),
                new ApplicationPacingEventNotice(
                    CombatStarted,
                    new ApplicationPacingEventSubjectId("ship-18")),
            ],
            TimeSpan.Zero);
        Assert.Equal(2, eventPacing.ActiveGraceWindowCount);

        eventPacing.ApplyAtBoundary([], TimeSpan.FromSeconds(5));

        Assert.Equal(0, eventPacing.ActiveGraceWindowCount);
    }

    [Fact]
    public void CapBelowTheCurrentSpeedReportsThatPacingWasUnchanged()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(2d);
        var eventPacing = new ApplicationEventPacingController(
            pacing,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
            {
                [CombatStarted] = new ApplicationEventPacingAction.Cap(5d),
            });

        ApplicationEventPacingBatchResult result = eventPacing.ApplyAtBoundary(
            [new ApplicationPacingEventNotice(CombatStarted, PlayerAsset)],
            TimeSpan.Zero);

        Assert.False(result.PacingChanged);
        Assert.Equal(2d, pacing.SelectedSpeedMultiplier);
        Assert.IsType<ApplicationEventPacingAction.Cap>(result.EffectiveAction);
    }

    [Fact]
    public void ConstructorRejectsAnUnsupportedPolicyActionBeforeEvaluation()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);

        Assert.Throws<ArgumentException>(() =>
            new ApplicationEventPacingController(
                pacing,
                new Dictionary<
                    ApplicationPacingEventCategoryId,
                    ApplicationEventPacingAction>
                {
                    [CombatStarted] = new UnsupportedAction(),
                }));
    }

    [Fact]
    public void PauseWinsOverCapsAndEveryNoticeRemainsAvailableForExplanation()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        var eventPacing = new ApplicationEventPacingController(
            pacing,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
            {
                [CombatStarted] = new ApplicationEventPacingAction.Pause(),
                [SlowEvent] = new ApplicationEventPacingAction.Cap(2d),
            });
        var capNotice = new ApplicationPacingEventNotice(
            SlowEvent,
            new ApplicationPacingEventSubjectId("slow-subject"));
        var pauseNotice = new ApplicationPacingEventNotice(CombatStarted, PlayerAsset);

        ApplicationEventPacingBatchResult result = eventPacing.ApplyAtBoundary(
            [capNotice, pauseNotice],
            TimeSpan.Zero);

        Assert.True(pacing.IsPaused);
        Assert.True(result.PacingChanged);
        Assert.IsType<ApplicationEventPacingAction.Pause>(result.EffectiveAction);
        Assert.Equal([capNotice, pauseNotice], result.Notices.Select(item => item.Notice));
        Assert.All(
            result.Notices,
            item => Assert.Equal(
                ApplicationEventPacingNoticeDisposition.Contributed,
                item.Disposition));
    }

    [Fact]
    public void LowestCapWinsWithoutResumingAnExistingPause()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d, 10d]);
        pacing.SelectSpeed(10d);
        var eventPacing = new ApplicationEventPacingController(
            pacing,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
            {
                [CombatStarted] = new ApplicationEventPacingAction.Cap(5d),
                [SlowEvent] = new ApplicationEventPacingAction.Cap(2d),
            });

        eventPacing.ApplyAtBoundary(
            [
                new ApplicationPacingEventNotice(CombatStarted, PlayerAsset),
                new ApplicationPacingEventNotice(
                    SlowEvent,
                    new ApplicationPacingEventSubjectId("slow-subject")),
            ],
            TimeSpan.Zero);

        Assert.False(pacing.IsPaused);
        Assert.Equal(2d, pacing.SelectedSpeedMultiplier);

        pacing.SelectSpeed(10d);
        pacing.Pause();
        ApplicationEventPacingBatchResult pausedResult = eventPacing.ApplyAtBoundary(
            [
                new ApplicationPacingEventNotice(
                    SlowEvent,
                    new ApplicationPacingEventSubjectId("another-subject")),
            ],
            TimeSpan.FromSeconds(5));

        Assert.True(pacing.IsPaused);
        Assert.Equal(10d, pacing.SelectedSpeedMultiplier);
        Assert.False(pausedResult.PacingChanged);
    }

    [Fact]
    public void SlidingGraceIsIndependentForEachStableSubject()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        var eventPacing = new ApplicationEventPacingController(
            pacing,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
            {
                [CombatStarted] = new ApplicationEventPacingAction.Pause(),
            });
        var firstAsset = new ApplicationPacingEventNotice(CombatStarted, PlayerAsset);
        var secondAsset = new ApplicationPacingEventNotice(
            CombatStarted,
            new ApplicationPacingEventSubjectId("ship-18"));

        eventPacing.ApplyAtBoundary([firstAsset], TimeSpan.Zero);
        pacing.SelectSpeed(5d);
        ApplicationEventPacingBatchResult firstRepeat = eventPacing.ApplyAtBoundary(
            [firstAsset],
            TimeSpan.FromSeconds(4));
        ApplicationEventPacingBatchResult mixed = eventPacing.ApplyAtBoundary(
            [firstAsset, secondAsset],
            TimeSpan.FromSeconds(8));

        Assert.Equal(
            ApplicationEventPacingNoticeDisposition.GraceSuppressed,
            Assert.Single(firstRepeat.Notices).Disposition);
        Assert.Equal(
            ApplicationEventPacingNoticeDisposition.GraceSuppressed,
            mixed.Notices[0].Disposition);
        Assert.Equal(
            ApplicationEventPacingNoticeDisposition.Contributed,
            mixed.Notices[1].Disposition);
        Assert.True(pacing.IsPaused);
    }

    [Fact]
    public void ClearingGraceWindowsLetsTheSameOccurrenceKeyContributeAgain()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        var eventPacing = new ApplicationEventPacingController(
            pacing,
            new Dictionary<ApplicationPacingEventCategoryId, ApplicationEventPacingAction>
            {
                [CombatStarted] = new ApplicationEventPacingAction.Pause(),
            });
        var notice = new ApplicationPacingEventNotice(CombatStarted, PlayerAsset);
        eventPacing.ApplyAtBoundary([notice], TimeSpan.FromSeconds(20));
        pacing.SelectSpeed(5d);

        eventPacing.ClearGraceWindows();
        ApplicationEventPacingBatchResult result = eventPacing.ApplyAtBoundary(
            [notice],
            TimeSpan.Zero);

        Assert.True(pacing.IsPaused);
        Assert.Equal(
            ApplicationEventPacingNoticeDisposition.Contributed,
            Assert.Single(result.Notices).Disposition);
    }

    [Fact]
    public void UnsupportedCategoryIsIgnoredWithoutStartingGrace()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        var eventPacing = new ApplicationEventPacingController(
            pacing,
            ApplicationEventPacingPolicies.CreateInitialDefaults());
        var notice = new ApplicationPacingEventNotice(
            new ApplicationPacingEventCategoryId("future-category"),
            new ApplicationPacingEventSubjectId("future-subject"));

        ApplicationEventPacingBatchResult result = eventPacing.ApplyAtBoundary(
            [notice],
            TimeSpan.Zero);

        Assert.False(pacing.IsPaused);
        Assert.Equal(5d, pacing.SelectedSpeedMultiplier);
        Assert.Equal(0, eventPacing.ActiveGraceWindowCount);
        Assert.Equal(
            ApplicationEventPacingNoticeDisposition.Ignored,
            Assert.Single(result.Notices).Disposition);
    }

    private sealed record UnsupportedAction : ApplicationEventPacingAction;
}
