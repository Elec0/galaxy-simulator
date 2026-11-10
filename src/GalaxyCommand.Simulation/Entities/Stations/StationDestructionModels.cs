namespace GalaxyCommand.Simulation;

internal sealed record StationDestructionRequest(CargoDispositionTrigger Trigger, StationId Station,
    ulong ExpectedRevision, IReadOnlyList<StationModuleId> Modules, bool DestroyBase, ulong Occurrence);

internal enum StationDestructionFailure { InvalidInput, StaleTarget, ConflictingReplay, PolicyUnavailable, OwnerConflict, NumericExhaustion }

internal sealed record StationDestructionResult(bool WasApplied, StationDestructionFailure? Failure = null)
{
    internal bool IsSuccess => Failure is null;
}

/// <summary>Prepared workflow consequences; inventory source loss retains ownership of the exact outgoing releases.</summary>
internal interface IPreparedStationWorkflowLoss
{
    IReadOnlyList<InventoryCommitmentRelease> Releases { get; }
    bool CanCommit(PreparedCargoSourceLoss loss);
    void ApplyAfterSourceLoss(PreparedCargoSourceLoss loss);
}

internal sealed record StationDestructionReceipt(StationDestructionRequest Request, StationSnapshot Before,
    InventoryCheckpoint InventoryBefore, StationStorageLossWindow WindowBefore, StationStorageLossWindow WindowAfter,
    CargoDispositionKey CargoKey, SimulationTime CommittedAt);
