using System.Numerics;

namespace GalaxyCommand.Simulation;

internal sealed record StationWorkWaveCommit(ScheduledEvent<GameEvent> Scheduled,
    ScheduledEventDisposition Disposition, IReadOnlyList<GameFactProposal> Facts);

internal sealed partial class StationRuntimeOwner
{
    /// <summary>Finds the greatest whole progress already covered by consumed inputs without activating completion.</summary>
    private static ulong FreeProgressCeiling(ProjectState project)
    {
        ulong ceiling = project.Recipe.RequiredWork.Units - 1;
        foreach ((MaterialId material, Quantity required) in project.Recipe.Inputs)
        {
            ulong consumed = project.Consumed.GetValueOrDefault(material, Quantity.Zero).Units;
            ceiling = Math.Min(ceiling, (ulong)((UInt128)consumed * project.Recipe.RequiredWork.Units / required.Units));
        }
        return ceiling;
    }

    /// <summary>Publishes only earned work whose cumulative input consumption is unchanged; gate work remains for its physical envelope.</summary>
    private static void AccrueProject(StationState station, ProjectState project, ulong elapsed)
    {
        project.WorkNumerator += elapsed * project.Rate;
        ulong ceiling = FreeProgressCeiling(project);
        if (ceiling <= project.Completed.Units) return;
        ulong advance = (ulong)BigInteger.Min(ceiling - project.Completed.Units, project.WorkNumerator / 1000);
        if (advance == 0) return;
        project.Completed = new(project.Completed.Units + advance);
        project.WorkNumerator -= (BigInteger)advance * 1000;
        station.Revision = checked(station.Revision + advance);
    }

    /// <summary>Wakes at the next changed input quantity, completion, or last affordable free work before starvation.</summary>
    private static ulong NextWorkBoundary(StationState station, ProjectState project)
    {
        ulong total = project.Recipe.RequiredWork.Units;
        ulong nextGate = total;
        foreach ((MaterialId material, Quantity required) in project.Recipe.Inputs)
        {
            ulong consumed = project.Consumed.GetValueOrDefault(material, Quantity.Zero).Units;
            ulong covered = (ulong)((UInt128)consumed * total / required.Units);
            if (covered < total) nextGate = Math.Min(nextGate, covered + 1);
        }
        ulong affordable = StationConstructionProgress.Evaluate(project.Recipe, project.Completed,
            new(total - project.Completed.Units), Available(station, project)).CompletedWork.Units;
        return Math.Min(nextGate, affordable);
    }

    /// <summary>Computes one future wake from exact uncommitted earned work; it never reopens a physical phase.</summary>
    private static SimulationTime WorkDueAt(StationState station, ProjectState project, SimulationTime now, BigInteger rate)
    {
        BigInteger needed = (BigInteger)(NextWorkBoundary(station, project) - project.Completed.Units) * 1000 - project.WorkNumerator;
        BigInteger delay = BigInteger.Max(1, (needed + rate - 1) / rate);
        if (delay > ulong.MaxValue - now.Milliseconds) throw new OverflowException("Station work exceeds the simulation timeline.");
        return new(now.Milliseconds + (ulong)delay);
    }

    /// <summary>Evaluates one contiguous work wave against immutable current availability, then commits in global project admission order.</summary>
    internal IReadOnlyList<StationWorkWaveCommit> ApplyWorkWave(IReadOnlyList<ScheduledEvent<GameEvent>> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var prepared = new List<(ScheduledEvent<GameEvent> Scheduled, StationConstructionProgress? Progress)>();
        var budgets = new Dictionary<InventoryId, Dictionary<MaterialId, Quantity>>();
        foreach (ScheduledEvent<GameEvent> scheduled in events.OrderBy(item => ((GameEvent.StationWork)item.Payload).Project.Value))
        {
            var work = (GameEvent.StationWork)scheduled.Payload;
            StationConstructionProgress? progress = null;
            if (_stations.TryGetValue(work.Station, out StationState? station)
                && station.Projects.TryGetValue(work.Project, out ProjectState? project)
                && project.Pending == scheduled.Key && project.Generation == scheduled.Generation)
            {
                if (!budgets.TryGetValue(station.Inventory.Id, out Dictionary<MaterialId, Quantity>? budget))
                    budgets.Add(station.Inventory.Id, budget = []);
                foreach (MaterialId material in project.Recipe.Inputs.Keys)
                    if (!budget.ContainsKey(material)) budget.Add(material, station.Inventory.Available(material));
                ulong supplied = (ulong)BigInteger.Min(ulong.MaxValue, project.WorkNumerator / 1000);
                progress = StationConstructionProgress.Evaluate(project.Recipe, project.Completed, new(supplied), budget);
                foreach ((MaterialId material, Quantity amount) in progress.ConsumedInputs)
                    budget[material] = budget[material].Subtract(amount);
            }
            prepared.Add((scheduled, progress));
        }
        var commits = new List<StationWorkWaveCommit>(prepared.Count);
        foreach (var item in prepared)
        {
            var applied = ApplyWork(item.Scheduled, item.Progress);
            commits.Add(new(item.Scheduled, applied.Disposition, applied.Facts));
        }
        return commits.AsReadOnly();
    }
}
