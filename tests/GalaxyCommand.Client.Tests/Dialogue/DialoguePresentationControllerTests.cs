using GalaxyCommand.Client;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client.Tests;

public sealed class DialoguePresentationControllerTests
{
    [Fact]
    public void FirstNewActiveConversationOpensInCreationOrderAndOthersRemainPending()
    {
        var pacing = new ApplicationPacingController();
        var controller = new DialoguePresentationController(pacing, true);

        Assert.True(controller.Update([Entry(3), Entry(1, suspended: true), Entry(2)]));

        Assert.Equal(Id(2), controller.ForegroundConversationId);
        Assert.Equal(new[] { Id(1), Id(3) }, controller.PendingConversations.Select(entry => entry.ConversationId));
        Assert.True(pacing.IsPaused);
    }

    [Fact]
    public void ClosingOrCompletingForegroundDoesNotAutomaticallyOpenEarlierPendingConversations()
    {
        var controller = new DialoguePresentationController(new ApplicationPacingController(), true);
        controller.Update([Entry(1), Entry(2)]);

        Assert.True(controller.Close());
        Assert.False(controller.Update([Entry(1, suspended: true), Entry(2)]));
        Assert.Null(controller.ForegroundConversationId);
        Assert.True(controller.Open(Id(2)));
        Assert.True(controller.Update([Entry(1, suspended: true)]));
        Assert.Null(controller.ForegroundConversationId);
        Assert.False(controller.Update([Entry(1)]));
        Assert.Null(controller.ForegroundConversationId);
    }

    [Fact]
    public void PendingConversationsNeverAcquirePauseAndExplicitOpeningUsesPreference()
    {
        var pacing = new ApplicationPacingController();
        var controller = new DialoguePresentationController(pacing, true);
        controller.Update([Entry(1, responseRequired: false), Entry(2)]);

        Assert.False(pacing.IsPaused);
        controller.Close();
        Assert.True(controller.Open(Id(2)));
        Assert.True(pacing.IsPaused);
        controller.Close();
        Assert.False(pacing.IsPaused);

        var disabled = new DialoguePresentationController(pacing, false);
        disabled.Update([Entry(1)]);
        Assert.False(pacing.IsPaused);
    }

    [Fact]
    public void SameConversationAcrossNodesDoesNotReacquirePauseAfterManualOverride()
    {
        var pacing = new ApplicationPacingController();
        var controller = new DialoguePresentationController(pacing, true);
        controller.Update([Entry(1)]);
        pacing.SelectSpeed(5d);

        Assert.False(controller.Update([Entry(1)]));
        Assert.False(controller.Open(Id(1)));
        Assert.False(pacing.IsPaused);
        controller.Close();
        Assert.False(pacing.IsPaused);
        Assert.Equal(5d, pacing.SelectedSpeedMultiplier);
    }

    [Fact]
    public void ForegroundRemovalReleasesOnlyItsOwnAutomaticPause()
    {
        var pacing = new ApplicationPacingController();
        var controller = new DialoguePresentationController(pacing, true);
        controller.Update([Entry(1)]);
        Assert.True(controller.Update([]));
        Assert.False(pacing.IsPaused);
        Assert.Null(controller.ForegroundConversationId);

        controller.Update([Entry(2)]);
        pacing.Pause();
        controller.Update([]);
        Assert.True(pacing.IsPaused);
    }

    [Fact]
    public void LaterNewConversationMayOpenAfterEarlierConversationWasClosed()
    {
        var controller = new DialoguePresentationController(new ApplicationPacingController(), true);
        controller.Update([Entry(1), Entry(2)]);
        controller.Close();

        Assert.True(controller.Update([Entry(3), Entry(2), Entry(1, suspended: true)]));
        Assert.Equal(Id(3), controller.ForegroundConversationId);
    }

    [Fact]
    public void InvalidSnapshotsRejectBeforeChangingForegroundPendingOrderOrPause()
    {
        var pacing = new ApplicationPacingController();
        var controller = new DialoguePresentationController(pacing, true);
        controller.Update([Entry(1), Entry(2)]);

        Assert.Throws<ArgumentException>(() => controller.Update([Entry(3), Entry(3)]));
        Assert.Throws<ArgumentException>(() => controller.Update([null!]));
        Assert.Throws<ArgumentNullException>(() => controller.Update(null!));
        Assert.Equal(Id(1), controller.ForegroundConversationId);
        Assert.Equal(Id(2), Assert.Single(controller.PendingConversations).ConversationId);
        Assert.True(pacing.IsPaused);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialoguePresentationEntry(default, false, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Open(default));
    }

    [Fact]
    public void OpeningUnknownConversationAndClosingEmptyForegroundLeaveStateUnchanged()
    {
        var controller = new DialoguePresentationController(new ApplicationPacingController(), true);

        Assert.False(controller.Open(Id(1)));
        Assert.False(controller.Close());
        Assert.Null(controller.ForegroundConversationId);
        Assert.Empty(controller.PendingConversations);
    }

    [Fact]
    public void SuspendedConversationMustBeResumedBeforeItCanAcquireForegroundAndPause()
    {
        var pacing = new ApplicationPacingController();
        var controller = new DialoguePresentationController(pacing, true);
        controller.Update([Entry(1, suspended: true)]);

        Assert.False(controller.Open(Id(1)));
        Assert.False(pacing.IsPaused);
        Assert.False(controller.Update([Entry(1)]));
        Assert.Null(controller.ForegroundConversationId);
        Assert.True(controller.Open(Id(1)));
        Assert.True(pacing.IsPaused);
        Assert.True(controller.Update([Entry(1, suspended: true)]));
        Assert.False(pacing.IsPaused);
        Assert.Null(controller.ForegroundConversationId);
    }

    [Fact]
    public void InputAndPreviouslyPublishedPendingCollectionsRemainIndependent()
    {
        var controller = new DialoguePresentationController(new ApplicationPacingController(), true);
        DialoguePresentationEntry[] input = [Entry(1), Entry(2)];
        controller.Update(input);
        IReadOnlyList<DialoguePresentationEntry> pending = controller.PendingConversations;
        input[1] = Entry(99);

        Assert.Equal(Id(2), Assert.Single(controller.PendingConversations).ConversationId);
        controller.Open(Id(2));
        Assert.Equal(Id(2), Assert.Single(pending).ConversationId);
        Assert.Equal(Id(1), Assert.Single(controller.PendingConversations).ConversationId);
    }

    [Fact]
    public void ClosingBeforeSessionReplacementReleasesPauseAndRestoredForegroundUsesCurrentPreference()
    {
        var pacing = new ApplicationPacingController();
        pacing.SelectSpeed(5d);
        var old = new DialoguePresentationController(pacing, true);
        old.Update([Entry(10)]);

        old.Close();
        Assert.False(pacing.IsPaused);
        Assert.Equal(5d, pacing.SelectedSpeedMultiplier);
        var restored = new DialoguePresentationController(pacing, true);
        Assert.True(restored.Update([Entry(1)]));
        Assert.True(pacing.IsPaused);
        Assert.Equal(5d, pacing.SelectedSpeedMultiplier);

        pacing.SelectSpeed(2d);
        restored.Close();
        Assert.False(pacing.IsPaused);
        Assert.Equal(2d, pacing.SelectedSpeedMultiplier);
    }

    [Fact]
    public void CompletedInformationalOutputRetainsForegroundButReleasesOnlyItsOwnPause()
    {
        var pacing = new ApplicationPacingController();
        var controller = new DialoguePresentationController(pacing, true);
        controller.Update([Entry(1)]);

        Assert.False(controller.Update([new DialoguePresentationEntry(Id(1), false, false, isCompleted: true)]));
        Assert.Equal(Id(1), controller.ForegroundConversationId);
        Assert.False(pacing.IsPaused);
        controller.Close();
        controller.Update([Entry(2)]);
        pacing.Pause();
        controller.Update([new DialoguePresentationEntry(Id(2), false, false, isCompleted: true)]);
        Assert.True(pacing.IsPaused);
    }

    private static DialogueConversationId Id(ulong value) => new(value);
    private static DialoguePresentationEntry Entry(ulong id, bool suspended = false, bool responseRequired = true) =>
        new(Id(id), suspended, responseRequired);
}
