using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoObjectObservationTests
{
    private static readonly QualifiedContentKey Ore = QualifiedContentKey.Create("test.cargo", "commodity", "ore");
    private static readonly QualifiedContentKey Ice = QualifiedContentKey.Create("test.cargo", "commodity", "ice");

    [Fact]
    public void ProjectionIncludesOnlyExplicitCoveredObjectsAndCopiesItsContainers()
    {
        CargoObjectView first = View(1, Ore, 0);
        CargoObjectView second = View(2, Ice, 1);
        var source = new List<CargoObjectView> { second, first };
        var covered = new HashSet<EntityId> { new(1) };
        IReadOnlyList<CargoObjectView> projected = CargoObjectObservation.ProjectCovered(source, covered);
        source.Clear();
        covered.Clear();
        Assert.Equal(first, Assert.Single(projected));
        Assert.Empty(CargoObjectObservation.ProjectCovered([first, second], new HashSet<EntityId>()));
        Assert.Equal(new EntityId[] { new(1), new(2) },
            CargoObjectObservation.ProjectCovered([second, first], new HashSet<EntityId> { new(1), new(2) }).Select(view => view.EntityId));
    }

    [Fact]
    public void ViewRejectsInvalidContentsAndPreservesOriginalDiscreteReference()
    {
        var original = new DiscreteItemInstance(new(99), Ore);
        var view = new CargoObjectView(new(1), Position(0), Ore, PhysicalHoldingKind.Discrete,
            new(1), new(3), new(3), original, new(2));
        Assert.Same(original, view.DiscreteItem);
        Assert.Equal(new DebrisFieldId(2), view.OriginalFieldId);
        Assert.Throws<ArgumentException>(() => new CargoObjectView(new(1), Position(0), Ice,
            PhysicalHoldingKind.Discrete, new(1), new(3), new(3), original, null));
        Assert.Throws<ArgumentException>(() => new CargoObjectView(new(1), Position(0), Ore,
            PhysicalHoldingKind.Discrete, new(2), new(3), new(6), original, null));
        Assert.Throws<ArgumentException>(() => new CargoObjectView(new(1), Position(0), Ore,
            PhysicalHoldingKind.Fungible, new(1), new(3), new(3), original, null));
        Assert.Throws<ArgumentException>(() => new CargoObjectView(new(1), Position(0), Ore,
            PhysicalHoldingKind.Fungible, new(2), new(3), new(5), null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => View(1, Ore, 0, quantity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => View(1, Ore, 0, cost: 0));
        Assert.Throws<OverflowException>(() => new CargoObjectView(new(1), Position(0), Ore,
            PhysicalHoldingKind.Fungible, new(ulong.MaxValue), new(2), new(1), null, null));
    }

    [Fact]
    public void SelectionUsesPriorityThenExactDistanceThenIdentityAndSkipsCargoThatCannotFit()
    {
        CargoObjectView near = View(1, Ore, 1);
        CargoObjectView prioritized = View(2, Ice, 100);
        CargoObjectView expensive = View(3, Ice, 0, cost: 5);
        Assert.Equal(prioritized, CargoFieldCandidateSelector.Select([near, expensive, prioritized], Position(0), new(1), [Ice]));
        Assert.Equal(near, CargoFieldCandidateSelector.Select([prioritized, near], Position(0), new(1), []));
        Assert.Equal(near, CargoFieldCandidateSelector.Select([View(4, Ore, -1), near], Position(0), new(1), []));
        Assert.Null(CargoFieldCandidateSelector.Select([expensive], Position(0), new(4), []));
        Assert.Null(CargoFieldCandidateSelector.Select([near], Position(0), Quantity.Zero, []));
        Assert.Equal(near, CargoFieldCandidateSelector.Select([near], Position(0), new(1), []));
    }

    [Fact]
    public void SelectionIgnoresOtherSystemsAndHandlesWideSquaredDistances()
    {
        const long edge = 1L << 50;
        CargoObjectView far = View(1, Ore, edge);
        CargoObjectView closer = View(2, Ore, edge - 1);
        CargoObjectView elsewhere = new(new(3), new(new(2), new(new(-edge), new(0))), Ore,
            PhysicalHoldingKind.Fungible, new(1), new(1), new(1), null, null);
        Assert.Equal(closer, CargoFieldCandidateSelector.Select([elsewhere, far, closer], Position(-edge), new(1), []));
        Assert.Null(CargoFieldCandidateSelector.Select([elsewhere], Position(0), new(1), []));
    }

    [Fact]
    public void PriorityOrderingPlacesUnlistedTypesAfterAllListedTypes()
    {
        CargoObjectView ore = View(1, Ore, 100);
        CargoObjectView ice = View(2, Ice, 1);
        Assert.Equal(ore, CargoFieldCandidateSelector.Select([ice, ore], Position(0), new(1), [Ore, Ice]));
        Assert.Throws<ArgumentException>(() => CargoFieldCandidateSelector.Select([ore], Position(0), new(1), [Ore, Ore]));
    }

    [Fact]
    public void OwnerReadsCurrentInventoryContentsWithoutRetainingAStaleProjection()
    {
        PhysicalDefinition definition = new(Ore, PhysicalHoldingKind.Fungible, new(2));
        PhysicalDefinition equipment = new(QualifiedContentKey.Create("test.cargo", "equipment", "sensor"), PhysicalHoldingKind.Discrete, new(3));
        var lifecycle = new EntityLifecycleOwner(new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), []);
        var design = new ShipDesign(new(1), "Source", new ConstructionRecipe([], new Work(1)), new(100),
            GameSessionTestFixture.ManeuverCapability, sensorRadiusUnits: 10000, pickupRangeUnits: 5000);
        lifecycle.RegisterSetup([new InitialShipSetup(new(1), new(1), new(1), new(1), design,
            Position(0), GameSessionTestFixture.PlayerController)]);
        Inventory source = lifecycle.Inventories.Get(new(1))!;
        Assert.True(source.StoreFungible(definition, new(6)).IsAccepted);
        CargoDispositionPolicy policy = new(QualifiedContentKey.Create("test.cargo", "cargo-policy", "initial"), 1, 1, 1, 1,
            [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)),
                new(CargoLossCause.FunctionalEjection, new(1, 1))], 1, 0, new(10000), new(1000), new(15000), new(100));
        var owner = new CargoDispositionOwner(lifecycle, new([definition, equipment]), policy,
            new DeterministicRandomOwner(GameSessionTestFixture.RootSeed), new EventAgenda<GameEvent>(), new GameFactStore(100));
        CargoDispositionRequest request = new(new(new CargoDispositionTrigger.Command(new(1)), new(1), 0), new(1), Position(0),
            CargoLossCause.StorageModuleLoss, policy, [new CargoDispositionSelection.Fungible(Ore, new(6))], []);
        Assert.True(owner.Commit(owner.Prepare(request).Prepared!).WasApplied);
        CargoObjectView before = Assert.Single(owner.CaptureObjectViews());
        Assert.Equal(new Quantity(3), before.Quantity);
        Assert.Equal(new Quantity(2), before.UnitCapacityCost);
        Assert.Equal(new Quantity(6), before.OccupiedStorage);
        Assert.NotNull(before.OriginalFieldId);
        CargoObjectSnapshot physical = Assert.Single(lifecycle.CaptureCargoObjects());
        var destination = new Inventory(new(100), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.Facility(new(1)), new(1)), new(100));
        lifecycle.RegisterEconomyInventory(destination);
        Assert.True(lifecycle.Inventories.TransferPhysical(new(physical.InventoryId, destination.Id, definition,
            new PhysicalTransferSubject.Fungible(Ore, new(1)))).IsAccepted);
        Assert.Equal(new Quantity(2), owner.CaptureObjectView(before.EntityId)!.Quantity);
        Assert.Equal(new Quantity(3), before.Quantity);
        Assert.Null(owner.CaptureObjectView(new(999)));

        Assert.True(source.StoreDiscrete(equipment, new(99)).IsAccepted);
        DiscreteItemInstance original = source.GetDiscrete(new(99))!;
        CargoDispositionRequest ejection = request with
        {
            Key = request.Key with { Trigger = new CargoDispositionTrigger.Command(new(2)) },
            Cause = CargoLossCause.FunctionalEjection,
            Selected = [new CargoDispositionSelection.Discrete(equipment.Key, original.Id)],
        };
        EntityId equipmentObject = Assert.Single(owner.Commit(owner.Prepare(ejection).Prepared!).Receipt!.ObjectIds);
        CargoObjectView discrete = owner.CaptureObjectView(equipmentObject)!;
        Assert.Same(original, discrete.DiscreteItem);
        Assert.Equal(new Quantity(3), discrete.OccupiedStorage);
        Assert.Equal(new Quantity(1), discrete.Quantity);
        Assert.Null(discrete.OriginalFieldId);
    }

    private static CargoObjectView View(ulong id, QualifiedContentKey key, long x, ulong quantity = 1, ulong cost = 1) =>
        new(new(id), Position(x), key, PhysicalHoldingKind.Fungible, new(quantity), new(cost), new(checked(quantity * cost)), null, null);

    private static SystemPosition Position(long x) => new(new(1), new(new(x), new(0)));
}
