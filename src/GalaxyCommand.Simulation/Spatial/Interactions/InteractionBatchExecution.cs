using System.Runtime.ExceptionServices;

namespace GalaxyCommand.Simulation;

/// <summary>Explicit opt-in tuning for pure interaction evaluation on bounded shared-thread-pool batches.</summary>
public sealed class InteractionBatchExecution
{
    /// <summary>Requires positive worker capacity and batch size. One worker uses the serial reference loop.</summary>
    public InteractionBatchExecution(int maximumWorkers, int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWorkers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        MaximumWorkers = maximumWorkers;
        BatchSize = batchSize;
    }

    public int MaximumWorkers { get; }
    public int BatchSize { get; }

    /// <summary>
    /// Runs only internal pure evaluations into private batch buffers. Join precedes ordered
    /// publication; the first failed input in caller order is rethrown with its original stack.
    /// No partial output escapes. The serial path stops directly at its first failure.
    /// </summary>
    internal T[] Evaluate<T>(int count, Func<int, T> evaluate)
    {
        if (MaximumWorkers == 1 || count == 0)
        {
            var serial = new T[count];
            for (int i = 0; i < count; i++) serial[i] = evaluate(i);
            return serial;
        }
        int batches = (count - 1) / BatchSize + 1;
        var outputs = new T[batches][];
        var failures = new ExceptionDispatchInfo?[batches];
        Parallel.For(0, batches, new ParallelOptions { MaxDegreeOfParallelism = MaximumWorkers }, batch =>
        {
            int start = batch * BatchSize;
            int length = Math.Min(BatchSize, count - start);
            var local = new T[length];
            for (int offset = 0; offset < length; offset++)
            {
                try { local[offset] = evaluate(start + offset); }
                catch (Exception error)
                {
                    // The first failure within this contiguous batch is its earliest input.
                    // Other batches finish privately; completion order never selects the error.
                    failures[batch] = ExceptionDispatchInfo.Capture(error);
                    return;
                }
            }
            outputs[batch] = local;
        });
        foreach (ExceptionDispatchInfo? failure in failures) failure?.Throw();
        var combined = new T[count];
        for (int batch = 0; batch < batches; batch++) outputs[batch].CopyTo(combined, batch * BatchSize);
        return combined;
    }
}
