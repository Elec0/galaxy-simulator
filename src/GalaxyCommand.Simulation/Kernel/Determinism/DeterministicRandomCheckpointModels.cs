using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record RandomStreamCheckpoint(
    RandomStreamKey? Key,
    string? GeneratorAlgorithm,
    int GeneratorVersion,
    ulong S0,
    ulong S1,
    ulong S2,
    ulong S3,
    ulong? NextPosition);

internal sealed class DeterministicRandomCheckpoint
{
    internal DeterministicRandomCheckpoint(
        IEnumerable<byte> rootSeed,
        string? derivationAlgorithm,
        int derivationVersion,
        IEnumerable<RandomStreamCheckpoint?> streams)
    {
        ArgumentNullException.ThrowIfNull(rootSeed);
        ArgumentNullException.ThrowIfNull(streams);
        RootSeed = new ReadOnlyCollection<byte>(rootSeed.ToArray());
        DerivationAlgorithm = derivationAlgorithm;
        DerivationVersion = derivationVersion;
        Streams = new ReadOnlyCollection<RandomStreamCheckpoint?>(streams.ToArray());
    }

    internal ReadOnlyCollection<byte> RootSeed { get; }

    internal string? DerivationAlgorithm { get; }

    internal int DerivationVersion { get; }

    internal ReadOnlyCollection<RandomStreamCheckpoint?> Streams { get; }
}

