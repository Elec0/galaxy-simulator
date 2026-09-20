namespace GalaxyCommand.Simulation;

public enum StationaryCruisePlanKind
{
    TerminalSettle,
    SubCruise,
    Cruise,
    DirectionalPlanner,
}

/// <summary>
/// Typed result from complete stationary cruise-versus-sub-cruise comparison.
/// The selected kind owns one executable payload while the evaluated
/// sub-cruise plan remains available for deterministic diagnostics.
/// </summary>
public sealed record StationaryCruisePlanSelection
{
    internal StationaryCruisePlanSelection(
        StationaryCruisePlanKind kind,
        StationaryDirectionalPlanSelection subCruisePlan,
        CruiseTerminalManeuverPlan? cruisePlan,
        ManeuverCandidateRank? selectedRank)
    {
        Kind = kind;
        SubCruisePlan = subCruisePlan;
        CruisePlan = cruisePlan;
        SelectedRank = selectedRank;
    }

    public StationaryCruisePlanKind Kind { get; }

    public StationaryDirectionalPlanSelection SubCruisePlan { get; }

    public CruiseTerminalManeuverPlan? CruisePlan { get; }

    public ManeuverCandidateRank? SelectedRank { get; }
}

/// <summary>
/// Builds complete stationary sub-cruise and cruise candidates under one
/// objective, then admits cruise only when its terminal arrival is strictly
/// earlier. Equal arrivals retain sub-cruise and avoid spool state.
/// </summary>
public static class StationaryCruiseManeuverPlanner
{
    /// <summary>
    /// Selects the complete terminal plan from exact rest. Unsupported
    /// sub-cruise ownership remains an explicit directional handoff because a
    /// cruise candidate cannot substitute for an unevaluated comparison.
    /// </summary>
    public static StationaryCruisePlanSelection Select(
        SimulationTime startsAt,
        ShipKinematicState start,
        SystemPosition destination,
        ShipHeading? requestedHeading,
        EffectiveShipManeuverCapability capability,
        ManeuverObjective objective)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (start.Velocity != ShipVelocity.Zero)
        {
            throw new ArgumentException(
                "Stationary cruise comparison requires zero starting velocity.",
                nameof(start));
        }

        StationaryDirectionalPlanSelection subCruise =
            StationaryDirectionalManeuverPlanner.Select(
                startsAt,
                start,
                destination,
                requestedHeading,
                capability,
                objective);
        if (subCruise.Kind == StationaryDirectionalPlanKind.TerminalSettle)
        {
            return new StationaryCruisePlanSelection(
                StationaryCruisePlanKind.TerminalSettle,
                subCruise,
                cruisePlan: null,
                selectedRank: null);
        }

        if (subCruise.CompletePlan is not { } completeSubCruise
            || subCruise.SelectedRank is not { } subCruiseRank)
        {
            return new StationaryCruisePlanSelection(
                StationaryCruisePlanKind.DirectionalPlanner,
                subCruise,
                cruisePlan: null,
                selectedRank: null);
        }

        if (!CruiseTerminalManeuverPlan.TryCreateFromRest(
                startsAt,
                start,
                destination,
                requestedHeading,
                capability,
                out CruiseTerminalManeuverPlan? cruise)
            || cruise is null
            || cruise.EndsAt >= completeSubCruise.EndsAt)
        {
            return new StationaryCruisePlanSelection(
                StationaryCruisePlanKind.SubCruise,
                subCruise,
                cruisePlan: null,
                subCruiseRank);
        }

        return new StationaryCruisePlanSelection(
            StationaryCruisePlanKind.Cruise,
            subCruise,
            cruise,
            ManeuverPlanRanking.Rank(cruise));
    }
}
