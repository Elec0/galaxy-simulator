using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed class GameFactStoreCheckpoint
{
    internal GameFactStoreCheckpoint(
        int capacity,
        IdSequenceCheckpoint sequences,
        IEnumerable<GameFactEnvelope?> retainedFacts)
    {
        ArgumentNullException.ThrowIfNull(sequences);
        ArgumentNullException.ThrowIfNull(retainedFacts);
        Capacity = capacity;
        Sequences = sequences;
        RetainedFacts = new ReadOnlyCollection<GameFactEnvelope?>(
            retainedFacts.ToArray());
    }

    internal int Capacity { get; }

    internal IdSequenceCheckpoint Sequences { get; }

    internal ReadOnlyCollection<GameFactEnvelope?> RetainedFacts { get; }
}

