using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Client-owned selection and fact-cursor input for one presentation refresh.
/// </summary>
public sealed record GamePresentationRequest
{
    /// <summary>
    /// Creates one validated observer-scoped presentation request.
    /// </summary>
    public GamePresentationRequest(
        PrincipalId observerPrincipalId,
        IEnumerable<ShipId> selectedShipIds,
        ShipId? focusedShipId,
        GameFactSequence? factCursor,
        int maximumFactCount)
    {
        ArgumentOutOfRangeException.ThrowIfZero(observerPrincipalId.Value);
        ArgumentNullException.ThrowIfNull(selectedShipIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFactCount);

        ShipId[] requested = selectedShipIds.ToArray();
        foreach (ShipId shipId in requested)
        {
            ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        }

        ShipId[] ordered = requested
            .OrderBy(shipId => shipId.Value)
            .ToArray();
        for (int index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1] == ordered[index])
            {
                throw new ArgumentException(
                    $"Duplicate selected ship {ordered[index]}.",
                    nameof(selectedShipIds));
            }
        }

        if (focusedShipId is { } focused)
        {
            ArgumentOutOfRangeException.ThrowIfZero(focused.Value);
            if (!ordered.Contains(focused))
            {
                throw new ArgumentException(
                    "The focused ship must be selected.",
                    nameof(focusedShipId));
            }
        }

        ObserverPrincipalId = observerPrincipalId;
        SelectedShipIds = new ReadOnlyCollection<ShipId>(ordered);
        FocusedShipId = focusedShipId;
        FactCursor = factCursor;
        MaximumFactCount = maximumFactCount;
    }

    public PrincipalId ObserverPrincipalId { get; }

    public IReadOnlyList<ShipId> SelectedShipIds { get; }

    public ShipId? FocusedShipId { get; }

    public GameFactSequence? FactCursor { get; }

    public int MaximumFactCount { get; }
}

/// <summary>
/// Immutable presentation-safe world state with authoritative relationship
/// diagnostics intentionally excluded.
/// </summary>
public sealed record GamePresentationWorldSnapshot
{
    internal GamePresentationWorldSnapshot(GameSnapshot world)
    {
        ArgumentNullException.ThrowIfNull(world);
        Time = world.Time;
        Systems = world.Systems;
        ConnectorEndpoints = world.ConnectorEndpoints;
        TransitConnections = world.TransitConnections;
        Ships = world.Ships;
    }

    public SimulationTime Time { get; }

    public IReadOnlyList<GameSystemSnapshot> Systems { get; }

    public IReadOnlyList<ConnectorEndpointSnapshot> ConnectorEndpoints { get; }

    public IReadOnlyList<TransitConnectionSnapshot> TransitConnections { get; }

    public IReadOnlyList<GameShipSnapshot> Ships { get; }
}

/// <summary>
/// Public principal identity safe for observer-scoped presentation.
/// </summary>
public sealed record RelationshipPrincipalPresentationSnapshot(
    PrincipalId Id,
    string Name);

/// <summary>
/// One other principal's directional treatment of the observer.
/// </summary>
public sealed record IncomingStandingPresentationSnapshot(
    PrincipalId AssessingPrincipalId,
    StandingValue Value,
    StandingBand Band);

/// <summary>
/// Immutable relationship view filtered for one observing principal.
/// </summary>
public sealed record RelationshipPresentationSnapshot
{
    internal RelationshipPresentationSnapshot(
        PrincipalId observerPrincipalId,
        IReadOnlyList<RelationshipPrincipalPresentationSnapshot> principals,
        IReadOnlyList<DiplomaticConditionSnapshot> diplomaticConditions,
        IReadOnlyList<IncomingStandingPresentationSnapshot> incomingStandings,
        IReadOnlyList<RelationshipGrantSnapshot> grantsIssuedToObserver)
    {
        ArgumentOutOfRangeException.ThrowIfZero(observerPrincipalId.Value);
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(diplomaticConditions);
        ArgumentNullException.ThrowIfNull(incomingStandings);
        ArgumentNullException.ThrowIfNull(grantsIssuedToObserver);
        ObserverPrincipalId = observerPrincipalId;
        Principals = principals;
        DiplomaticConditions = diplomaticConditions;
        IncomingStandings = incomingStandings;
        GrantsIssuedToObserver = grantsIssuedToObserver;
    }

    public PrincipalId ObserverPrincipalId { get; }

    public IReadOnlyList<RelationshipPrincipalPresentationSnapshot> Principals { get; }

    public IReadOnlyList<DiplomaticConditionSnapshot> DiplomaticConditions { get; }

    public IReadOnlyList<IncomingStandingPresentationSnapshot> IncomingStandings { get; }

    public IReadOnlyList<RelationshipGrantSnapshot> GrantsIssuedToObserver { get; }
}

/// <summary>
/// Resolution of client-owned selected ships against one world snapshot.
/// </summary>
public sealed record GamePresentationSelection
{
    internal GamePresentationSelection(
        IReadOnlyList<ShipId> requestedShipIds,
        IReadOnlyList<GameShipSnapshot> resolvedShips,
        IReadOnlyList<ShipId> unresolvedShipIds,
        GameShipSnapshot? focusedShip)
    {
        ArgumentNullException.ThrowIfNull(requestedShipIds);
        ArgumentNullException.ThrowIfNull(resolvedShips);
        ArgumentNullException.ThrowIfNull(unresolvedShipIds);
        RequestedShipIds = requestedShipIds;
        ResolvedShips = resolvedShips;
        UnresolvedShipIds = unresolvedShipIds;
        FocusedShip = focusedShip;
    }

    public IReadOnlyList<ShipId> RequestedShipIds { get; }

    public IReadOnlyList<GameShipSnapshot> ResolvedShips { get; }

    public IReadOnlyList<ShipId> UnresolvedShipIds { get; }

    public GameShipSnapshot? FocusedShip { get; }
}

/// <summary>
/// Immutable rendering-independent composition of presentation-safe world and
/// relationship state, local selection, and one incremental semantic-fact read.
/// </summary>
public sealed record GamePresentationSnapshot
{
    internal GamePresentationSnapshot(
        GamePresentationWorldSnapshot world,
        RelationshipPresentationSnapshot relationships,
        GamePresentationSelection selection,
        GameFactReadResult facts,
        IReadOnlyList<GameFactEnvelope> selectedShipFacts,
        GameFactSequence? nextFactCursor)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(relationships);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(selectedShipFacts);
        World = world;
        Relationships = relationships;
        Selection = selection;
        Facts = facts;
        SelectedShipFacts = selectedShipFacts;
        NextFactCursor = nextFactCursor;
    }

    public GamePresentationWorldSnapshot World { get; }

    public RelationshipPresentationSnapshot Relationships { get; }

    public GamePresentationSelection Selection { get; }

    public GameFactReadResult Facts { get; }

    public IReadOnlyList<GameFactEnvelope> SelectedShipFacts { get; }

    /// <summary>
    /// Cursor after all source facts inspected for this observer-scoped read,
    /// including facts withheld by the relationship privacy filter.
    /// </summary>
    public GameFactSequence? NextFactCursor { get; }
}

internal static class GamePresentationSnapshotFactory
{
    /// <summary>
    /// Composes one presentation-safe world, observer relationship projection,
    /// selection resolution, and filtered fact read.
    /// </summary>
    internal static GamePresentationSnapshot Create(
        GameSnapshot world,
        GamePresentationRequest request,
        GameFactReadResult facts)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(facts);

        var shipsById = world.Ships.ToDictionary(ship => ship.Id);
        var resolvedShips = new List<GameShipSnapshot>();
        var unresolvedShipIds = new List<ShipId>();
        foreach (ShipId shipId in request.SelectedShipIds)
        {
            if (shipsById.TryGetValue(shipId, out GameShipSnapshot? ship))
            {
                resolvedShips.Add(ship);
            }
            else
            {
                unresolvedShipIds.Add(shipId);
            }
        }

        GameShipSnapshot? focusedShip = request.FocusedShipId is { } focused
            && shipsById.TryGetValue(focused, out GameShipSnapshot? resolvedFocusedShip)
                ? resolvedFocusedShip
                : null;
        IReadOnlyList<GameFactEnvelope> visibleFacts = GameSnapshotCollection.Copy(
            facts.Facts.Where(fact => IsVisibleToObserver(
                fact.Fact,
                world.Relationships,
                request.ObserverPrincipalId)));
        var scopedFacts = new GameFactReadResult(
            visibleFacts,
            facts.OldestRetainedSequence,
            facts.NewestCommittedSequence,
            facts.CursorGap);
        var selectedIds = new HashSet<ShipId>(request.SelectedShipIds);
        IReadOnlyList<GameFactEnvelope> selectedShipFacts =
            GameSnapshotCollection.Copy(visibleFacts.Where(fact =>
                ReferencesSelectedShip(fact.Fact, selectedIds)));
        GameFactSequence? nextFactCursor = facts.Facts.Count > 0
            ? facts.Facts[^1].Sequence
            : request.FactCursor;

        return new GamePresentationSnapshot(
            new GamePresentationWorldSnapshot(world),
            CreateRelationshipPresentation(
                world.Relationships,
                request.ObserverPrincipalId),
            new GamePresentationSelection(
                request.SelectedShipIds,
                GameSnapshotCollection.Copy(resolvedShips),
                GameSnapshotCollection.Copy(unresolvedShipIds),
                focusedShip),
            scopedFacts,
            selectedShipFacts,
            nextFactCursor);
    }

    /// <summary>
    /// Applies the initial relationship information boundary while preserving
    /// non-relational facts and public diplomacy facts.
    /// </summary>
    private static bool IsVisibleToObserver(
        GameFact fact,
        RelationshipSnapshot relationships,
        PrincipalId observerPrincipalId)
    {
        ArgumentNullException.ThrowIfNull(fact);
        return fact switch
        {
            StandingChangedFact standing =>
                standing.SubjectPrincipalId == observerPrincipalId,
            DiplomaticConditionChangedFact => true,
            RelationshipGrantIssuedFact grant =>
                grant.HolderPrincipalId == observerPrincipalId,
            RelationshipGrantRevokedFact grant => relationships.Grants.Any(value =>
                value.Id == grant.Id && value.HolderPrincipalId == observerPrincipalId),
            _ => true,
        };
    }

    /// <summary>
    /// Projects public relationship state and observer-directed private state
    /// without exposing the observer's reverse assessments.
    /// </summary>
    private static RelationshipPresentationSnapshot CreateRelationshipPresentation(
        RelationshipSnapshot relationships,
        PrincipalId observerPrincipalId)
    {
        if (!relationships.Principals.Any(principal => principal.Id == observerPrincipalId))
        {
            throw new ArgumentException(
                $"Observer principal {observerPrincipalId} is not registered.",
                nameof(observerPrincipalId));
        }

        return new RelationshipPresentationSnapshot(
            observerPrincipalId,
            GameSnapshotCollection.Copy(relationships.Principals.Select(principal =>
                new RelationshipPrincipalPresentationSnapshot(
                    principal.Id,
                    principal.Name))),
            GameSnapshotCollection.Copy(relationships.DiplomaticConditions),
            GameSnapshotCollection.Copy(relationships.Standings
                .Where(standing => standing.SubjectPrincipalId == observerPrincipalId)
                .Select(standing => new IncomingStandingPresentationSnapshot(
                    standing.AssessingPrincipalId,
                    standing.Value,
                    standing.Band))),
            GameSnapshotCollection.Copy(relationships.Grants
                .Where(grant => grant.HolderPrincipalId == observerPrincipalId
                    && grant.IsIssued)));
    }

    private static bool ReferencesSelectedShip(
        GameFact fact,
        HashSet<ShipId> selectedIds) =>
        GetShipId(fact) is { } shipId && selectedIds.Contains(shipId);

    /// <summary>
    /// Resolves the ship identity carried by fact kinds that participate in
    /// selected-ship presentation filtering.
    /// </summary>
    private static ShipId? GetShipId(GameFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        return fact switch
        {
            ShipOrderTransitionFact transition => transition.ShipId,
            ShipLocalMotionStartedFact started => started.ShipId,
            ShipLocalMotionEndedFact ended => ended.ShipId,
            ShipConnectorTransitStartedFact started => started.ShipId,
            ShipConnectorTransitCompletedFact completed => completed.ShipId,
            EntityMaterializedFact materialized => materialized.ShipId,
            EntityRemovedFact removed => removed.ShipId,
            _ => null,
        };
    }
}
