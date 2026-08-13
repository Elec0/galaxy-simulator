using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ActorControlCheckpointTests
{
    private static readonly ShipId Ship = new(3);
    private static readonly ActorController PlayerController = new(
        ActorControllerKind.Player,
        new CommandSourceId("player"));
    private static readonly CommandSource ScriptSource = new(
        CommandSourceKind.Script,
        new CommandSourceId("script:intro"));
    private static readonly ActorOverrideReasonId OverrideReason =
        new("story.intro");

    [Fact]
    public void RestorePreservesOverrideAndContinuesRevision()
    {
        var original = new ActorControlRegistry();
        original.Add(Ship, PlayerController);
        original.BeginOverride(Ship, ScriptSource, OverrideReason);

        ActorControlRegistryCheckpoint checkpoint = original.CaptureCheckpoint();
        CheckpointResult<ActorControlRegistry> result =
            ActorControlRegistry.RestoreCheckpoint(checkpoint);

        Assert.True(result.IsSuccess);
        ActorControlRegistry restored =
            Assert.IsType<ActorControlRegistry>(result.Value);
        Assert.Equal(original.Capture(Ship), restored.Capture(Ship));
        Assert.Equal(
            ActorCommandEligibility.Eligible,
            restored.CheckCommand(Ship, ScriptSource));

        restored.EndOverride(Ship);

        ActorControlSnapshot released = restored.Capture(Ship);
        Assert.Equal(new ActorControlRevision(2), released.Revision);
        Assert.Null(released.TemporaryOverride);
        Assert.Null(released.TemporaryOverrideReason);
    }

    [Fact]
    public void RestoreAcceptsUnorderedActorsAndCanonicalizesCapture()
    {
        var checkpoint = new ActorControlRegistryCheckpoint(
            [
                Actor(new ShipId(9), revision: 0),
                Actor(new ShipId(3), revision: 0),
            ]);

        CheckpointResult<ActorControlRegistry> result =
            ActorControlRegistry.RestoreCheckpoint(checkpoint);

        ActorControlRegistry restored =
            Assert.IsType<ActorControlRegistry>(result.Value);
        Assert.Equal(
            [3UL, 9UL],
            restored.CaptureCheckpoint().Actors.Select(actor => actor!.ShipId.Value));
    }

    [Fact]
    public void RestoreRejectsDuplicateShip()
    {
        var checkpoint = new ActorControlRegistryCheckpoint(
            [Actor(Ship, revision: 0), Actor(Ship, revision: 0)]);

        CheckpointResult<ActorControlRegistry> result =
            ActorControlRegistry.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.control.actors[1].shipId", result.Failure?.Path);
    }

    [Fact]
    public void RestoreRejectsOverrideWithoutReason()
    {
        var checkpoint = new ActorControlRegistryCheckpoint(
            [new ActorControlCheckpoint(
                Ship,
                PlayerController,
                ActorController.FromScriptSource(ScriptSource),
                TemporaryOverrideReason: null,
                new ActorControlRevision(1))]);

        CheckpointResult<ActorControlRegistry> result =
            ActorControlRegistry.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.control.actors[0]", result.Failure?.Path);
    }

    [Fact]
    public void RestoreRejectsRevisionThatDisagreesWithOverrideState()
    {
        var checkpoint = new ActorControlRegistryCheckpoint(
            [new ActorControlCheckpoint(
                Ship,
                PlayerController,
                ActorController.FromScriptSource(ScriptSource),
                OverrideReason,
                new ActorControlRevision(2))]);

        CheckpointResult<ActorControlRegistry> result =
            ActorControlRegistry.RestoreCheckpoint(checkpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.control.actors[0].revision", result.Failure?.Path);
    }

    private static ActorControlCheckpoint Actor(
        ShipId shipId,
        ulong revision) =>
        new(
            shipId,
            PlayerController,
            TemporaryOverride: null,
            TemporaryOverrideReason: null,
            new ActorControlRevision(revision));
}
