namespace GalaxyCommand.Simulation;

/// <summary>
/// Deterministically reduces independently evaluated maneuver ranks.
/// </summary>
public static class ManeuverCandidateSelection
{
    /// <summary>
    /// Selects the preferred rank under the requested objective without
    /// depending on candidate enumeration order. The candidate set must be
    /// nonempty and contain no null entries; unknown objectives are rejected
    /// even when only one candidate is supplied.
    /// </summary>
    public static ManeuverCandidateRank SelectPreferred(
        ManeuverObjective objective,
        IEnumerable<ManeuverCandidateRank> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (objective is not ManeuverObjective.FastestArrival
            and not ManeuverObjective.ShortestPath)
        {
            throw new ArgumentOutOfRangeException(
                nameof(objective),
                objective,
                "Unknown maneuver objective.");
        }

        ManeuverCandidateRank? preferred = null;
        foreach (ManeuverCandidateRank? candidate in candidates)
        {
            if (candidate is null)
            {
                throw new ArgumentException(
                    "A maneuver candidate set cannot contain null entries.",
                    nameof(candidates));
            }

            // The total rank ordering makes reduction independent of which
            // evaluator or worker published its candidate first.
            if (preferred is null
                || ManeuverCandidateRanking.Compare(
                    objective,
                    candidate,
                    preferred) < 0)
            {
                preferred = candidate;
            }
        }

        return preferred ?? throw new ArgumentException(
            "A maneuver candidate set must contain at least one candidate.",
            nameof(candidates));
    }
}
