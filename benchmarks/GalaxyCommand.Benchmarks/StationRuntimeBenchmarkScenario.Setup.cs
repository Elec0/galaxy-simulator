using System.Buffers.Binary;
using System.Globalization;
using GalaxyCommand.Simulation;
using GalaxyCommand.Content;

namespace GalaxyCommand.Benchmarks;

internal sealed partial class StationRuntimeBenchmarkScenario
{
    /// <summary>Authors every fixture capability and rule explicitly, with a separate local observer and station-owning principal.</summary>
    private static GameSessionSetup CreateSetup(ResolvedBenchmarkScenario configuration)
    {
        bool loss = configuration.Get(BenchmarkParameterNames.DestructionLifecycle) != 0;
        int systems = configuration.GetInt32(BenchmarkParameterNames.SystemCount);
        int count = configuration.GetInt32(BenchmarkParameterNames.StationCount);
        int builders = configuration.GetInt32(BenchmarkParameterNames.BuildersPerProject);
        ulong material = configuration.GetUInt64(BenchmarkParameterNames.ConstructionMaterials);
        ulong work = configuration.GetUInt64(BenchmarkParameterNames.ConstructionWork);
        ulong throughput = configuration.GetUInt64(BenchmarkParameterNames.BuilderThroughput);
        ulong duration = configuration.GetUInt64(BenchmarkParameterNames.SimulatedDurationMilliseconds);
        long goal = ((count + systems - 1L) / systems + 1) * 100;
        string speed = Math.Max(1, goal * 1000 / (long)duration).ToString(CultureInfo.InvariantCulture);
        var maneuver = new ShipManeuverCapability(10000, ManeuverAcceleration.ParseMetersPerSecondSquared("1000"), null,
            ManeuverSpeed.ParseMetersPerSecond(speed), ManeuverSpeed.ParseMetersPerSecond(speed),
            ManeuverTurnRate.ParseDegreesPerSecond("360"), new(1000));
        var observer = new ShipDesign(new(1), "Station Benchmark Observer", new([], new(1)), new(32), maneuver, 30, 100);
        var builder = new ShipDesign(new(2), "Station Benchmark Builder", new([], new(1)), new(loss ? material + 64 : 32), maneuver, 0, 100,
            new StationConstructionCapability(0, new(throughput)));
        var ships = new List<InitialShipSetup>();
        for (int index = 0; index < systems; index++)
        {
            ulong id = (ulong)index + 1;
            ships.Add(new(new(id), new(id), new(id), Player, observer, Position(index + 1, 0), new(ActorControllerKind.Player, PlayerSource.Id)));
        }
        for (int index = 0; index < count; index++)
        for (int source = 0; source < builders; source++)
        {
            ShipId id = Builder(systems, builders, index, source);
            ships.Add(new(new(id.Value), id, new(id.Value), Owner, builder, Position(index % systems + 1, (index / systems + 1) * 100),
                new(ActorControllerKind.Autonomous, OwnerSource.Id)));
        }
        var baseDefinition = new StationDefinition(BaseKey, "Benchmark Base", 8, new(material + 16), new(material + 16),
            new(new Dictionary<MaterialId, Quantity> { [Material] = new(material) }, new(work)), 0,
            (loss ? [ModuleKey, UpgradeKey, StorageKey] : new[] { ModuleKey, UpgradeKey }), new(2), new Dictionary<MaterialId, Quantity> { [Material] = new(3) });
        var module = new StationModuleDefinition(ModuleKey, "Benchmark Sensor", 1, new(0), 60, null,
            new(new Dictionary<MaterialId, Quantity> { [Material] = new(2) }, new(2)), new(2),
            new Dictionary<MaterialId, Quantity> { [Material] = new(1) }, [UpgradeKey],
            [new(UpgradeKey, new(new Dictionary<MaterialId, Quantity> { [Material] = new(1) }, new(2)))]);
        var upgrade = new StationModuleDefinition(UpgradeKey, "Upgraded Benchmark Sensor", 2, new(0), 120, null,
            new(new Dictionary<MaterialId, Quantity> { [Material] = new(3) }, new(2)), new(2),
            new Dictionary<MaterialId, Quantity> { [Material] = new(1) }, []);
        var storage = new StationModuleDefinition(StorageKey, "Benchmark Storage", 1, new(8), 0, null,
            new([], new(1)), new(1), [], []);
        var cargoPolicy = !loss ? null : new CargoDispositionPolicy(
            QualifiedContentKey.Create("benchmark.stations", "cargo-policy", "loss"), 1, 1, 1, 1,
            new Dictionary<CargoLossCause, CargoRecoveryFraction>
            {
                [CargoLossCause.EntityDestruction] = new(1, 2), [CargoLossCause.StorageModuleLoss] = new(1, 2),
                [CargoLossCause.FunctionalEjection] = new(1, 1),
            }, 1, 0, new(duration * 2), new(1), new(1), new(1000));
        var storageLoss = !loss ? null : new StationStorageLossPolicy(
            QualifiedContentKey.Create("benchmark.stations", "station-storage-loss-policy", "loss"), 1, 1, 100);
        var relationships = new RelationshipSetup([new(Player, new("station-benchmark-player"), "Player"),
            new(Owner, new("station-benchmark-owner"), "Station Owner")], Player, ScenarioSetup.Relationships.StandingPolicy, []);
        byte[] seed = new byte[RandomRootSeed.ByteCount];
        BinaryPrimitives.WriteInt64LittleEndian(seed, configuration.Get(BenchmarkParameterNames.Seed));
        return new(ScenarioSetup.CreateSystems(systems), ships, new ConnectorTopology([], []), [], null, relationships,
            RandomRootSeed.FromBytes(seed), configuration.GetInt32(BenchmarkParameterNames.FactRetentionCapacity), PlayerSource,
            cargoPolicy: cargoPolicy,
            stations: new(new([baseDefinition], loss ? [module, upgrade, storage] : new[] { module, upgrade }),
                new(new Dictionary<MaterialId, PhysicalDefinition> { [Material] = MaterialDefinition }), StationTerritoryPolicy.Absent,
                [new(OwnerSource, Owner)], storageLossPolicy: storageLoss));
    }
}
