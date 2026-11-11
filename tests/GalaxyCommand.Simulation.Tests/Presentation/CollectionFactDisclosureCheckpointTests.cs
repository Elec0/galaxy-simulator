using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class CollectionFactDisclosureCheckpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecodedStandingDisclosureRequiresEveryScalarAndContribution(bool changed)
    {
        using GameSession session = GameSessionFogOfWarTests.Create(5_000);
        session.CommitStandingChanges(new(new(StandingChangeSourceKind.Explicit, 1), [
            new(new(2), new(1), new(new(1), -40, StandingChangeReason.Explicit)),
        ]));
        GameSessionCheckpoint checkpoint = Decode(session.CaptureCheckpoint().Value!);
        if (changed)
        {
            GameFactEnvelope envelope = Assert.Single(checkpoint.Facts.RetainedFacts, item => item!.Fact is StandingChangedFact)!;
            StandingChangedFact fact = Assert.IsType<StandingChangedFact>(envelope.PlayerDisclosure!.Fact);
            var altered = new StandingChangedFact(new(fact.AssessingPrincipalId, fact.SubjectPrincipalId, fact.PriorValue,
                fact.PriorBand, fact.CombinedDelta, fact.ResultingValue, fact.ResultingBand,
                fact.Contributions.Select(item => new StandingChangeContribution(new(999), item.Delta, item.Reason))));
            checkpoint = WithDisclosure(checkpoint, envelope, altered);
        }

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint);

        Assert.Equal(!changed, result.IsSuccess);
        if (changed) Assert.Equal("$.checkpoint.playerObservation", result.Failure!.Path);
        result.Value?.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecodedCargoFieldOrderDisclosureRequiresExactPriorityOrder(bool changed)
    {
        using GameSession session = StationDestructionSessionTests.Create();
        StationSnapshot station = StationDestructionSessionTests.Build(session);
        QualifiedContentKey metal = QualifiedContentKey.Create("test.station-loss", "material", "metal");
        Assert.True(Assert.Single(session.CommitInventoryMutations([new StoreFungibleInventoryProposal(
            new(InventoryOperationSourceKind.Explicit, 1), station.InventoryId,
            new PhysicalDefinition(metal, PhysicalHoldingKind.Fungible, new(1)), new(400))]).Outcomes).WasApplied);
        Assert.True(session.CommitStationDestruction(StationDestructionSessionTests.Request(session, station, destroyBase: false)).WasApplied);
        CargoObjectView cargo = Assert.Single(session.CaptureVisibleCargo());
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new CollectCargoFieldCommand(new(1), cargo.OriginalFieldId!.Value, [metal], OrderPlacement.ReplaceAll)).Result.Status);
        GameSessionCheckpoint checkpoint = Decode(session.CaptureCheckpoint().Value!);
        if (changed)
        {
            GameFactEnvelope envelope = checkpoint.Facts.RetainedFacts.First(item =>
                item!.Fact is ShipOrderTransitionFact { Action: ShipActionIntent.CollectField })!;
            ShipOrderTransitionFact fact = Assert.IsType<ShipOrderTransitionFact>(envelope.PlayerDisclosure!.Fact);
            var action = Assert.IsType<ShipActionIntent.CollectField>(fact.Action);
            var altered = new ShipActionIntent.CollectField(action.OriginCommandSequence, action.CargoInventoryId,
                action.TargetFieldId, action.LastObservedObjectEntityId, action.LastObservedPosition, []);
            checkpoint = WithDisclosure(checkpoint, envelope, new ShipOrderTransitionFact(fact.ShipId, fact.OrderId,
                fact.Source, fact.Destination, fact.PreviousStatus, fact.NextStatus, fact.Reason, fact.RequestedHeading, altered));
        }

        CheckpointResult<GameSession> result = GameSession.RestoreCheckpoint(checkpoint, new PhysicalDefinitionCatalog([
            new PhysicalDefinition(metal, PhysicalHoldingKind.Fungible, new(1)),
        ]));

        Assert.Equal(!changed, result.IsSuccess);
        if (changed) Assert.Equal("$.checkpoint.playerObservation", result.Failure!.Path);
        result.Value?.Dispose();
    }

    private static GameSessionCheckpoint Decode(GameSessionCheckpoint checkpoint)
    {
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);
        return codec.Decode<GameSessionCheckpoint>(codec.Encode(checkpoint));
    }

    private static GameSessionCheckpoint WithDisclosure(GameSessionCheckpoint checkpoint, GameFactEnvelope source, GameFact fact) => checkpoint with
    {
        Facts = new(checkpoint.Facts.Capacity, checkpoint.Facts.Sequences, checkpoint.Facts.RetainedFacts.Select(envelope =>
            envelope == source ? source with { PlayerDisclosure = source.PlayerDisclosure! with { Fact = fact } } : envelope)),
    };
}
