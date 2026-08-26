using GalaxyCommand.GodotClient;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Godot.Tests;

public sealed class ApplicationInputBufferTests
{
    private static readonly CommandSource Player = new(
        CommandSourceKind.Player,
        new CommandSourceId("input-buffer-player"));

    [Fact]
    public void DrainReturnsCapturedGameplayCommandsInOrderAndClearsTheBuffer()
    {
        var buffer = new ApplicationInputBuffer();
        var first = new TestCommand("first");
        var second = new TestCommand("second");

        buffer.EnqueueGameplay(Player, first);
        buffer.EnqueueGameplay(Player, second);

        IReadOnlyList<BufferedApplicationInput> drained = buffer.Drain();

        Assert.Equal(2, drained.Count);
        var firstInput = Assert.IsType<BufferedApplicationInput.Gameplay>(drained[0]);
        var secondInput = Assert.IsType<BufferedApplicationInput.Gameplay>(drained[1]);
        Assert.Same(first, firstInput.Command);
        Assert.Same(second, secondInput.Command);
        Assert.Equal(Player, firstInput.Source);
        Assert.Equal(Player, secondInput.Source);
        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.Drain());
    }

    [Fact]
    public void DrainPreservesTheCaptureOrderAcrossGameplayAndPacingInput()
    {
        var buffer = new ApplicationInputBuffer();
        var command = new TestCommand("move");
        var pause = new ApplicationPacingAction.Pause();

        buffer.EnqueueGameplay(Player, command);
        buffer.EnqueuePacing(pause);

        IReadOnlyList<BufferedApplicationInput> drained = buffer.Drain();

        var gameplay = Assert.IsType<BufferedApplicationInput.Gameplay>(drained[0]);
        var pacing = Assert.IsType<BufferedApplicationInput.Pacing>(drained[1]);
        Assert.Same(command, gameplay.Command);
        Assert.Same(pause, pacing.Action);
    }

    [Fact]
    public void CapturedPauseActionChangesOnlyLocalPacingState()
    {
        var pacing = new ApplicationPacingController();
        var action = new ApplicationPacingAction.Pause();

        action.Apply(pacing);

        Assert.True(pacing.IsPaused);
    }

    [Fact]
    public void CapturedIncreaseSpeedActionStartsPausedPacingAtOne()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        pacing.Pause();
        var action = new ApplicationPacingAction.IncreaseSpeed();

        action.Apply(pacing);

        Assert.False(pacing.IsPaused);
        Assert.Equal(1d, pacing.SelectedSpeedMultiplier);
    }

    [Fact]
    public void CapturedDecreaseSpeedActionPausesAtTheBottomOfTheLadder()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        var action = new ApplicationPacingAction.DecreaseSpeed();

        action.Apply(pacing);

        Assert.True(pacing.IsPaused);
        Assert.Equal(1d, pacing.SelectedSpeedMultiplier);
    }

    [Fact]
    public void CapturedPresetActionSelectsItsConfiguredRunningSpeed()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.Pause();
        var action = new ApplicationPacingAction.SelectSpeed(5d);

        action.Apply(pacing);

        Assert.False(pacing.IsPaused);
        Assert.Equal(5d, pacing.SelectedSpeedMultiplier);
    }

    [Fact]
    public void CapturedUnpauseActionRestoresTheRememberedRunningSpeed()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        pacing.Pause();
        var action = new ApplicationPacingAction.Unpause();

        action.Apply(pacing);

        Assert.False(pacing.IsPaused);
        Assert.Equal(5d, pacing.SelectedSpeedMultiplier);
    }

    private sealed record TestCommand(string Name) : GameplayCommand(Name);
}
