using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record ActorControlCheckpoint(
    ShipId ShipId,
    ActorController? BaseController,
    ActorController? TemporaryOverride,
    ActorOverrideReasonId? TemporaryOverrideReason,
    ActorControlRevision Revision);

internal sealed class ActorControlRegistryCheckpoint
{
    internal ActorControlRegistryCheckpoint(
        IEnumerable<ActorControlCheckpoint?> actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        Actors = new ReadOnlyCollection<ActorControlCheckpoint?>(
            actors.ToArray());
    }

    internal ReadOnlyCollection<ActorControlCheckpoint?> Actors { get; }
}

