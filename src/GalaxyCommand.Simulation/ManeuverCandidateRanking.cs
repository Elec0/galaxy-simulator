namespace GalaxyCommand.Simulation;

/// <summary>
/// Replaceable policy identity used to compare otherwise valid maneuver
/// candidates. The default value is ordinary fastest-arrival planning.
/// </summary>
public enum ManeuverObjective
{
    FastestArrival = 0,
    ShortestPath = 1,
}

/// <summary>
/// Complete analytic path length in integer millimeters.
/// </summary>
public readonly record struct ManeuverPathDistance(UInt128 Millimeters);

/// <summary>
/// Stable case-sensitive identity used only after all substantive candidate
/// metrics tie.
/// </summary>
public readonly record struct ManeuverProfileKey
{
    /// <summary>
    /// Creates one nonempty opaque profile identity.
    /// </summary>
    public ManeuverProfileKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Immutable metrics sufficient to rank one valid maneuver candidate without
/// exposing or mutating its phase plan.
/// </summary>
public sealed record ManeuverCandidateRank
{
    /// <summary>
    /// Creates a rank for a candidate with at least one phase and a stable
    /// profile identity. Zero path length remains valid for turning in place.
    /// </summary>
    public ManeuverCandidateRank(
        SimulationTime arrivesAt,
        ManeuverPathDistance pathDistance,
        int phaseCount,
        ManeuverProfileKey profileKey)
    {
        if (phaseCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(phaseCount),
                phaseCount,
                "A maneuver candidate must contain at least one phase.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(profileKey.Value);
        ArrivesAt = arrivesAt;
        PathDistance = pathDistance;
        PhaseCount = phaseCount;
        ProfileKey = profileKey;
    }

    public SimulationTime ArrivesAt { get; }

    public ManeuverPathDistance PathDistance { get; }

    public int PhaseCount { get; }

    public ManeuverProfileKey ProfileKey { get; }
}

/// <summary>
/// Deterministic total ordering for valid maneuver candidates.
/// </summary>
public static class ManeuverCandidateRanking
{
    /// <summary>
    /// Returns a negative value when <paramref name="left"/> is preferred, a
    /// positive value when <paramref name="right"/> is preferred, or zero for
    /// identical ranks. Unknown objectives are rejected.
    /// </summary>
    public static int Compare(
        ManeuverObjective objective,
        ManeuverCandidateRank left,
        ManeuverCandidateRank right)
    {
        int result = objective switch
        {
            ManeuverObjective.FastestArrival =>
                left.ArrivesAt.CompareTo(right.ArrivesAt),
            ManeuverObjective.ShortestPath =>
                left.PathDistance.Millimeters.CompareTo(
                    right.PathDistance.Millimeters),
            _ => throw new ArgumentOutOfRangeException(
                nameof(objective),
                objective,
                "Unknown maneuver objective."),
        };
        if (result != 0)
        {
            return result;
        }

        result = objective == ManeuverObjective.FastestArrival
            ? left.PathDistance.Millimeters.CompareTo(
                right.PathDistance.Millimeters)
            : left.ArrivesAt.CompareTo(right.ArrivesAt);
        if (result != 0)
        {
            return result;
        }

        result = left.PhaseCount.CompareTo(right.PhaseCount);
        return result != 0
            ? result
            : StringComparer.Ordinal.Compare(
                left.ProfileKey.Value,
                right.ProfileKey.Value);
    }
}
