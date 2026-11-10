namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    public bool HasPhysicalCompletionWaves => _stations is not null;

    /// <summary>Limits work-wave ordering to station envelopes; every other physical domain remains an exact barrier.</summary>
    public bool IsPhysicalCompletionWaveEvent(ScheduledEvent<GameEvent> scheduled) => scheduled.Payload is GameEvent.StationWork;

    /// <summary>Commits prepared station effects by project identity while publishing each outcome with its original agenda cause.</summary>
    public IReadOnlyList<ScheduledEventDisposition> HandlePhysicalCompletionWave(IReadOnlyList<ScheduledEvent<GameEvent>> events,
        SimulationTime now, EventAgenda<GameEvent> agenda)
    {
        if (_stations is null) return events.Select(_ => ScheduledEventDisposition.IgnoredMissingReference).ToArray();
        IReadOnlyList<StationWorkWaveCommit> commits = _stations.ApplyWorkWave(events);
        var dispositions = new Dictionary<EventKey, ScheduledEventDisposition>();
        foreach (StationWorkWaveCommit commit in commits)
        {
            _facts.Commit(now, new ScheduledEventFactCause(commit.Scheduled.Key), commit.Facts);
            dispositions.Add(commit.Scheduled.Key, commit.Disposition);
        }
        return events.Select(scheduled => dispositions[scheduled.Key]).ToArray();
    }
}
