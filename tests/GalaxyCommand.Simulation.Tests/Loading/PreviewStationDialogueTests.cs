using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PreviewStationDialogueTests
{
    private static readonly SaveJsonLimits Limits = new(16_777_216, 128, 65_536, 100_000);
    private static readonly GameSessionSaveMetadata Metadata = new(
        Guid.Parse("bd3fcda3-dd3b-4e05-b516-730c4ff091cc"), DateTimeOffset.UnixEpoch, "preview-station-dialogue-test");

    [Fact]
    public void AuthoredPreviewStationsStartOperationalWithEmptyOwnedInventoriesAndRestoreWithoutReseeding()
    {
        GameSessionSetup setup = Load();
        using GameSession original = Create(setup);
        GameSnapshot world = original.CaptureSnapshot();

        Assert.Equal(3, world.Stations.Count);
        Assert.Equal("Aster Relay", Definition(setup, Station(original, "aster-relay")).InvariantFallback);
        Assert.Equal("Borealis Harbor", Definition(setup, Station(original, "borealis-harbor")).InvariantFallback);
        Assert.Equal("Cinder Beacon", Definition(setup, Station(original, "cinder-beacon")).InvariantFallback);
        Assert.Empty(original.CommandRecords);
        Assert.Empty(original.ReadFactsAfter(null, 2048).Facts.Select(envelope => envelope.Fact).OfType<StationCreatedFact>());
        GameSessionCheckpoint before = Capture(original);
        Assert.Equal(3, before.Stations!.Setup.InitialStations.Count);
        Assert.Empty(before.Stations.Receipts);
        foreach (StationSnapshot station in world.Stations)
        {
            Assert.Equal(StationPhase.Operational, station.Phase);
            Assert.Equal(setup.Relationships.PlayerPrincipalId, station.PrincipalId);
            Assert.Empty(station.Modules);
            Assert.Empty(station.Projects);
            InitialStationSetup origin = Assert.Single(before.Stations.Setup.InitialStations, seed => seed.Id == station.Id);
            Assert.Equal(station.EntityId, origin.EntityId);
            Assert.Equal(station.InventoryId, origin.InventoryId);
            Assert.Equal(station.DefinitionKey, origin.DefinitionKey);
            InventoryCheckpoint inventory = Inventory(before, station);
            Assert.Equal(Definition(setup, station).BaseCapacity, inventory.Capacity);
            var custody = Assert.IsType<InventoryCustody.PrincipalControlled>(inventory.Custody);
            Assert.Equal(station.PrincipalId, custody.ControllingPrincipalId);
            Assert.Equal(station.EntityId, Assert.IsType<InventoryOwnerReference.SessionEntity>(custody.PhysicalOwner).EntityId);
            AssertEmptyInventory(inventory);
        }

        using GameSession restored = RoundTrip(original, setup);

        Assert.Equal(3, restored.CaptureSnapshot().Stations.Count);
        AssertSameAuthority(original, restored);
        foreach (StationSnapshot station in restored.CaptureSnapshot().Stations)
        {
            InventoryCheckpoint inventory = Inventory(Capture(restored), station);
            Assert.Equal(Inventory(before, station).Capacity, inventory.Capacity);
            Assert.Equal(Inventory(before, station).Custody, inventory.Custody);
            AssertEmptyInventory(inventory);
        }
    }

    [Fact]
    public void PreviewGreetingRemembersIntroductionOnlyForTheBoundStation()
    {
        GameSessionSetup setup = Load();
        using GameSession session = Create(setup);
        StationSnapshot relay = Station(session, "aster-relay");
        StationSnapshot harbor = Station(session, "borealis-harbor");

        DialogueConversationSnapshot first = Start(session, setup, "station-greeting", relay);
        Assert.Contains(first.Choices, choice => choice.Id == "introduce");
        Assert.DoesNotContain(first.Choices, choice => choice.Id == "return-greeting");
        Select(session, setup, first.Id, "introduce");
        Assert.Equal("introduction", Assert.Single(session.CaptureDialogue()).NodeId);
        Select(session, setup, first.Id, "introduction-ack");
        Assert.Empty(session.CaptureDialogue());

        DialogueConversationSnapshot again = Start(session, setup, "station-greeting", relay);
        Assert.DoesNotContain(again.Choices, choice => choice.Id == "introduce");
        Assert.Contains(again.Choices, choice => choice.Id == "return-greeting");
        Select(session, setup, again.Id, "return-greeting");
        Select(session, setup, again.Id, "return-ack");

        DialogueConversationSnapshot other = Start(session, setup, "station-greeting", harbor);
        Assert.Contains(other.Choices, choice => choice.Id == "introduce");
        Assert.DoesNotContain(other.Choices, choice => choice.Id == "return-greeting");
        DialogueMemoryReceipt memory = Assert.Single(Capture(session).Dialogue!.Memories);
        Assert.Equal(DialogueMemoryScope.ParticipantBinding, memory.Scope);
        Assert.Equal(relay.Id, Assert.IsType<DialogueParticipant.Station>(Assert.Single(memory.Bindings!.Items).Participant).Id);
    }

    [Fact]
    public void PreviewWelcomeRejectsRepeatWhileActiveAndAfterRestoreButAllowsAnotherStation()
    {
        GameSessionSetup setup = Load();
        using GameSession original = Create(setup);
        StationSnapshot relay = Station(original, "aster-relay");
        StationSnapshot harbor = Station(original, "borealis-harbor");
        DialogueConversationSnapshot welcome = Start(original, setup, "station-welcome", relay);
        Assert.Equal(CommandResultStatus.Rejected, original.SubmitCommand(setup.PlayerCommandSource,
            StartCommand("station-welcome", relay)).Result.Status);
        Assert.Equal(welcome.Id, Assert.Single(original.CaptureDialogue()).Id);

        using GameSession restored = RoundTrip(original, setup);

        Assert.Equal(CommandResultStatus.Rejected, restored.SubmitCommand(setup.PlayerCommandSource,
            StartCommand("station-welcome", relay)).Result.Status);
        Select(restored, setup, welcome.Id, "welcome-ack");
        Assert.Empty(restored.CaptureDialogue());
        Assert.Equal(CommandResultStatus.Rejected, restored.SubmitCommand(setup.PlayerCommandSource,
            StartCommand("station-welcome", relay)).Result.Status);
        DialogueConversationSnapshot second = Start(restored, setup, "station-welcome", harbor);
        Assert.NotEqual(welcome.Id, second.Id);
        Assert.Equal(harbor.Id, Assert.IsType<DialogueParticipant.Station>(Assert.Single(second.Bindings.Items).Participant).Id);
    }

    [Fact]
    public void PreviewBulletinCompletesThroughFactsWithoutRequiringAChoice()
    {
        GameSessionSetup setup = Load();
        using GameSession session = Create(setup);
        StationSnapshot beacon = Station(session, "cinder-beacon");
        GameplayCommandRecord started = session.SubmitCommand(setup.PlayerCommandSource, StartCommand("channel-bulletin", beacon));

        Assert.Equal(CommandResultStatus.Accepted, started.Result.Status);
        Assert.Empty(session.CaptureDialogue());
        GameFact[] facts = session.ReadFactsAfter(null, 2048).Facts.Select(envelope => envelope.Fact).ToArray();
        Assert.Single(facts.OfType<DialogueStartedFact>());
        DialogueEndedFact ended = Assert.Single(facts.OfType<DialogueEndedFact>());
        Assert.Equal(DialogueEndReason.Completed, ended.Reason);
        Assert.Equal("bulletin-delivered", ended.OutcomeId);
        Assert.Empty(facts.OfType<DialogueChoiceCommittedFact>());
    }

    [Fact]
    public void PreviewNavigationMovesBoundShipOnceAndRestoresTheSeparateAcknowledgement()
    {
        GameSessionSetup setup = Load();
        using GameSession original = Create(setup);
        StationSnapshot relay = Station(original, "aster-relay");
        InitialShipSetup actor = Assert.Single(setup.Ships, ship => ship.Position.SystemId == relay.Position.SystemId);
        GameplayCommandRecord start = original.SubmitCommand(setup.PlayerCommandSource,
            new StartDialogueCommand(Key("station-navigation"),
                [new("ship", new DialogueParticipant.Ship(actor.Id)), new("station", new DialogueParticipant.Station(relay.Id))]));
        Assert.Equal(CommandResultStatus.Accepted, start.Result.Status);
        DialogueConversationId conversation = Assert.Single(original.CaptureDialogue()).Id;

        GameplayCommandRecord selected = Select(original, setup, conversation, "set-course");

        Assert.Equal(2, original.CommandRecords.Count);
        Assert.Equal("acknowledgement", Assert.Single(original.CaptureDialogue()).NodeId);
        ShipOrderSnapshot order = original.CaptureSnapshot().Ships.Single(ship => ship.Id == actor.Id).CurrentOrder!;
        Assert.Equal(relay.EntityId, Assert.IsType<NavigationDestination.Entity>(order.Destination).EntityId);
        GameFactEnvelope committed = Assert.Single(original.ReadFactsAfter(null, 2048).Facts,
            envelope => envelope.Fact is DialogueChoiceCommittedFact);
        Assert.Equal(selected.Envelope.Sequence, Assert.IsType<CommandFactCause>(committed.Cause).Sequence);
        Assert.Contains(original.ReadFactsAfter(null, 2048).Facts, envelope =>
            envelope.Fact is ShipOrderTransitionFact transition && transition.ShipId == actor.Id && envelope.Cause == committed.Cause);
        IdSequenceCheckpoint orderIds = Capture(original).Orders.OrderIds;

        using GameSession restored = RoundTrip(original, setup);

        Assert.Equal(conversation, Assert.Single(restored.CaptureDialogue()).Id);
        Assert.Equal("acknowledgement", Assert.Single(restored.CaptureDialogue()).NodeId);
        Assert.Equal(order.Id, restored.CaptureSnapshot().Ships.Single(ship => ship.Id == actor.Id).CurrentOrder!.Id);
        foreach (GameSession session in new[] { original, restored })
        {
            GameSessionTestFixture.AdvanceUntilOrderTerminal(session, actor.Id);
            Select(session, setup, conversation, "navigation-ack");
            Assert.Empty(session.CaptureDialogue());
            Assert.Equal(orderIds, Capture(session).Orders.OrderIds);
            Assert.Equal(ShipOrderStatus.Completed, session.CaptureSnapshot().Ships.Single(ship => ship.Id == actor.Id).CurrentOrder!.Status);
        }
        AssertSameAuthority(original, restored);
    }

    private static GameSessionSetup Load()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "GalaxyCommand.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        string package = Path.Combine(repository.FullName, "content", "development", "galaxy-command.preview");
        StaticNewGameLoadResult loaded = StaticNewGameLoader.Load([package], PackageId.Create("galaxy-command.preview"),
            LocalContentId.Create("visual-preview"), GameSessionTestFixture.RootSeed, 2048, 2);
        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Diagnostics));
        return loaded.Setup!;
    }

    private static GameSession Create(GameSessionSetup setup) => new(setup,
        new HierarchicalNavigationPlanner(setup.ConnectorTopology, new ChebyshevLocalTravelTimeEstimator(10)));

    private static StationSnapshot Station(GameSession session, string localId) =>
        Assert.Single(session.CaptureSnapshot().Stations, station => station.DefinitionKey == QualifiedContentKey.Create("galaxy-command.preview", "station", localId));

    private static StationDefinition Definition(GameSessionSetup setup, StationSnapshot station) =>
        Assert.Single(setup.Stations!.Definitions.Stations, definition => definition.Key == station.DefinitionKey);

    private static InventoryCheckpoint Inventory(GameSessionCheckpoint checkpoint, StationSnapshot station) =>
        Assert.Single(checkpoint.Lifecycle.Inventories.Inventories, inventory => inventory.Id == station.InventoryId);

    private static QualifiedContentKey Key(string localId) => QualifiedContentKey.Create("galaxy-command.preview", "dialogue", localId);

    private static StartDialogueCommand StartCommand(string definition, StationSnapshot station) =>
        new(Key(definition), [new("station", new DialogueParticipant.Station(station.Id))]);

    private static DialogueConversationSnapshot Start(GameSession session, GameSessionSetup setup, string definition, StationSnapshot station)
    {
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(setup.PlayerCommandSource, StartCommand(definition, station)).Result.Status);
        return Assert.Single(session.CaptureDialogue());
    }

    private static GameplayCommandRecord Select(GameSession session, GameSessionSetup setup, DialogueConversationId conversation, string choice)
    {
        GameplayCommandRecord selected = session.SubmitCommand(setup.PlayerCommandSource, new SelectDialogueChoiceCommand(conversation, choice));
        Assert.Equal(CommandResultStatus.Accepted, selected.Result.Status);
        return selected;
    }

    private static void AssertEmptyInventory(InventoryCheckpoint inventory)
    {
        Assert.Empty(inventory.StoredMaterials);
        Assert.Empty(inventory.FungibleHoldings);
        Assert.Empty(inventory.DiscreteItems);
        Assert.Empty(inventory.Reservations);
        Assert.Empty(inventory.CapacityReservations);
        Assert.Empty(inventory.PhysicalReservations);
        Assert.Empty(inventory.IncomingAdmissions);
        Assert.Empty(inventory.OutgoingAdmissions);
    }

    /// <summary>Uses actual preview content dependencies and bounded bytes, preserving origin rather than replaying scenario setup.</summary>
    private static GameSession RoundTrip(GameSession session, GameSessionSetup setup)
    {
        using var bytes = new MemoryStream();
        SaveWriteResult written = SaveJsonCodec.Write(bytes, Encode(session), Limits);
        Assert.True(written.IsSuccess, written.Failure?.Message);
        bytes.Position = 0;
        SaveReadResult decoded = SaveJsonCodec.Read(bytes, Limits, new SaveMigrationRegistry(SaveFormat.CurrentSchemaVersion, []));
        Assert.True(decoded.IsSuccess, decoded.Failure?.Message);
        CheckpointResult<GameSession> restored = GameSessionSaveCodec.Restore(decoded.Document!, Limits,
            definitions: new PhysicalDefinitionCatalog(setup.SensorDeployableDefinitions.Select(definition => definition.Physical)),
            materialCompatibility: setup.Stations!.Materials, dialogueDefinitions: setup.DialogueDefinitions);
        Assert.True(restored.IsSuccess, restored.Failure?.Path + ": " + restored.Failure?.Message);
        AssertSameAuthority(session, restored.Value!);
        return restored.Value!;
    }

    private static InertSaveDocument Encode(GameSession session)
    {
        CheckpointResult<InertSaveDocument> result = GameSessionSaveCodec.Encode(Capture(session), Metadata, Limits);
        Assert.True(result.IsSuccess, result.Failure?.Path + ": " + result.Failure?.Message);
        return result.Value!;
    }

    private static GameSessionCheckpoint Capture(GameSession session)
    {
        CheckpointResult<GameSessionCheckpoint> capture = session.CaptureCheckpoint();
        Assert.True(capture.IsSuccess, capture.Failure?.Path + ": " + capture.Failure?.Message);
        return capture.Value!;
    }

    private static void AssertSameAuthority(GameSession expected, GameSession actual) =>
        Assert.Equal(Encode(expected).Root.GetRawText(), Encode(actual).Root.GetRawText());
}
