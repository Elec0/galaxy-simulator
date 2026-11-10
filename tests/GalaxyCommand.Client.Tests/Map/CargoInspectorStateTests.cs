using GalaxyCommand.Client;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client.Tests;

public sealed class CargoInspectorStateTests
{
    private static readonly CommandSource Player = new(CommandSourceKind.Player, new("player"));
    private static readonly QualifiedContentKey Ore = QualifiedContentKey.Create("test.cargo", "commodity", "ore");
    private static readonly QualifiedContentKey Ice = QualifiedContentKey.Create("test.cargo", "commodity", "ice");

    [Fact]
    public void InspectorRequiresRegisteredPrincipalAndPlayerCommandSource()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CargoInspectorState(default, Player));
        Assert.Throws<ArgumentException>(() => new CargoInspectorState(new(1), new(CommandSourceKind.Autonomous, new("npc"))));
    }

    [Fact]
    public void CargoSelectionDoesNotChangeFocusedShipAndRefreshDropsHiddenDetails()
    {
        var state = Create();
        GameObservedShipSnapshot focused = Ship();
        CargoObjectView first = Cargo(10, new(2));
        state.Refresh([first]);
        Assert.True(state.Select(first.EntityId));
        Assert.Same(first, state.SelectedCargo);
        Assert.Equal(first.EntityId, state.SelectedCargoId);
        Assert.False(state.Select(new(99)));
        Assert.Same(first, state.SelectedCargo);
        state.AddPriority(Ore);
        state.Refresh([]);
        Assert.Null(state.SelectedCargo);
        Assert.Null(state.SelectedCargoId);
        Assert.Null(state.CreateObjectCommand(focused, OrderPlacement.ReplaceAll));
        Assert.Equal(new[] { Ore }, state.PriorityDraft);
        Assert.Equal(new ShipId(1), focused.Id);
        state.Refresh([first]);
        Assert.Null(state.SelectedCargo);
        Assert.True(state.Select(first.EntityId));
    }

    [Fact]
    public void PriorityDraftIsUniqueOrderedEditableAndIndependentFromEarlierSnapshots()
    {
        var state = Create();
        Assert.Empty(state.PriorityDraft);
        Assert.True(state.AddPriority(Ore));
        Assert.True(state.AddPriority(Ice));
        IReadOnlyList<QualifiedContentKey> original = state.PriorityDraft;
        Assert.False(state.AddPriority(Ore));
        Assert.True(state.MovePriorityUp(Ice));
        Assert.Equal(new[] { Ice, Ore }, state.PriorityDraft);
        Assert.False(state.MovePriorityUp(Ice));
        Assert.True(state.MovePriorityDown(Ice));
        Assert.False(state.MovePriorityDown(Ice));
        Assert.True(state.RemovePriority(Ore));
        Assert.False(state.RemovePriority(Ore));
        Assert.False(state.MovePriorityUp(Ore));
        Assert.Equal(new[] { Ice }, state.PriorityDraft);
        Assert.Equal(new[] { Ore, Ice }, original);
    }

    [Fact]
    public void ObjectAndFieldCommandsUseOnlyTheFocusedAuthorizedShipAndCaptureExplicitPlacementAndDraft()
    {
        var state = Create();
        state.Refresh([Cargo(10, new(5))]);
        Assert.True(state.Select(new(10)));
        state.AddPriority(Ore);
        state.AddPriority(Ice);
        CollectCargoObjectCommand objectCommand = Assert.IsType<CollectCargoObjectCommand>(
            state.CreateObjectCommand(Ship(), OrderPlacement.Append));
        Assert.Equal(new ShipId(1), objectCommand.ShipId);
        Assert.Equal(new EntityId(10), objectCommand.TargetEntityId);
        Assert.Equal(OrderPlacement.Append, objectCommand.Placement);
        CollectCargoFieldCommand fieldCommand = Assert.IsType<CollectCargoFieldCommand>(
            state.CreateFieldCommand(Ship(), OrderPlacement.ReplaceAll));
        Assert.Equal(new ShipId(1), fieldCommand.ShipId);
        Assert.Equal(new DebrisFieldId(5), fieldCommand.TargetFieldId);
        Assert.Equal(OrderPlacement.ReplaceAll, fieldCommand.Placement);
        state.RemovePriority(Ore);
        Assert.Equal(new[] { Ore, Ice }, fieldCommand.Priorities);
        state.Refresh([Cargo(11, null)]);
        state.Select(new(11));
        Assert.Null(state.CreateFieldCommand(Ship(), OrderPlacement.ReplaceAll));
        Assert.NotNull(state.CreateObjectCommand(Ship(), OrderPlacement.ReplaceAll));
    }

    [Fact]
    public void UnavailableUnownedOrDifferentlyControlledShipsCannotIssueInspectorCommands()
    {
        var state = Create();
        state.Refresh([Cargo(10, new(5))]);
        state.Select(new(10));
        GameObservedShipSnapshot[] unavailable =
        [
            Ship() with { OwnedDetail = null },
            Ship() with { PrincipalId = new(2) },
            Ship(controller: new(ActorControllerKind.Autonomous, new("npc"))),
            Ship(controller: new(ActorControllerKind.Player, new("other"))),
            Ship(controller: new(ActorControllerKind.Script, new("script"))),
        ];
        foreach (GameObservedShipSnapshot focused in unavailable)
        {
            Assert.Null(state.CreateObjectCommand(focused, OrderPlacement.ReplaceAll));
            Assert.Null(state.CreateFieldCommand(focused, OrderPlacement.ReplaceAll));
            Assert.Null(state.CreateCancelCommand(focused));
        }
        Assert.Null(state.CreateObjectCommand(null, OrderPlacement.Append));
        Assert.Null(state.CreateFieldCommand(null, OrderPlacement.Append));
    }

    [Fact]
    public void CancellationUsesOnlyFocusedCurrentOrderEvenWithoutVisibleCargo()
    {
        var state = Create();
        CancelShipOrderCommand cancel = Assert.IsType<CancelShipOrderCommand>(state.CreateCancelCommand(Ship()));
        Assert.Equal(new ShipId(1), cancel.ShipId);
        Assert.Equal(new ShipOrderId(7), cancel.OrderId);
        foreach (ShipOrderStatus status in new[] { ShipOrderStatus.Completed, ShipOrderStatus.Cancelled, ShipOrderStatus.Failed })
            Assert.Null(state.CreateCancelCommand(Ship(status: status)));
        Assert.Null(state.CreateCancelCommand(null));
    }

    [Fact]
    public void CommandsDrainThroughExistingApplicationFifoWithoutLaterDraftChanges()
    {
        var state = Create();
        state.Refresh([Cargo(10, new(5))]);
        state.Select(new(10));
        state.AddPriority(Ore);
        var input = new ApplicationInputBuffer();
        GameplayCommand first = state.CreateFieldCommand(Ship(), OrderPlacement.Append)!;
        input.EnqueueGameplay(Player, first);
        input.EnqueuePacing(new ApplicationPacingAction.Pause());
        GameplayCommand cancel = state.CreateCancelCommand(Ship())!;
        input.EnqueueGameplay(Player, cancel);
        state.AddPriority(Ice);
        IReadOnlyList<BufferedApplicationInput> drained = input.Drain();
        Assert.Same(first, Assert.IsType<BufferedApplicationInput.Gameplay>(drained[0]).Command);
        Assert.IsType<BufferedApplicationInput.Pacing>(drained[1]);
        Assert.Same(cancel, Assert.IsType<BufferedApplicationInput.Gameplay>(drained[2]).Command);
        Assert.Equal(new[] { Ore }, Assert.IsType<CollectCargoFieldCommand>(first).Priorities);
    }

    [Fact]
    public void RefreshCopiesVisibleContainersAndRejectsDuplicateIdentityBeforeChangingSelection()
    {
        var state = Create();
        CargoObjectView cargo = Cargo(10, null);
        var views = new List<CargoObjectView> { cargo };
        state.Refresh(views);
        state.Select(cargo.EntityId);
        views.Clear();
        Assert.Same(cargo, Assert.Single(state.VisibleCargo));
        Assert.Throws<ArgumentException>(() => state.Refresh([cargo, cargo]));
        Assert.Same(cargo, state.SelectedCargo);
    }

    private static CargoInspectorState Create() => new(new(1), Player);
    private static CargoObjectView Cargo(ulong id, DebrisFieldId? field) => new(new(id), Position(), Ore,
        PhysicalHoldingKind.Fungible, new(2), new(1), new(2), null, field);
    private static SystemPosition Position() => new(new(1), new(new(0), new(0)));
    private static GameObservedShipSnapshot Ship(ActorController? controller = null, ShipOrderStatus status = ShipOrderStatus.Active)
    {
        var active = controller ?? new(ActorControllerKind.Player, Player.Id);
        var control = new ActorControlSnapshot(active, active, null, null, new(0));
        var order = new ShipOrderSnapshot(new(7), Player, new NavigationDestination.Position(Position()), status, ShipOrderReason.MovingToDestination);
        var detail = new GameShipSnapshot(new(1), new(1), new(1), new(1), new(1), new(100),
            new ShipSpatialSnapshotState.AtPosition(Position()), control, order, [], []);
        return new(new(1), new(1), new(1), new(1), Position(), ShipVelocity.Zero, ShipHeading.Zero, detail);
    }
}
