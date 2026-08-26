using GalaxyCommand.GodotClient;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Godot.Tests;

public sealed class ApplicationPacingControllerTests
{
    [Fact]
    public void DefaultLadderStartsAtOneAndRetainsEveryAcceptedStep()
    {
        var pacing = new ApplicationPacingController();

        Assert.Equal([1d, 2d, 5d, 10d, 30d], pacing.RunningSpeedMultipliers);
        Assert.Equal(1d, pacing.SelectedSpeedMultiplier);
        Assert.False(pacing.IsPaused);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ConstructorRejectsNonRunningSpeedMultiplier(double invalidMultiplier)
    {
        Assert.Throws<ArgumentException>(() =>
            new ApplicationPacingController([1d, invalidMultiplier]));
    }

    [Fact]
    public void ConstructorRejectsLadderWithoutOneAsItsFirstStep()
    {
        Assert.Throws<ArgumentException>(() =>
            new ApplicationPacingController([2d, 5d]));
    }

    [Fact]
    public void DirectUnpauseRestoresTheRunningSpeedThatPausePreserved()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);

        pacing.Pause();
        pacing.Unpause();

        Assert.False(pacing.IsPaused);
        Assert.Equal(5d, pacing.SelectedSpeedMultiplier);
    }

    [Fact]
    public void IncreasingSpeedFromPauseStartsAtOneAndReplacesRememberedSpeed()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        pacing.Pause();

        pacing.IncreaseSpeed();

        Assert.False(pacing.IsPaused);
        Assert.Equal(1d, pacing.SelectedSpeedMultiplier);
    }

    [Fact]
    public void DecreasingFromOnePausesWithoutChangingTheRememberedSpeed()
    {
        var pacing = new ApplicationPacingController([1d, 2d]);

        pacing.DecreaseSpeed();

        Assert.True(pacing.IsPaused);
        Assert.Equal(1d, pacing.SelectedSpeedMultiplier);
    }

    [Fact]
    public void PausedWallClockTimeDoesNotCreateAdvancementDebt()
    {
        var pacing = new ApplicationPacingController([1d, 2d]);
        pacing.SelectSpeed(2d);

        SimulationTime firstTarget = pacing.Advance(
            SimulationTime.Zero,
            TimeSpan.FromMilliseconds(500));
        pacing.Pause();
        SimulationTime pausedTarget = pacing.Advance(
            firstTarget,
            TimeSpan.FromSeconds(5));
        pacing.Unpause();
        SimulationTime resumedTarget = pacing.Advance(
            pausedTarget,
            TimeSpan.FromMilliseconds(500));

        Assert.Equal(new SimulationTime(1_000), firstTarget);
        Assert.Equal(firstTarget, pausedTarget);
        Assert.Equal(new SimulationTime(2_000), resumedTarget);
    }

    [Fact]
    public void OpeningResponseRequiredDialogueAutomaticallyPausesAndClosingRestoresSpeed()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);

        pacing.OpenResponseRequiredDialogue(
            pauseWhenResponseRequiredDialogueOpens: true);
        Assert.True(pacing.IsPaused);
        pacing.CloseResponseRequiredDialogue();

        Assert.False(pacing.IsPaused);
        Assert.Equal(5d, pacing.SelectedSpeedMultiplier);
    }

    [Fact]
    public void DisabledDialoguePausePreferenceLeavesPacingUnchanged()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);

        pacing.OpenResponseRequiredDialogue(
            pauseWhenResponseRequiredDialogueOpens: false);
        Assert.False(pacing.IsPaused);
        pacing.CloseResponseRequiredDialogue();

        Assert.False(pacing.IsPaused);
        Assert.Equal(5d, pacing.SelectedSpeedMultiplier);
    }

    [Fact]
    public void ManualPauseDuringResponseRequiredDialoguePreventsAutomaticResume()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        pacing.OpenResponseRequiredDialogue(
            pauseWhenResponseRequiredDialogueOpens: true);

        pacing.Pause();
        pacing.CloseResponseRequiredDialogue();

        Assert.True(pacing.IsPaused);
        Assert.Equal(5d, pacing.SelectedSpeedMultiplier);
    }

    [Fact]
    public void ContinuingTheSameDialogueAfterManualPauseDoesNotAcquireAutomaticPauseAgain()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.OpenResponseRequiredDialogue(
            pauseWhenResponseRequiredDialogueOpens: true);
        pacing.Pause();

        pacing.OpenResponseRequiredDialogue(
            pauseWhenResponseRequiredDialogueOpens: true);
        pacing.CloseResponseRequiredDialogue();

        Assert.True(pacing.IsPaused);
    }
}
