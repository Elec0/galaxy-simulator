using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

/// <summary>Measures complete advancing sessions; only forecast execution varies between presets.</summary>
internal sealed class InteractionSessionBenchmarkScenario(bool batched) : IBenchmarkScenario
{
    public ScenarioCorrectnessResult Run(ResolvedBenchmarkScenario configuration)
    {
        int systems = configuration.GetInt32(BenchmarkParameterNames.SystemCount);
        int active = configuration.GetInt32(BenchmarkParameterNames.ActiveShipCount);
        var initial = ScenarioSetup.CreateShips(configuration.GetInt32(BenchmarkParameterNames.ShipCount), systems);
        var ships = initial.Select((ship, i) => new InitialShipSetup(ship.EntityId, ship.Id,
            ship.CargoInventoryId, ship.PrincipalId, ship.Design,
            ScenarioSetup.Position((int)ship.Position.SystemId.Value, i < active ? 0 : ((i - active) / systems % 2 == 0 ? 3 : 10), 0),
            ship.BaseController)).ToArray();
        var setup = new GameSessionSetup(ScenarioSetup.CreateSystems(systems), ships,
            ScenarioSetup.Relationships, ScenarioSetup.RootSeed,
            configuration.GetInt32(BenchmarkParameterNames.FactRetentionCapacity));
        var navigation = new DirectLocalNavigationPlanner(new FixedTravelTimeEstimator(
            new SimulationDuration(configuration.GetUInt64(BenchmarkParameterNames.TravelDurationMilliseconds))));
        // The serial preset exercises the original session entry point, not a one-worker substitute.
        using var session = batched
            ? new GameSession(setup, navigation, new InteractionBatchExecution(
                configuration.GetInt32(BenchmarkParameterNames.WorkerCount), configuration.GetInt32(BenchmarkParameterNames.BatchSize)))
            : new GameSession(setup, navigation);
        var owner = session.RegisterSpatialInteractionDomain("benchmark");
        var domain = new RecordingDomain();
        session.BindSpatialInteractionDomain("benchmark", domain);
        int interestCount = configuration.GetInt32(BenchmarkParameterNames.InterestCount);
        ulong range = configuration.GetUInt64(BenchmarkParameterNames.RangeUnits);
        var interests = new ShipPairInteractionInterest[interestCount];
        var targetsBySystem = ships.Skip(active).GroupBy(ship => ship.Position.SystemId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        for (int i = 0; i < interests.Length; i++)
        {
            var source = ships[i % active];
            var targets = targetsBySystem[source.Position.SystemId];
            var target = targets[i / active % targets.Length];
            interests[i] = owner.Register($"contact-{i}", source.Id, target.Id, range);
        }
        long updates = 0, cancellations = 0;
        int replans = configuration.GetInt32(BenchmarkParameterNames.ReplanCount);
        ulong interval = configuration.GetUInt64(BenchmarkParameterNames.ReplanIntervalMilliseconds);
        long distance = configuration.Get(BenchmarkParameterNames.DestinationDistance);
        for (int wave = 0; wave < replans; wave++)
        {
            if (wave > 0) session.AdvanceTo(new SimulationTime(checked((ulong)wave * interval)));
            if (wave > 0)
            {
                foreach (var interest in owner.CaptureSnapshot())
                {
                    if ((interest.Id.Value + (ulong)wave) % 11 == 0)
                    {
                        if (owner.Cancel(interest.Id)) cancellations++;
                    }
                    else if ((interest.Id.Value + (ulong)wave) % 3 == 0)
                    {
                        owner.Update(interest.Id, interest.Source, interest.Target, range + (ulong)(wave % 2));
                        updates++;
                    }
                }
            }
            for (int i = 0; i < active; i++)
            {
                var ship = ships[i];
                var goal = ScenarioSetup.Position((int)ship.Position.SystemId.Value, wave % 2 == 0 ? distance : -distance, 0);
                ScenarioSetup.RequireAccepted(session.SubmitCommand(ScenarioSetup.AutonomousSource,
                    new MoveShipCommand(ship.Id, new NavigationDestination.Position(goal), OrderPlacement.ReplaceAll)), configuration.Id);
            }
        }
        session.AdvanceTo(new SimulationTime(configuration.GetUInt64(BenchmarkParameterNames.SimulatedDurationMilliseconds)));
        var result = ScenarioResult.Create(session, configuration);
        var digest = new DeterministicDigest();
        digest.Add(result.Digest);
        // Include semantic workload parameters and serial delivery evidence, excluding execution and harness tuning.
        foreach (var parameter in configuration.Parameters.Where(p => p.Key is not ("workerCount" or "batchSize" or "warmupIterations" or "measuredIterations")))
        {
            digest.Add(parameter.Key); digest.Add(parameter.Value);
        }
        foreach (var contact in domain.Delivered)
        {
            digest.Add(contact.SystemId.Value); digest.Add(contact.Interest.Id.Value);
            digest.Add(contact.Interest.Generation.Value); digest.Add(contact.Interest.Source.Value);
            digest.Add(contact.Interest.Target.Value); digest.Add(contact.Interest.RangeUnits);
            digest.Add(contact.Contact.Timestamp.Milliseconds); digest.Add(contact.Contact.InRangeAtTimestamp);
            digest.Add(contact.Contact.AlreadyInRange);
        }
        foreach (var interest in owner.CaptureSnapshot())
        {
            digest.Add(interest.Id.Value); digest.Add(interest.Generation.Value); digest.Add(interest.RangeUnits);
        }
        var counts = new SortedDictionary<string, long>(result.Counts.ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal)
        {
            ["effects"] = domain.Delivered.Count,
            ["evaluationWaves"] = domain.Waves,
            ["interestsRegistered"] = interestCount,
            ["interestsUpdated"] = updates,
            ["interestsCanceled"] = cancellations,
            ["interestsRemaining"] = owner.CaptureSnapshot().Count,
            ["interactionEventsIgnored"] = session.EventRecords.Count(e => e.Kind is GameEventKind.SpatialInteraction && e.Disposition != ScheduledEventDisposition.Applied),
            ["interactionEventsApplied"] = session.EventRecords.Count(e => e.Kind is GameEventKind.SpatialInteraction && e.Disposition == ScheduledEventDisposition.Applied),
        };
        return new ScenarioCorrectnessResult(digest.ToString(), result.SimulatedMilliseconds, counts);
    }

    /// <summary>Records serial delivery without adding gameplay behavior owned by other tasks.</summary>
    private sealed class RecordingDomain : ISpatialInteractionDomainEvaluator
    {
        internal List<InteractionCandidate> Delivered { get; } = [];
        internal long Waves { get; private set; }
        public IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view, IReadOnlyList<InteractionCandidate> contacts)
        {
            Waves++;
            return contacts.Select(contact => new SpatialInteractionEffectProposal(contact.Key, 0, 0, new Effect(this, contact))).ToArray();
        }
        private sealed class Effect(RecordingDomain domain, InteractionCandidate contact) : ISpatialInteractionEffect
        {
            public void Commit(GameSession session) => domain.Delivered.Add(contact);
        }
    }
}
