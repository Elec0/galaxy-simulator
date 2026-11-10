using System.Numerics;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class StationStorageLossSelectionTests
{
    private static readonly PhysicalDefinition Metal = new(QualifiedContentKey.Create("test.stations", "material", "metal"),
        PhysicalHoldingKind.Fungible, new(1));
    private static readonly StationStorageLossPolicy Policy = new(
        QualifiedContentKey.Create("test.stations", "station-storage-loss-policy", "default"), 1, 1, 100);

    [Fact]
    public void RepeatedSmallLossesSettleExactlyAtCutoff()
    {
        Inventory inventory = Store(10_000, 400);
        var first = Evaluate(StationStorageLossWindow.Empty, 50, 9_950, inventory);
        Assert.Empty(first.Selected);
        Assert.Equal(new BigInteger(50), first.Window.AccumulatedDestroyedCapacity);

        var second = Evaluate(first.Window, 50, 9_900, inventory);
        Assert.Equal(new Quantity(4), Assert.IsType<CargoDispositionSelection.Fungible>(Assert.Single(second.Selected)).Quantity);
        Assert.Equal(BigInteger.Zero, second.Window.AccumulatedDestroyedCapacity);
        Assert.Empty(second.Window.OverflowCredits);
    }

    [Fact]
    public void OverflowCreditsAreNotChargedTwiceAtSettlement()
    {
        Inventory full = Store(10_000, 10_000);
        var first = Evaluate(StationStorageLossWindow.Empty, 50, 9_950, full);
        Assert.Equal(new Quantity(50), first.RemovedCapacity);
        Assert.Equal(new BigInteger(50), first.Window.OverflowCredits[Metal.Key]);

        Inventory remaining = Store(10_000, 9_950);
        var second = Evaluate(first.Window, 50, 9_900, remaining);
        Assert.Equal(new Quantity(50), second.RemovedCapacity);
        Assert.Equal(BigInteger.Zero, second.Window.AccumulatedDestroyedCapacity);
    }

    [Fact]
    public void IndivisibleOverflowMayFreeMoreThanTheRequiredCapacity()
    {
        var item = new PhysicalDefinition(QualifiedContentKey.Create("test.stations", "equipment", "large"),
            PhysicalHoldingKind.Discrete, new(1_000));
        var inventory = new Inventory(new(1), Custody(), new(1_000));
        Assert.True(inventory.StoreDiscrete(item, new(7)).IsAccepted);
        var random = new DeterministicRandomOwner(GameSessionTestFixture.RootSeed);
        var result = StationStorageLossSelection.Evaluate(Policy, StationStorageLossWindow.Empty,
            new(1), 1, new(1), new(999), inventory.CaptureCheckpoint(), new([item]), random);

        Assert.Equal(new Quantity(1_000), result.RemovedCapacity);
        Assert.Equal(new BigInteger(1), result.Window.OverflowCredits[item.Key]);
        Assert.Equal(new ItemInstanceId(7), Assert.IsType<CargoDispositionSelection.Discrete>(Assert.Single(result.Selected)).InstanceId);
    }

    [Fact]
    public void SelectionIsReplayableAndDoesNotConsumeRandomStreams()
    {
        var item = new PhysicalDefinition(QualifiedContentKey.Create("test.stations", "equipment", "part"),
            PhysicalHoldingKind.Discrete, new(1));
        var inventory = new Inventory(new(1), Custody(), new(10));
        for (ulong id = 1; id <= 10; id++) Assert.True(inventory.StoreDiscrete(item, new(id)).IsAccepted);
        var random = new DeterministicRandomOwner(GameSessionTestFixture.RootSeed);
        var first = StationStorageLossSelection.Evaluate(Policy, StationStorageLossWindow.Empty,
            new(1), 1, new(5), new(5), inventory.CaptureCheckpoint(), new([item]), random);
        var retry = StationStorageLossSelection.Evaluate(Policy, StationStorageLossWindow.Empty,
            new(1), 1, new(5), new(5), inventory.CaptureCheckpoint(), new([item]), random);

        Assert.Equal(first.Selected, retry.Selected);
        Assert.Equal(5, first.Selected.Count);
        Assert.Empty(random.CaptureCheckpoint().Streams);
        Assert.Equal(10, inventory.CaptureCheckpoint().DiscreteItems.Count);
    }

    [Fact]
    public void HistoricalCreditsCanExceedOneQuantityWithoutOverflow()
    {
        var window = new StationStorageLossWindow(1,
            new Dictionary<QualifiedContentKey, BigInteger> { [Metal.Key] = (BigInteger)ulong.MaxValue + 1 });
        var result = Evaluate(window, 1, 98, Store(100, 1));
        Assert.Empty(result.Selected);
        Assert.Equal(BigInteger.Zero, result.Window.AccumulatedDestroyedCapacity);
        Assert.Empty(result.Window.OverflowCredits);
    }

    private static StationStorageLossSelection Evaluate(StationStorageLossWindow window, ulong lost, ulong surviving, Inventory inventory) =>
        StationStorageLossSelection.Evaluate(Policy, window, new(1), 1, new(lost), new(surviving),
            inventory.CaptureCheckpoint(), new([Metal]), new(GameSessionTestFixture.RootSeed));

    private static Inventory Store(ulong capacity, ulong quantity)
    {
        var inventory = new Inventory(new(1), Custody(), new(capacity));
        Assert.True(inventory.StoreFungible(Metal, new(quantity)).IsAccepted);
        return inventory;
    }

    private static InventoryCustody.PrincipalControlled Custody() => new(
        new InventoryOwnerReference.SessionEntity(new(1)), GameSessionTestFixture.Principal);
}
