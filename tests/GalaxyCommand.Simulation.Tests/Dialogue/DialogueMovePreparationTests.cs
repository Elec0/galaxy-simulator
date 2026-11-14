using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class DialogueMovePreparationTests
{
    [Fact]
    public void PreparingMoveDoesNotAllocateOrdersAgendaOrFacts()
    {
        using ActorOrderRuntimeCoordinator runtime = CreateRuntime(out GameFactStore facts);
        GameSessionRuntimeCheckpoint before = runtime.CaptureCheckpoint(256).Value!;

        var prepared = runtime.PrepareDialogueMove(GameSessionTestFixture.Player, Move());

        Assert.Equal(CommandResultStatus.Accepted, prepared.Result.Status);
        GameSessionRuntimeCheckpoint after = runtime.CaptureCheckpoint(256).Value!;
        Assert.Equal(before.Orders.OrderIds, after.Orders.OrderIds);
        Assert.Equal(before.Engine.Agenda.NextCreationSequence, after.Engine.Agenda.NextCreationSequence);
        Assert.Equal(before.Engine.Agenda.PendingEvents, after.Engine.Agenda.PendingEvents);
        Assert.Null(Assert.Single(runtime.CaptureSnapshot().Ships).CurrentOrder);
        Assert.Empty(facts.ReadAfter(null, 256).Facts);
    }

    [Fact]
    public void PreparedMoveUsesOrdinarySourceAndDestinationValidationWithoutMutation()
    {
        using ActorOrderRuntimeCoordinator runtime = CreateRuntime(out _);
        var otherPlayer = new CommandSource(CommandSourceKind.Player, new CommandSourceId("other-player"));

        var unauthorized = runtime.PrepareDialogueMove(otherPlayer, Move());
        var unreachable = runtime.PrepareDialogueMove(GameSessionTestFixture.Player,
            new MoveShipCommand(GameSessionTestFixture.Ship,
                new NavigationDestination.System(new SystemId(2)), OrderPlacement.ReplaceAll));

        Assert.Equal(CommandRejectionCodes.InvalidSource, unauthorized.Result.RejectionCode);
        Assert.Equal(CommandRejectionCodes.InvalidState, unreachable.Result.RejectionCode);
        Assert.Null(Assert.Single(runtime.CaptureSnapshot().Ships).CurrentOrder);
        Assert.True(runtime.IsHealthy);
    }

    [Fact]
    public void PreparedCommitMatchesNormalMoveAndBuffersFactsWithoutSubmittingAnotherCommand()
    {
        using ActorOrderRuntimeCoordinator preparedRuntime = CreateRuntime(out GameFactStore preparedFacts);
        using ActorOrderRuntimeCoordinator ordinaryRuntime = CreateRuntime(out _);
        var prepared = preparedRuntime.PrepareDialogueMove(GameSessionTestFixture.Player, Move());

        IReadOnlyList<GameFactProposal> proposals = preparedRuntime.CommitDialogueMove(prepared);
        GameplayCommandHandlingResult ordinary = ordinaryRuntime.Handle(new GameplayCommandEnvelope(
            new CommandSequence(1), SimulationTime.Zero, GameSessionTestFixture.Player, Move()));

        Assert.Equal(Assert.Single(ordinaryRuntime.CaptureSnapshot().Ships).CurrentOrder,
            Assert.Single(preparedRuntime.CaptureSnapshot().Ships).CurrentOrder);
        Assert.Equal(ordinary.FactProposals.Select(proposal => proposal.Key), proposals.Select(proposal => proposal.Key));
        Assert.Equal(ordinary.FactProposals.Select(proposal => proposal.Fact), proposals.Select(proposal => proposal.Fact));
        Assert.Empty(preparedFacts.ReadAfter(null, 256).Facts);
        Assert.True(preparedRuntime.IsHealthy);
    }

    [Fact]
    public void PreparedMoveCannotBeCommittedTwiceAndInvariantFailurePoisonsRuntime()
    {
        using ActorOrderRuntimeCoordinator runtime = CreateRuntime(out _);
        var prepared = runtime.PrepareDialogueMove(GameSessionTestFixture.Player, Move());
        runtime.CommitDialogueMove(prepared);

        Assert.Throws<InvalidOperationException>(() => runtime.CommitDialogueMove(prepared));
        Assert.False(runtime.IsHealthy);
        Assert.False(runtime.CaptureCheckpoint(256).IsSuccess);
    }

    [Fact]
    public void ForeignAndRejectedPreparedMovesCannotMutateRuntime()
    {
        using ActorOrderRuntimeCoordinator first = CreateRuntime(out _);
        using ActorOrderRuntimeCoordinator second = CreateRuntime(out _);
        var prepared = first.PrepareDialogueMove(GameSessionTestFixture.Player, Move());

        Assert.Throws<InvalidOperationException>(() => second.CommitDialogueMove(prepared));
        Assert.Null(Assert.Single(second.CaptureSnapshot().Ships).CurrentOrder);
        Assert.False(second.IsHealthy);
        Assert.True(first.IsHealthy);

        var rejected = first.PrepareDialogueMove(new CommandSource(CommandSourceKind.Player, new CommandSourceId("other")), Move());
        Assert.Throws<InvalidOperationException>(() => first.CommitDialogueMove(rejected));
        Assert.Null(Assert.Single(first.CaptureSnapshot().Ships).CurrentOrder);
        Assert.False(first.IsHealthy);
    }

    [Fact]
    public void OuterDialogueCommitFailureCanPoisonRuntimeAndBlockCheckpoint()
    {
        using ActorOrderRuntimeCoordinator runtime = CreateRuntime(out _);

        runtime.PoisonDialogueCommit();

        Assert.False(runtime.IsHealthy);
        Assert.Throws<InvalidOperationException>(() => runtime.ThrowIfUnhealthy());
        Assert.False(runtime.CaptureCheckpoint(256).IsSuccess);
    }

    private static MoveShipCommand Move() => new(GameSessionTestFixture.Ship,
        GameSessionTestFixture.Destination(100, 0), OrderPlacement.ReplaceAll);

    private static ActorOrderRuntimeCoordinator CreateRuntime(out GameFactStore facts)
    {
        facts = new GameFactStore(256);
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [new InitialShipSetup(GameSessionTestFixture.Entity, GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory, GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design, GameSessionTestFixture.Position(0, 0), GameSessionTestFixture.PlayerController)],
            GameSessionTestFixture.Relationships, GameSessionTestFixture.RootSeed, factRetentionCapacity: 256,
            playerCommandSource: GameSessionTestFixture.Player);
        return new ActorOrderRuntimeCoordinator(setup,
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(100)), facts);
    }
}
