namespace GalaxyCommand.Simulation;

public enum RuntimeEvaluationWave
{
    PhysicalCompletion = 1,
    ProductionReadiness = 3,
    ConstructionReadiness = 4,
    LogisticsAssignment = 6,
    ActorOrders = 7,
}

public readonly record struct AgendaProposalOrder(
    RuntimeEvaluationWave Wave,
    ulong PrimaryOwnerId,
    ulong SecondaryActivityId,
    int EffectKind,
    int LocalOrdinal) : IComparable<AgendaProposalOrder>
{
    public int CompareTo(AgendaProposalOrder other)
    {
        int result = Wave.CompareTo(other.Wave);
        if (result != 0) return result;
        result = PrimaryOwnerId.CompareTo(other.PrimaryOwnerId);
        if (result != 0) return result;
        result = SecondaryActivityId.CompareTo(other.SecondaryActivityId);
        if (result != 0) return result;
        result = EffectKind.CompareTo(other.EffectKind);
        return result != 0 ? result : LocalOrdinal.CompareTo(other.LocalOrdinal);
    }

    public static bool operator <(
        AgendaProposalOrder left,
        AgendaProposalOrder right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(
        AgendaProposalOrder left,
        AgendaProposalOrder right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(
        AgendaProposalOrder left,
        AgendaProposalOrder right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(
        AgendaProposalOrder left,
        AgendaProposalOrder right) =>
        left.CompareTo(right) >= 0;
}

public sealed record AgendaEventProposal<TEvent>(
    AgendaProposalOrder Order,
    SimulationTime Timestamp,
    EventPhase Phase,
    EventGeneration Generation,
    TEvent Payload);

public sealed class AgendaCommitResult
{
    internal AgendaCommitResult(IEnumerable<EventKey> eventKeys)
    {
        EventKeys = Array.AsReadOnly(eventKeys.ToArray());
    }

    public IReadOnlyList<EventKey> EventKeys { get; }
}

/// <summary>
/// Narrow owner for deterministic event creation-sequence allocation.
/// </summary>
public static class AgendaCommitOwner
{
    public static AgendaCommitResult Commit<TEvent>(
        EventAgenda<TEvent> agenda,
        IEnumerable<AgendaEventProposal<TEvent>> proposals)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(proposals);

        AgendaEventProposal<TEvent>[] ordered = proposals
            .OrderBy(proposal => proposal.Order)
            .ToArray();
        Validate(agenda, ordered);

        var keys = new List<EventKey>(ordered.Length);
        foreach (AgendaEventProposal<TEvent> proposal in ordered)
        {
            keys.Add(agenda.Schedule(
                proposal.Timestamp,
                proposal.Phase,
                proposal.Generation,
                proposal.Payload));
        }

        return new AgendaCommitResult(keys);
    }

    private static void Validate<TEvent>(
        EventAgenda<TEvent> agenda,
        IReadOnlyList<AgendaEventProposal<TEvent>> proposals)
    {
        AgendaProposalOrder? previous = null;
        foreach (AgendaEventProposal<TEvent> proposal in proposals)
        {
            if (previous == proposal.Order)
            {
                throw new InvalidOperationException(
                    $"Duplicate agenda proposal order {proposal.Order}.");
            }

            if (proposal.Timestamp < agenda.CurrentTime)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(proposals),
                    proposal.Timestamp,
                    $"Timestamp {proposal.Timestamp.Milliseconds} ms precedes current simulation time {agenda.CurrentTime.Milliseconds} ms.");
            }

            if (proposal.Timestamp == agenda.CurrentTime
                && agenda.CurrentPhase is { } currentPhase
                && proposal.Phase < currentPhase)
            {
                throw new InvalidOperationException(
                    $"Cannot schedule phase {proposal.Phase} after phase {currentPhase} at the current timestamp.");
            }

            previous = proposal.Order;
        }
    }
}
