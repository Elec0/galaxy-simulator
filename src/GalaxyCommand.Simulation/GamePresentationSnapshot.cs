using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Client-owned selection and fact-cursor input for one presentation refresh.
/// </summary>
public sealed record GamePresentationRequest
{
    public GamePresentationRequest(
        IEnumerable<ShipId> selectedShipIds,
        ShipId? focusedShipId,
        GameFactSequence? factCursor,
        int maximumFactCount)
    {
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

        SelectedShipIds = new ReadOnlyCollection<ShipId>(ordered);
        FocusedShipId = focusedShipId;
        FactCursor = factCursor;
        MaximumFactCount = maximumFactCount;
    }

    public IReadOnlyList<ShipId> SelectedShipIds { get; }

    public ShipId? FocusedShipId { get; }

    public GameFactSequence? FactCursor { get; }

    public int MaximumFactCount { get; }
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
/// Immutable rendering-independent composition of world state, local selection,
/// and one incremental semantic-fact read.
/// </summary>
public sealed record GamePresentationSnapshot
{
    internal GamePresentationSnapshot(
        GameSnapshot world,
        GamePresentationSelection selection,
        GameFactReadResult facts,
        IReadOnlyList<GameFactEnvelope> selectedShipFacts)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(selectedShipFacts);
        World = world;
        Selection = selection;
        Facts = facts;
        SelectedShipFacts = selectedShipFacts;
    }

    public GameSnapshot World { get; }

    public GamePresentationSelection Selection { get; }

    public GameFactReadResult Facts { get; }

    public IReadOnlyList<GameFactEnvelope> SelectedShipFacts { get; }
}

internal static class GamePresentationSnapshotFactory
{
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
        var selectedIds = new HashSet<ShipId>(request.SelectedShipIds);
        IReadOnlyList<GameFactEnvelope> selectedShipFacts =
            GameSnapshotCollection.Copy(facts.Facts.Where(fact =>
                ReferencesSelectedShip(fact.Fact, selectedIds)));

        return new GamePresentationSnapshot(
            world,
            new GamePresentationSelection(
                request.SelectedShipIds,
                GameSnapshotCollection.Copy(resolvedShips),
                GameSnapshotCollection.Copy(unresolvedShipIds),
                focusedShip),
            facts,
            selectedShipFacts);
    }

    private static bool ReferencesSelectedShip(
        GameFact fact,
        HashSet<ShipId> selectedIds) =>
        GetShipId(fact) is { } shipId && selectedIds.Contains(shipId);

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
            EntityRemovedFact removed => removed.ShipId,
            _ => null,
        };
    }
}
