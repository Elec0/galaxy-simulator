using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Initial authoritative spatial state for one ship. Runtime spawning remains
/// outside this setup-only contract.
/// </summary>
public sealed record InitialShipSetup
{
    public InitialShipSetup(
        ShipId id,
        SystemPosition position,
        ActorController baseController)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        ArgumentNullException.ThrowIfNull(baseController);
        if (baseController.Kind == ActorControllerKind.Script)
        {
            throw new ArgumentException(
                "A script cannot be an actor's persistent base controller.",
                nameof(baseController));
        }

        Id = id;
        Position = position;
        BaseController = baseController;
    }

    public ShipId Id { get; }

    public SystemPosition Position { get; }

    public ActorController BaseController { get; }
}

/// <summary>
/// Explicit immutable input used to construct a clean game session.
/// </summary>
public sealed class GameSessionSetup
{
    public GameSessionSetup(
        IEnumerable<StarSystem> systems,
        IEnumerable<InitialShipSetup> ships)
    {
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(ships);

        StarSystem[] systemValues = systems.ToArray();
        InitialShipSetup[] shipValues = ships.ToArray();
        var systemIds = new HashSet<SystemId>();
        foreach (StarSystem system in systemValues)
        {
            ArgumentNullException.ThrowIfNull(system);
            if (!systemIds.Add(system.Id))
            {
                throw new ArgumentException(
                    $"Duplicate system {system.Id}.",
                    nameof(systems));
            }
        }

        var shipIds = new HashSet<ShipId>();
        foreach (InitialShipSetup ship in shipValues)
        {
            ArgumentNullException.ThrowIfNull(ship);
            if (!shipIds.Add(ship.Id))
            {
                throw new ArgumentException(
                    $"Duplicate ship {ship.Id}.",
                    nameof(ships));
            }

            if (!systemIds.Contains(ship.Position.SystemId))
            {
                throw new ArgumentException(
                    $"Ship {ship.Id} references unknown system {ship.Position.SystemId}.",
                    nameof(ships));
            }
        }

        Systems = new ReadOnlyCollection<StarSystem>(systemValues);
        Ships = new ReadOnlyCollection<InitialShipSetup>(shipValues);
    }

    public IReadOnlyList<StarSystem> Systems { get; }

    public IReadOnlyList<InitialShipSetup> Ships { get; }
}
