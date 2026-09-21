using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class GameSessionRandomSetupTests
{
    [Fact]
    public void EverySessionSetupConstructorRequiresResolvedRandomRootSeed()
    {
        bool everyConstructorRequiresSeed = typeof(GameSessionSetup)
            .GetConstructors()
            .All(constructor => constructor
                .GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(RandomRootSeed)));

        Assert.True(everyConstructorRequiresSeed);
    }

    [Fact]
    public void SessionSetupRetainsTheResolvedRandomRootSeed()
    {
        RandomRootSeed seed = RandomRootSeed.FromBytes(
            Enumerable.Range(0, RandomRootSeed.ByteCount).Select(value => (byte)value));
        var setup = new GameSessionSetup(
            [],
            [],
            GameSessionTestFixture.Relationships,
            seed,
            factRetentionCapacity: 1);

        Assert.Same(seed, setup.RandomRootSeed);
    }

    [Fact]
    public void SessionSetupRejectsMissingRandomRootSeed()
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() =>
            new GameSessionSetup(
                [],
                [],
                GameSessionTestFixture.Relationships,
                null!,
                factRetentionCapacity: 1));

        Assert.Equal("randomRootSeed", error.ParamName);
    }

    [Fact]
    public void SessionCheckpointCapturesAndRestoresTheExactResolvedRootSeed()
    {
        byte[] bytes = Enumerable.Range(0, RandomRootSeed.ByteCount)
            .Select(value => (byte)value)
            .ToArray();
        var setup = new GameSessionSetup(
            [],
            [],
            GameSessionTestFixture.Relationships,
            RandomRootSeed.FromBytes(bytes),
            factRetentionCapacity: 64);
        var session = new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new ChebyshevLocalTravelTimeEstimator(100)));

        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(
            session.CaptureCheckpoint().Value);
        DeterministicRandomCheckpoint random =
            Assert.IsType<DeterministicRandomCheckpoint>(checkpoint.Random);

        Assert.Equal(bytes, random.RootSeed);
        Assert.Empty(random.Streams);

        GameSession restored = Assert.IsType<GameSession>(
            GameSession.RestoreCheckpoint(checkpoint).Value);
        GameSessionCheckpoint continued = Assert.IsType<GameSessionCheckpoint>(
            restored.CaptureCheckpoint().Value);
        DeterministicRandomCheckpoint continuedRandom =
            Assert.IsType<DeterministicRandomCheckpoint>(continued.Random);

        Assert.Equal(bytes, continuedRandom.RootSeed);
    }

    [Fact]
    public void SessionRestoreRejectsMissingRandomCheckpoint()
    {
        var setup = new GameSessionSetup(
            [],
            [],
            GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed,
            factRetentionCapacity: 64);
        var session = new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new ChebyshevLocalTravelTimeEstimator(100)));
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(
            session.CaptureCheckpoint().Value);

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(
            checkpoint with { Random = null });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random", result.Failure!.Path);
    }

    [Fact]
    public void SessionRestoreRejectsStreamWithoutOwningDomainDeclaration()
    {
        var setup = new GameSessionSetup(
            [],
            [],
            GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed,
            factRetentionCapacity: 64);
        var session = new GameSession(
            setup,
            new DirectLocalNavigationPlanner(
                new ChebyshevLocalTravelTimeEstimator(100)));
        GameSessionCheckpoint checkpoint = Assert.IsType<GameSessionCheckpoint>(
            session.CaptureCheckpoint().Value);
        var random = new DeterministicRandomOwner(GameSessionTestFixture.RootSeed);
        random.RegisterStream(new RandomStreamKey(
            RandomScope.SessionRuntime,
            "combat",
            "engagement",
            42,
            "resolution"));

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(
            checkpoint with { Random = random.CaptureCheckpoint() });

        Assert.False(result.IsSuccess);
        Assert.Equal("$.checkpoint.random.streams[0].key", result.Failure!.Path);
    }
}
