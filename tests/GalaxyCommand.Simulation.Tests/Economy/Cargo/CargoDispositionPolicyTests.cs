using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoDispositionPolicyTests
{
    [Fact]
    public void PolicyRetainsExplicitCauseFractionsAndDefensivelyCopiesThem()
    {
        var causes = Causes();
        var policy = Create(causes: causes);
        causes[CargoLossCause.EntityDestruction] = new CargoRecoveryFraction(0, 1);

        Assert.Equal(new CargoRecoveryFraction(1, 2), policy.RecoveryFor(CargoLossCause.EntityDestruction));
        Assert.Equal(new CargoRecoveryFraction(1, 2), policy.RecoveryFor(CargoLossCause.StorageModuleLoss));
        Assert.Equal(new CargoRecoveryFraction(1, 1), policy.RecoveryFor(CargoLossCause.FunctionalEjection));
        Assert.Equal(4, policy.FungibleObjectCount);
        Assert.Equal(10_000UL, policy.ScatterRadiusMeters);
        Assert.Equal(new SimulationDuration(63_072_000_000), policy.Lifetime);
        Assert.Equal(new SimulationDuration(1000), policy.MinimumCollectionDuration);
        Assert.Equal(new SimulationDuration(15000), policy.MaximumCollectionDuration);
        Assert.Equal(new Quantity(100), policy.CollectionStorageCap);
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.RecoveryFor((CargoLossCause)999));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    public void FractionRejectsInvalidExactRatios(ulong numerator, ulong denominator) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CargoRecoveryFraction(numerator, denominator));

    [Theory]
    [InlineData(CargoLossCause.EntityDestruction)]
    [InlineData(CargoLossCause.StorageModuleLoss)]
    [InlineData(CargoLossCause.FunctionalEjection)]
    public void EverySupportedCauseRequiresAnExplicitEntry(CargoLossCause missing)
    {
        var causes = Causes();
        causes.Remove(missing);
        Assert.Throws<ArgumentException>(() => Create(causes: causes));
    }

    [Fact]
    public void RejectsDuplicateUnknownAndLossyFunctionalEjectionCauses()
    {
        Assert.Throws<ArgumentException>(() => Create(causes: Causes().Concat(Causes().Take(1))));
        Assert.Throws<ArgumentException>(() => Create(causes: Causes().Append(new((CargoLossCause)9, new(1, 2)))));
        var causes = Causes();
        causes[CargoLossCause.FunctionalEjection] = new CargoRecoveryFraction(1, 2);
        Assert.Throws<ArgumentException>(() => Create(causes: causes));
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 2, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, 2)]
    public void RejectsUnsupportedBehaviorVersions(int generation, int merge, int ordering, int duration) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(generation: generation, merge: merge, ordering: ordering, duration: duration));

    [Fact]
    public void RejectsInvalidPolicyBounds()
    {
        Assert.Throws<ArgumentException>(() => Create(key: QualifiedContentKey.Create("test", "commodity", "ore")));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(count: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(radius: (1UL << 50) + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(lifetime: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(minimum: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(minimum: 2, maximum: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(cap: 1));
        Assert.Equal(1UL << 50, Create(radius: 1UL << 50).ScatterRadiusMeters);
        Assert.Equal(0UL, Create(radius: 0).ScatterRadiusMeters);
    }

    private static Dictionary<CargoLossCause, CargoRecoveryFraction> Causes() => new()
    {
        [CargoLossCause.EntityDestruction] = new(1, 2),
        [CargoLossCause.StorageModuleLoss] = new(1, 2),
        [CargoLossCause.FunctionalEjection] = new(1, 1),
    };

    private static CargoDispositionPolicy Create(QualifiedContentKey? key = null,
        int generation = 1, int merge = 1, int ordering = 1, int duration = 1,
        IEnumerable<KeyValuePair<CargoLossCause, CargoRecoveryFraction>>? causes = null,
        int count = 4, ulong radius = 10000, ulong lifetime = 63_072_000_000,
        ulong minimum = 1000, ulong maximum = 15000, ulong cap = 100) => new(
            key ?? QualifiedContentKey.Create("test", "cargo-policy", "recovery"), generation, merge, ordering, duration,
            causes ?? Causes(), count, radius, new(lifetime), new(minimum), new(maximum), new(cap));
}
