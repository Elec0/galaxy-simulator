using System.Collections.Concurrent;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class InventorySessionDeterminismTests
{
    public static TheoryData<int, int> WorkerLayouts => new()
    {
        { 1, 1 },
        { 2, 2 },
        { 4, 3 },
        { 3, 5 },
    };

    [Theory]
    [MemberData(nameof(WorkerLayouts))]
    public void AuthoritativeCommitIsIndependentOfWorkerAndBatchLayout(
        int workerCount,
        int batchSize)
    {
        PhysicalDefinition ore = FungibleDefinition("ore");
        PhysicalDefinition sensor = DiscreteDefinition("sensor");
        InventoryMutationProposal[] proposals = Proposals(ore, sensor);

        CommitEvidence reference = Commit(
            proposals,
            ore,
            sensor);
        CommitEvidence parallel = Commit(
            EvaluateInParallel(proposals, workerCount, batchSize),
            ore,
            sensor);

        Assert.Equal(8, reference.Outcomes.Count);
        Assert.Equal(4, reference.Outcomes.Count(outcome => outcome.WasApplied));
        Assert.Equal(4, reference.InventoryState.Count);
        Assert.Equal<ulong?>(2, reference.ItemAllocator.NextValue);
        Assert.Equal<ulong?>(3, reference.ReservationAllocator.NextValue);
        Assert.Equal(4, reference.Receipts.Count);
        Assert.Empty(reference.Facts);
        Assert.Equal(reference.Outcomes, parallel.Outcomes);
        Assert.Equal(reference.InventoryState, parallel.InventoryState);
        Assert.Equal(reference.ItemAllocator, parallel.ItemAllocator);
        Assert.Equal(reference.ReservationAllocator, parallel.ReservationAllocator);
        Assert.Equal(reference.Receipts, parallel.Receipts);
        Assert.Equal(reference.Facts, parallel.Facts);
    }

    private static CommitEvidence Commit(
        IEnumerable<InventoryMutationProposal> proposals,
        params PhysicalDefinition[] definitions)
    {
        GameSession session = CreateSession();
        InventoryCommitBatchResult result = session.CommitInventoryMutations(proposals);
        CheckpointResult<GameSessionCheckpoint> captured = session.CaptureCheckpoint();
        Assert.True(captured.IsSuccess, captured.Failure?.Message);
        GameSessionCheckpoint checkpoint = captured.Value!;
        InventoryCommitOwnerCheckpoint commit = Assert.IsType<InventoryCommitOwnerCheckpoint>(
            checkpoint.InventoryCommit);
        InventoryCheckpoint cargo = Assert.Single(
            checkpoint.Lifecycle.Inventories.Inventories.Cast<InventoryCheckpoint>());
        var catalog = new PhysicalDefinitionCatalog(definitions);
        Assert.True(
            GameSession.RestoreCheckpoint(checkpoint, catalog).IsSuccess);

        return new CommitEvidence(
            result.Outcomes,
            InventoryState(cargo),
            commit.ItemInstanceIds,
            commit.ReservationIds,
            commit.Receipts.Select(ReceiptState).ToArray(),
            session.ReadFactsAfter(null, 64).Facts);
    }

    /// <summary>
    /// Models worker-private output buffers without allowing workers to mutate
    /// authoritative inventory state or allocate authoritative identities.
    /// </summary>
    private static IEnumerable<InventoryMutationProposal> EvaluateInParallel(
        IReadOnlyList<InventoryMutationProposal> proposals,
        int workerCount,
        int batchSize)
    {
        var buffers = new ConcurrentBag<InventoryMutationProposal[]>();
        InventoryMutationProposal[][] batches = proposals
            .Reverse()
            .Chunk(batchSize)
            .ToArray();
        Parallel.ForEach(
            batches,
            new ParallelOptions { MaxDegreeOfParallelism = workerCount },
            batch => buffers.Add(batch));
        return buffers.SelectMany(batch => batch);
    }

    private static InventoryMutationProposal[] Proposals(
        PhysicalDefinition ore,
        PhysicalDefinition sensor) =>
    [
        new StoreFungibleInventoryProposal(
            Key(8),
            GameSessionTestFixture.CargoInventory,
            ore,
            new Quantity(6)),
        new CreateDiscreteInventoryProposal(
            Key(6),
            GameSessionTestFixture.CargoInventory,
            sensor),
        new ReservePhysicalInventoryProposal(
            Key(7),
            GameSessionTestFixture.CargoInventory,
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(3)),
            Owner(1)),
        new StoreFungibleInventoryProposal(
            Key(9),
            GameSessionTestFixture.CargoInventory,
            ore,
            new Quantity(6)),
        new ReservePhysicalInventoryProposal(
            Key(10),
            GameSessionTestFixture.CargoInventory,
            new PhysicalReservationSubject.Discrete(new ItemInstanceId(1)),
            Owner(2)),
        new ReservePhysicalInventoryProposal(
            Key(11),
            GameSessionTestFixture.CargoInventory,
            new PhysicalReservationSubject.IncomingCapacity(new Quantity(1)),
            Owner(3)),
        new CreateDiscreteInventoryProposal(
            Key(12),
            GameSessionTestFixture.CargoInventory,
            sensor),
        new StoreFungibleInventoryProposal(
            Key(13),
            GameSessionTestFixture.CargoInventory,
            ore,
            new Quantity(1)),
        new StoreFungibleInventoryProposal(
            Key(13),
            GameSessionTestFixture.CargoInventory,
            ore,
            new Quantity(2)),
    ];

    private static GameSession CreateSession()
    {
        var navigation = new DirectLocalNavigationPlanner(
            new ChebyshevLocalTravelTimeEstimator(100));
        var setup = new GameSessionSetup(
            [new StarSystem(GameSessionTestFixture.System, "Test System")],
            [new InitialShipSetup(
                GameSessionTestFixture.Entity,
                GameSessionTestFixture.Ship,
                GameSessionTestFixture.CargoInventory,
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Design,
                GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController)],
            new ConnectorTopology([], []),
            [new ShipMaterializationPolicy(
                new FacilityId(1),
                GameSessionTestFixture.Principal,
                GameSessionTestFixture.Position(0, 0),
                GameSessionTestFixture.PlayerController,
                InitialShipOrderPolicy.NoInitialOrder,
                [GameSessionTestFixture.Design])],
            GameSessionTestFixture.Relationships,
            GameSessionTestFixture.RootSeed,
            factRetentionCapacity: 64);
        return new GameSession(setup, navigation);
    }

    private static PhysicalDefinition FungibleDefinition(string localId) =>
        new(
            QualifiedContentKey.Create("core", "cargo", localId),
            PhysicalHoldingKind.Fungible,
            new Quantity(1));

    private static PhysicalDefinition DiscreteDefinition(string localId) =>
        new(
            QualifiedContentKey.Create("core", "cargo", localId),
            PhysicalHoldingKind.Discrete,
            new Quantity(1));

    private static InventoryOperationKey Key(ulong value) =>
        new(InventoryOperationSourceKind.Explicit, value);

    private static ReservationOwner.ProductionJob Owner(ulong value) =>
        new ReservationOwner.ProductionJob(new ProductionJobId(value));

    private static string[] InventoryState(InventoryCheckpoint checkpoint) =>
    [
        .. checkpoint.FungibleHoldings.Select(holding =>
            $"fungible:{holding.DefinitionKey}:{holding.Quantity.Units}"),
        .. checkpoint.DiscreteItems.Select(item =>
            $"discrete:{item.Id.Value}:{item.DefinitionKey}"),
        .. checkpoint.PhysicalReservations.Select(reservation =>
            $"reservation:{reservation.Id.Value}:{reservation.Subject}:{reservation.Owner}"),
    ];

    private static string ReceiptState(InventoryCommitReceiptCheckpoint receipt) =>
        $"{receipt.Proposal.Key}:{receipt.Proposal}:{receipt.Outcome}";

    private sealed record CommitEvidence(
        IReadOnlyList<InventoryCommitDisposition> Outcomes,
        IReadOnlyList<string> InventoryState,
        IdSequenceCheckpoint ItemAllocator,
        IdSequenceCheckpoint ReservationAllocator,
        IReadOnlyList<string> Receipts,
        IReadOnlyList<GameFactEnvelope> Facts);
}
