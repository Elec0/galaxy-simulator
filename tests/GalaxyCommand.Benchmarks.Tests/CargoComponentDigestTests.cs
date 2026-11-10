using GalaxyCommand.Benchmarks;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks.Tests;

public sealed class CargoComponentDigestTests
{
    private static readonly QualifiedContentKey Key = QualifiedContentKey.Create("fixture", "commodity", "ore");
    private static readonly CommandSource Source = new(CommandSourceKind.Autonomous, new("fixture"));
    private static readonly SystemPosition Anchor = new(new(1), new(new(0), new(0)));

    [Fact]
    public void CausalRequestDigestDistinguishesSelectedAmountsAndCleanupAuthorization()
    {
        var policy = new CargoDispositionPolicy(QualifiedContentKey.Create("fixture", "cargo-policy", "policy"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)), new(CargoLossCause.FunctionalEjection, new(1, 1))],
            1, 0, new(1000), new(1), new(100), new(10));
        var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1), Anchor,
            CargoLossCause.StorageModuleLoss, policy, [new CargoDispositionSelection.Fungible(Key, new(4))], []);
        string original = Digest(digest => CargoComponentBenchmarkScenario.AddRequest(digest, request));
        Assert.NotEqual(original, Digest(digest => CargoComponentBenchmarkScenario.AddRequest(digest,
            request with { Selected = [new CargoDispositionSelection.Fungible(Key, new(5))] })));
        var cleanup = request with { Cleanup = [new CargoSourceReservationCleanup.Physical(new(1), new ReservationOwner.ProductionJob(new(1)))] };
        Assert.NotEqual(original, Digest(digest => CargoComponentBenchmarkScenario.AddRequest(digest, cleanup)));
        Assert.NotEqual(Digest(digest => CargoComponentBenchmarkScenario.AddRequest(digest, cleanup)),
            Digest(digest => CargoComponentBenchmarkScenario.AddRequest(digest, cleanup with
            { Cleanup = [new CargoSourceReservationCleanup.Physical(new(1), new ReservationOwner.ProductionJob(new(2)))] })));
    }

    [Fact]
    public void AcceptedCommandAndTransitionPayloadsAndDisclosureAffectDigest()
    {
        var accepted = Envelope(new CommandAcceptedFact(new(1), Source, "cargo.collect-object"));
        Assert.NotEqual(FactDigest(accepted), FactDigest(Envelope(new CommandAcceptedFact(new(1), Source, "ship.move"))));
        var transition = Envelope(new ShipOrderTransitionFact(new(1), new(1), Source, new NavigationDestination.Position(Anchor),
            null, ShipOrderStatus.Active, ShipOrderReason.CollectingCargo, action: new ShipActionIntent.CollectObject(new(1), new(1), new(2), Anchor)));
        var changed = Envelope(new ShipOrderTransitionFact(new(1), new(1), Source, new NavigationDestination.Position(Anchor),
            null, ShipOrderStatus.Active, ShipOrderReason.CollectingCargo, action: new ShipActionIntent.CollectObject(new(1), new(1), new(3), Anchor)));
        Assert.NotEqual(FactDigest(transition), FactDigest(changed));
        Assert.NotEqual(FactDigest(accepted), FactDigest(accepted with { PlayerDisclosure = new(new(1), null) }));
        Assert.NotEqual(FactDigest(accepted with { PlayerDisclosure = new(new(1), null) }),
            FactDigest(accepted with { PlayerDisclosure = new(new(2), null) }));
    }

    [Fact]
    public void RandomRootDerivationAndEveryStreamStateWordAffectDigest()
    {
        byte[] seed = new byte[32];
        var stream = new RandomStreamCheckpoint(new(RandomScope.SessionRuntime, "cargo", "ship", 1, "fixture"), "xoshiro", 1, 1, 2, 3, 4, 5);
        var original = new DeterministicRandomCheckpoint(seed, "derive", 1, [stream]);
        string expected = RandomDigest(original);
        byte[] changedSeed = (byte[])seed.Clone(); changedSeed[0] = 1;
        Assert.NotEqual(expected, RandomDigest(new(changedSeed, "derive", 1, [stream])));
        Assert.NotEqual(expected, RandomDigest(new(seed, "derive", 2, [stream])));
        foreach (RandomStreamCheckpoint changed in new[] { stream with { S0 = 9 }, stream with { S1 = 9 }, stream with { S2 = 9 },
            stream with { S3 = 9 }, stream with { NextPosition = 9 }, stream with { GeneratorVersion = 2 } })
            Assert.NotEqual(expected, RandomDigest(new(seed, "derive", 1, [changed])));
    }

    [Fact]
    public void DiscreteAppearanceHashesExactItemIdentityAndDefinition()
    {
        static CargoObjectView View(QualifiedContentKey key, ulong item) => new(new(1), Anchor, key,
            PhysicalHoldingKind.Discrete, new(1), new(1), new(1), new(new(item), key), new(1));
        string original = FactDigest(Envelope(new CargoObjectAppearedFact(View(Key, 1))));
        Assert.NotEqual(original, FactDigest(Envelope(new CargoObjectAppearedFact(View(Key, 2)))));
        Assert.NotEqual(original, FactDigest(Envelope(new CargoObjectAppearedFact(View(
            QualifiedContentKey.Create("fixture", "equipment", "other"), 1)))));
    }

    [Fact]
    public void UnexpectedFixtureFactRejectsRatherThanOmittingPayload()
    {
        Assert.Throws<InvalidOperationException>(() => FactDigest(Envelope(
            new CommandRejectedFact(new(1), Source, "unexpected", new("fixture.rejected")))));
    }

    private static GameFactEnvelope Envelope(GameFact fact) => new(new(1), new(0), new CommandFactCause(new(1)), fact);
    private static string FactDigest(GameFactEnvelope fact) => Digest(digest => CargoComponentBenchmarkScenario.AddFact(digest, fact));
    private static string RandomDigest(DeterministicRandomCheckpoint random) => Digest(digest => CargoComponentBenchmarkScenario.AddRandom(digest, random));
    private static string Digest(Action<DeterministicDigest> add) { var digest = new DeterministicDigest(); add(digest); return digest.ToString(); }
}
