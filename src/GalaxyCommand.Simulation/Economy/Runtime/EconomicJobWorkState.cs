namespace GalaxyCommand.Simulation;

/// <summary>Exact remaining work in thousandths of a work unit, anchored only while its provider is active.</summary>
internal sealed record EconomicJobWorkState(UInt128 RemainingMilliwork, SimulationTime? ActiveSince)
{
    internal static EconomicJobWorkState Start(Work work, SimulationTime now) => new((UInt128)work.Units * 1_000, now);

    /// <summary>Evaluates elapsed work without mutation, retaining fractional work across provider changes.</summary>
    internal UInt128 RemainingAt(Throughput throughput, SimulationTime now)
    {
        if (ActiveSince is not { } start) return RemainingMilliwork;
        ArgumentOutOfRangeException.ThrowIfLessThan(now.Milliseconds, start.Milliseconds, nameof(now));
        UInt128 supplied = (UInt128)(now.Milliseconds - start.Milliseconds) * throughput.UnitsPerSecond;
        return supplied >= RemainingMilliwork ? 0 : RemainingMilliwork - supplied;
    }

    /// <summary>Computes the exact rounded-up future completion and rejects timeline exhaustion before owner mutation.</summary>
    internal SimulationTime CompletionAt(Throughput throughput, SimulationTime now)
    {
        ArgumentOutOfRangeException.ThrowIfZero(throughput.UnitsPerSecond);
        UInt128 remaining = RemainingAt(throughput, now);
        UInt128 delay = remaining / throughput.UnitsPerSecond + (remaining % throughput.UnitsPerSecond == 0 ? 0U : 1U);
        if (delay > ulong.MaxValue - now.Milliseconds) throw new OverflowException("Economic work exceeds the simulation timeline.");
        return new(now.Milliseconds + (ulong)delay);
    }

    /// <summary>Checks exact work bounds, active anchoring and cached deadline before any restored job is published.</summary>
    internal static bool IsValidCheckpoint(EconomicJobWorkState? state, Work total, Throughput throughput,
        bool available, bool running, bool paused, bool finished, SimulationTime? completion)
    {
        if (!running && !paused && !finished) return state is null;
        if (state is null || state.RemainingMilliwork > (UInt128)total.Units * 1_000) return false;
        if (paused) return !available && state.ActiveSince is null && completion is null;
        if (finished) return state.RemainingMilliwork == 0 && state.ActiveSince is null;
        if (!available || state.ActiveSince is not { } start || completion is null) return false;
        try { return state.CompletionAt(throughput, start) == completion; }
        catch (OverflowException) { return false; }
    }
}
