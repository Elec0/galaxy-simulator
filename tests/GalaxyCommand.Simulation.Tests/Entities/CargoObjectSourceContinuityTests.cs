using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoObjectSourceContinuityTests
{
    private static readonly PhysicalDefinition Ore = new(QualifiedContentKey.Create("test.continuity", "commodity", "ore"), PhysicalHoldingKind.Fungible, new(2));
    private static readonly PhysicalDefinition Ice = new(QualifiedContentKey.Create("test.continuity", "commodity", "ice"), PhysicalHoldingKind.Fungible, new(3));
    private static readonly PhysicalDefinition Module = new(QualifiedContentKey.Create("test.continuity", "equipment", "module"), PhysicalHoldingKind.Discrete, new(7));
    private static readonly SystemPosition Position = GameSessionTestFixture.Position(0, 0);

    [Theory]
    [InlineData("fabricated-id")]
    [InlineData("equivalent-clone")]
    [InlineData("wrong-definition")]
    [InlineData("wrong-capacity")]
    public void SourceAwarePublicationRejectsAnythingOtherThanSelectedOriginalInstance(string invalid)
    {
        var (owner, source) = Create();
        source.StoreDiscrete(Module, new(9));
        PreparedCargoSourceLoss loss = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Discrete(new(9))], new([Module]), []).Prepared!;
        DiscreteItemInstance original = source.GetDiscrete(new(9))!;
        PhysicalDefinition definition = invalid switch
        {
            "wrong-definition" => new(Ice.Key, PhysicalHoldingKind.Discrete, Module.CapacityCost),
            "wrong-capacity" => new(Module.Key, PhysicalHoldingKind.Discrete, new(8)),
            _ => Module,
        };
        ItemInstanceId item = invalid == "fabricated-id" ? new(10) : original.Id;
        DiscreteItemInstance? retained = invalid == "wrong-capacity" ? original : null;
        Assert.True(owner.TryPrepareCargoObjects([new(Position, definition, new PhysicalTransferSubject.Discrete(item), retained)], out var batch));
        Assert.False(owner.CanCommitCargoObjects(batch!, loss));
        Assert.Same(original, source.GetDiscrete(original.Id));
        Assert.Empty(owner.CaptureCargoObjects());
    }

    [Theory]
    [InlineData("unselected-type")]
    [InlineData("wrong-cost")]
    [InlineData("too-many-units")]
    [InlineData("aggregate-overflow")]
    public void SourceAwarePublicationBoundsEveryFungibleShareBySelectedDefinitionAndQuantity(string invalid)
    {
        var (owner, source) = Create();
        source.StoreFungible(Ore, new(10));
        PreparedCargoSourceLoss loss = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(5))], new([Ore]), []).Prepared!;
        PhysicalDefinition definition = invalid switch
        {
            "unselected-type" => Ice,
            "wrong-cost" => new(Ore.Key, PhysicalHoldingKind.Fungible, new(3)),
            _ => Ore,
        };
        ulong quantity = invalid == "too-many-units" ? 6UL : 3UL;
        CargoObjectSeed seed = new(Position, definition, new PhysicalTransferSubject.Fungible(definition.Key, new(quantity)));
        CargoObjectSeed[] seeds = invalid == "aggregate-overflow" ? [seed, seed] : [seed];
        Assert.True(owner.TryPrepareCargoObjects(seeds, out var batch));
        Assert.False(owner.CanCommitCargoObjects(batch!, loss));
        Assert.Equal(new Quantity(10), source.FungibleStored(Ore.Key));
        Assert.Empty(owner.CaptureCargoObjects());
    }

    [Fact]
    public void SelectedOriginalsAndConservedFungibleSubsetsRemainValidBeforeAndAfterSourceRemoval()
    {
        var (owner, source) = Create();
        source.StoreFungible(Ore, new(10));
        source.StoreDiscrete(Module, new(9));
        DiscreteItemInstance original = source.GetDiscrete(new(9))!;
        PreparedCargoSourceLoss loss = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(5)), new PhysicalTransferSubject.Discrete(new(9))], new([Ore, Module]), []).Prepared!;
        Assert.True(owner.TryPrepareCargoObjects([
            new(Position, Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(2))),
            new(Position, Ore, new PhysicalTransferSubject.Fungible(Ore.Key, new(3))),
            new(Position, Module, new PhysicalTransferSubject.Discrete(original.Id), original)], out var batch));
        Assert.True(owner.CanCommitCargoObjects(batch!, loss));
        Assert.True(loss.Apply());
        Assert.True(owner.CanCommitCargoObjects(batch!));
        owner.ApplyPreparedCargoObjects(batch!);
        Assert.Same(original, owner.Inventories.Get(batch!.Objects[2].Object.InventoryId)!.GetDiscrete(original.Id));
        Assert.Equal(new Quantity(5), source.FungibleStored(Ore.Key));
    }

    [Fact]
    public void EmptyPublicationIsValidWhenAllSelectedCargoIsDestroyed()
    {
        var (owner, source) = Create();
        source.StoreFungible(Ore, new(1));
        PreparedCargoSourceLoss loss = CargoSourceLoss.Prepare(source,
            [new PhysicalTransferSubject.Fungible(Ore.Key, new(1))], new([Ore]), []).Prepared!;
        Assert.True(owner.TryPrepareCargoObjects([], out var batch));
        Assert.True(owner.CanCommitCargoObjects(batch!, loss));
    }

    private static (EntityLifecycleOwner Owner, Inventory Source) Create()
    {
        var owner = new EntityLifecycleOwner(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);
        var design = new ShipDesign(new(1), "Source", new ConstructionRecipe([], new Work(1)), new(100),
            GameSessionTestFixture.ManeuverCapability, sensorRadiusUnits: 10000, pickupRangeUnits: 5000);
        owner.RegisterSetup([new InitialShipSetup(new(1), new(1), new(1), new(1), design, Position, GameSessionTestFixture.PlayerController)]);
        return (owner, owner.Inventories.Get(new(1))!);
    }
}
