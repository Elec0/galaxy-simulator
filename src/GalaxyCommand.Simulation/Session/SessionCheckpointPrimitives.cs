using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record IdSequenceCheckpoint(ulong? NextValue);

internal sealed record CommandAdmissionCheckpoint(
    IdSequenceCheckpoint Sequences,
    SimulationTime? LastSubmittedAt);

