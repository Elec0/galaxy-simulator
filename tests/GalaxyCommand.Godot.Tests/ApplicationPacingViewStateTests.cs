using GalaxyCommand.GodotClient;

namespace GalaxyCommand.Godot.Tests;

public sealed class ApplicationPacingViewStateTests
{
    [Fact]
    public void CreateReflectsTheCurrentLocalPacingStateAndConfiguredLadder()
    {
        var pacing = new ApplicationPacingController([1d, 2d, 5d]);
        pacing.SelectSpeed(5d);
        pacing.Pause();

        ApplicationPacingViewState state = ApplicationPacingViewState.Create(pacing);

        Assert.True(state.IsPaused);
        Assert.Equal(5d, state.SelectedSpeedMultiplier);
        Assert.Equal([1d, 2d, 5d], state.RunningSpeedMultipliers);
    }
}
