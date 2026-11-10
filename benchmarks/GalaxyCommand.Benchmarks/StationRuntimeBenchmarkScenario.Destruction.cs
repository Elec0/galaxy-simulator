using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

internal sealed partial class StationRuntimeBenchmarkScenario
{
    /// <summary>Exercises grouped losses, exact disposition witnesses and collection through the real session owners.</summary>
    private static void RunDestruction(StationRun run, int active, int systems, int builders, ulong duration,
        ulong materials, List<GameSessionCheckpoint> checkpoints)
    {
        for (int addition = 0; addition < 2; addition++)
        {
            foreach (StationSnapshot station in Current(run, active))
                run.Submit(new ConstructStationModuleCommand(Owner, station.Id, station.Revision, StorageKey));
            AssignStructural(run, active, systems, builders, StationProjectKind.ModuleConstruction);
            run.AdvanceTo(new(duration * 8 / 12 + (ulong)(addition + 1) * (duration / 24)));
        }
        run.Store(Current(run, active), materials + 32);
        foreach (StationSnapshot station in Current(run, active))
        {
            StationModuleId storage = station.Modules.First(module => module.DefinitionKey == StorageKey).Id;
            Destroy(run, station, systems, builders,
                station.Modules.Where(module => module.Id == storage || module.DefinitionKey == UpgradeKey).Select(module => module.Id).ToArray(), false, 1);
        }
        checkpoints.Add(Capture(run.Session));
        run.AdvanceTo(new(duration * 9 / 12 + 1));
        Collect(run, active, systems, builders);
        run.AdvanceTo(new(duration * 10 / 12));
        foreach (StationSnapshot station in Current(run, active))
            Destroy(run, station, systems, builders, station.Modules.Select(module => module.Id).ToArray(), true, 2);
        checkpoints.Add(Capture(run.Session));
        run.AdvanceTo(new(duration * 10 / 12 + 1));
        Collect(run, active, systems, builders);
        run.AdvanceTo(new(duration));
        Require(Capture(run.Session).Cargo!.Objects.Count == 0, "Recoverable station loss cargo was not collected.");
    }

    private static void Destroy(StationRun run, StationSnapshot station, int systems, int builders,
        IReadOnlyList<StationModuleId> modules, bool destroyBase, ulong occurrence)
    {
        ShipId builder = Builder(systems, builders, (int)station.Id.Value - 1, 0);
        GameplayCommandRecord cause = run.Submit(new MoveShipCommand(builder,
            new NavigationDestination.Position(station.Position), OrderPlacement.ReplaceAll));
        StationDestructionResult result = run.Session.CommitStationDestruction(new(
            new CargoDispositionTrigger.Command(cause.Envelope.Sequence), station.Id, station.Revision, modules, destroyBase, occurrence));
        Require(result.IsSuccess && result.WasApplied, $"Station loss failed: {result.Failure}.");
    }

    /// <summary>Selects each field from retained occurrence evidence, without depending on player visibility or projection order.</summary>
    private static void Collect(StationRun run, int active, int systems, int builders)
    {
        GameSessionCheckpoint checkpoint = Capture(run.Session);
        foreach (CargoDispositionReceipt receipt in checkpoint.Cargo!.Receipts)
        {
            if (receipt.FieldHandle is not { } field || !checkpoint.Cargo.Objects.Any(item => item.OriginalFieldId == field)) continue;
            StationDestructionReceipt origin = checkpoint.Stations!.Destructions.Single(item =>
                item.Request.Trigger == receipt.Request.Key.Trigger);
            Require(origin.Request.Station.Value <= (ulong)active, "A quiet site unexpectedly emitted loss cargo.");
            run.Submit(new CollectCargoFieldCommand(Builder(systems, builders, (int)origin.Request.Station.Value - 1, 0),
                field, [], OrderPlacement.ReplaceAll));
        }
    }
}
