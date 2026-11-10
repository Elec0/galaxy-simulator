using System.Diagnostics;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

internal sealed partial class StationRuntimeBenchmarkScenario
{
    /// <summary>Owns only benchmark orchestration; session authority remains behind commands and typed checkpoints.</summary>
    private sealed class StationRun : IDisposable
    {
        private readonly ResolvedBenchmarkScenario _configuration;
        private readonly InteractionBatchExecution? _execution;
        private readonly List<GameEventRecord> _beforeRestore = [];
        private ulong _inventoryOperation = 1;
        internal StationRun(ResolvedBenchmarkScenario configuration, GameSessionSetup setup)
        {
            _configuration = configuration;
            int workers = configuration.GetInt32(BenchmarkParameterNames.WorkerCount);
            _execution = workers == 0 ? null : new(workers, configuration.GetInt32(BenchmarkParameterNames.BatchSize));
            var navigation = new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(1));
            Session = _execution is null ? new(setup, navigation) : new(setup, navigation, _execution);
        }

        internal GameSession Session { get; private set; }
        internal int Restores { get; private set; }
        internal int Commands { get; private set; }
        internal double RestoreMilliseconds { get; private set; }
        internal long RestoreAllocated { get; private set; }
        internal GameplayCommandRecord Submit(GameplayCommand command) => Submit(OwnerSource, command);
        internal GameplayCommandRecord SubmitPlayer(GameplayCommand command) => Submit(PlayerSource, command);

        /// <summary>Records admitted command volume independently of the disposable per-session command diagnostic log.</summary>
        private GameplayCommandRecord Submit(CommandSource source, GameplayCommand command)
        {
            GameplayCommandRecord applied = Session.SubmitCommand(source, command);
            if (applied.Result.Status != CommandResultStatus.Accepted)
                throw new InvalidOperationException($"Station benchmark {command.Kind}: {applied.Result.RejectionCode} {applied.Result.Reason}");
            Commands++;
            return applied;
        }

        /// <summary>Commits one complete deterministic delivery batch using the existing generalized inventory boundary.</summary>
        internal void Store(IEnumerable<StationSnapshot> stations, ulong amount)
        {
            InventoryMutationProposal[] proposals = stations.Select(station => (InventoryMutationProposal)new StoreFungibleInventoryProposal(
                new(InventoryOperationSourceKind.Explicit, _inventoryOperation++), station.InventoryId, MaterialDefinition, new(amount))).ToArray();
            InventoryCommitBatchResult result = Session.CommitInventoryMutations(proposals);
            if (result.Outcomes.Any(outcome => outcome.Outcome is not InventoryMutationOutcome.StoredFungible || !outcome.WasApplied))
                throw new InvalidOperationException("A station benchmark material delivery was not applied.");
        }

        /// <summary>Partitions only external advancement and restores at an explicit interior boundary without replaying gameplay input.</summary>
        internal void AdvanceTo(SimulationTime through)
        {
            ulong duration = _configuration.GetUInt64(BenchmarkParameterNames.SimulatedDurationMilliseconds);
            int partitions = _configuration.GetInt32(BenchmarkParameterNames.AdvancePartitions);
            ulong restore = _configuration.GetUInt64(BenchmarkParameterNames.RestoreAtMilliseconds);
            var boundaries = new SortedSet<ulong> { through.Milliseconds };
            for (int index = 1; index < partitions; index++)
            {
                ulong timestamp = duration * (ulong)index / (ulong)partitions;
                if (timestamp > Session.CurrentTime.Milliseconds && timestamp < through.Milliseconds) boundaries.Add(timestamp);
            }
            if (Restores == 0 && restore != 0 && restore > Session.CurrentTime.Milliseconds && restore <= through.Milliseconds) boundaries.Add(restore);
            foreach (ulong timestamp in boundaries)
            {
                Session.AdvanceTo(new(timestamp));
                if (Restores != 0 || restore == 0 || timestamp != restore) continue;
                long allocated = GC.GetTotalAllocatedBytes(false);
                var timer = Stopwatch.StartNew();
                GameSessionCheckpoint checkpoint = Capture(Session);
                string original = StationCheckpointDigest.Compute(checkpoint);
                CheckpointResult<GameSession> restored = _execution is null ? GameSession.RestoreCheckpoint(checkpoint)
                    : GameSession.RestoreCheckpoint(checkpoint, _execution);
                if (!restored.IsSuccess) throw new InvalidOperationException(restored.Failure!.Message);
                if (StationCheckpointDigest.Compute(Capture(restored.Value!)) != original)
                    throw new InvalidOperationException("Restoring the station checkpoint changed saved authority before continuation.");
                _beforeRestore.AddRange(Session.EventRecords);
                Session.Dispose();
                Session = restored.Value!;
                Restores++;
                RestoreMilliseconds += timer.Elapsed.TotalMilliseconds;
                RestoreAllocated += GC.GetTotalAllocatedBytes(false) - allocated;
            }
        }

        internal GameEventRecord[] AllEvents() => _beforeRestore.Concat(Session.EventRecords).ToArray();
        public void Dispose() => Session.Dispose();
    }
}
