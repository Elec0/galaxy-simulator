using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CargoVisibleObservationTests
{
    private static readonly PhysicalDefinition Cargo = new(QualifiedContentKey.Create("test.cargo", "equipment", "sensor"),
        PhysicalHoldingKind.Discrete, new(1));
    private static readonly SensorDeployableDefinition Satellite = new(QualifiedContentKey.Create("test.cargo", "sensor-deployable", "satellite"),
        "Satellite", new(1), 2500, 10);

    [Fact]
    public void CargoVisibilityUsesOnlyPlayerOwnedSourcesAndRetainsNoStaleObject()
    {
        using GameSession session = Create();
        CargoDispositionReceipt receipt = Dispose(session, 1, 1, 1, [1, 2, 3]);
        EntityId id = Assert.Single(receipt.ObjectIds);
        CargoObjectView original = Assert.Single(session.CaptureVisibleCargo());
        Assert.Equal(id, original.EntityId);
        MovePlayer(session, 10);
        Assert.Single(session.CaptureVisibleCargo());
        MovePlayer(session, 100);
        Assert.Empty(session.CaptureVisibleCargo());
        // The NPC ship remains at the cargo position, and the player is still
        // inside pickup range. Neither creates a player map contact.
        Assert.Single(session.CaptureCargoAuthority());
        MovePlayer(session, 0);
        Assert.Equal(original, Assert.Single(session.CaptureVisibleCargo()));
    }

    [Fact]
    public void OwnedDeployedSensorAddsCoverageAndItsRemovalClearsCargoImmediately()
    {
        using GameSession session = Create(withSatellite: true);
        Dispose(session, 1, 1, 1, [1, 2, 3]);
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new DeploySensorCommand(new(1), new(4), GameSessionTestFixture.Position(0, 0), OrderPlacement.ReplaceAll)).Result.Status);
        session.AdvanceTo(session.CurrentTime);
        EntityId satellite = Assert.Single(session.CaptureSnapshot().DeployedSensors).EntityId;
        MovePlayer(session, 100);
        Assert.Single(session.CaptureVisibleCargo());
        session.RemoveEntity(new EntityRemovalRequest(satellite, EntityRemovalReason.Destroyed, EntityCargoDisposition.DiscardCargo));
        Assert.Empty(session.CaptureVisibleCargo());
    }

    [Fact]
    public void HiddenMergeDoesNotReplaceVisibleObjectsOriginalOccurrenceHandles()
    {
        using GameSession session = Create();
        CargoDispositionReceipt first = Dispose(session, 1, 1, 1, [1, 2, 3]);
        MovePlayer(session, 100);
        Assert.Empty(session.CaptureVisibleCargo());
        CargoDispositionReceipt second = Dispose(session, 2, 2, 2, [4, 5, 6]);
        Assert.Empty(session.CaptureVisibleCargo());
        Assert.NotEqual(first.FieldHandle, second.FieldHandle);
        MovePlayer(session, 0);
        IReadOnlyList<CargoObjectView> visible = session.CaptureVisibleCargo();
        Assert.Equal(first.FieldHandle, Assert.Single(visible, view => view.EntityId == first.ObjectIds[0]).OriginalFieldId);
        Assert.Equal(second.FieldHandle, Assert.Single(visible, view => view.EntityId == second.ObjectIds[0]).OriginalFieldId);
        Assert.Single(session.CaptureCheckpoint().Value!.Cargo!.Fields.Fields);
    }

    [Fact]
    public void SessionWithoutCargoPolicyHasNoVisibleCargo()
    {
        using GameSession session = GameSessionTestFixture.Create();
        Assert.Empty(session.CaptureVisibleCargo());
    }

    [Fact]
    public void FieldInspectorRetainsOnlyCoveredOriginalGroupsWithoutCanonicalIdentity()
    {
        using GameSession session = Create();
        CargoDispositionReceipt first = Dispose(session, 1, 1, 1, [1, 2, 3]);
        MovePlayer(session, 100);
        CargoDispositionReceipt second = Dispose(session, 2, 2, 2, [4, 5, 6]);
        Assert.Empty(session.CaptureVisibleCargoField(first.FieldHandle!.Value));
        MovePlayer(session, 0);
        IReadOnlyList<CargoObjectView> members = session.CaptureVisibleCargoField(first.FieldHandle.Value);
        Assert.Equal(first.ObjectIds[0], Assert.Single(members).EntityId);
        Assert.Equal(first.FieldHandle, members[0].OriginalFieldId);
        CargoObjectView secondMember = Assert.Single(session.CaptureVisibleCargoField(second.FieldHandle!.Value));
        Assert.Equal(second.ObjectIds[0], secondMember.EntityId);
        Assert.Equal(second.FieldHandle, secondMember.OriginalFieldId);
        Assert.Empty(session.CaptureVisibleCargoField(new(999)));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.CaptureVisibleCargoField(default));
    }

    private static CargoDispositionReceipt Dispose(GameSession session, ulong source, ulong inventory, ulong command, ulong[] items)
    {
        var request = new CargoDispositionRequest(new(new CargoDispositionTrigger.Command(new(command)), new(source), 0),
            new(inventory), GameSessionTestFixture.Position(0, 0), CargoLossCause.StorageModuleLoss, Policy(),
            items.Select(item => (CargoDispositionSelection)new CargoDispositionSelection.Discrete(Cargo.Key, new(item))).ToArray(), []);
        CargoDispositionCommitResult result = session.CommitCargoDisposition(request);
        Assert.True(result.WasApplied, result.Failure?.ToString());
        return result.Receipt!;
    }

    private static void MovePlayer(GameSession session, long x)
    {
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(x, 0), OrderPlacement.ReplaceAll)).Result.Status);
        GameSessionTestFixture.AdvanceUntilOrderTerminal(session);
    }

    private static GameSession Create(bool withSatellite = false)
    {
        var design = new ShipDesign(new(1), "Test", new ConstructionRecipe([], new Work(1)), new(100),
            GameSessionTestFixture.ManeuverCapability, sensorRadiusUnits: 10, pickupRangeUnits: 5000);
        var relationships = new RelationshipSetup(GameSessionTestFixture.Relationships.Principals.Append(
            new PrincipalDefinition(new(2), new PrincipalContentId("other"), "Other")), new(1), GameSessionTestFixture.StandingPolicy, []);
        var other = new ActorController(ActorControllerKind.Autonomous, new CommandSourceId("other"));
        PhysicalDefinition[] playerCargo = withSatellite ? [Cargo, Cargo, Cargo, Satellite.Physical] : [Cargo, Cargo, Cargo];
        var setup = new GameSessionSetup([new StarSystem(new(1), "System")],
            [new InitialShipSetup(new(1), new(1), new(1), new(1), design, GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController, cargo: playerCargo),
             new InitialShipSetup(new(2), new(2), new(2), new(2), design, GameSessionTestFixture.Position(0, 0), other,
                cargo: [Cargo, Cargo, Cargo])],
            new ConnectorTopology([], []), [], null, relationships, GameSessionTestFixture.RootSeed, 100,
            GameSessionTestFixture.Player, sensorDeployableDefinitions: withSatellite ? [Satellite] : [], cargoPolicy: Policy());
        var session = new GameSession(setup, new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1)));
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(new(1), GameSessionTestFixture.Destination(0, 0), OrderPlacement.ReplaceAll)).Result.Status);
        return session;
    }

    private static CargoDispositionPolicy Policy() => new(QualifiedContentKey.Create("test.cargo", "cargo-policy", "initial"),
        1, 1, 1, 1, [new(CargoLossCause.EntityDestruction, new(1, 2)), new(CargoLossCause.StorageModuleLoss, new(1, 2)),
            new(CargoLossCause.FunctionalEjection, new(1, 1))], 4, 0, new(63_072_000_000), new(1000), new(15000), new(100));
}
