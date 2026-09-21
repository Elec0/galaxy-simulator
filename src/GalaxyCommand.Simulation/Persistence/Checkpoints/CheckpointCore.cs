using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

internal sealed record CheckpointValidationFailure(
    string Path,
    string Message);

internal sealed class CheckpointResult<T>
    where T : class
{
    private CheckpointResult(T? value, CheckpointValidationFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    internal T? Value { get; }

    internal CheckpointValidationFailure? Failure { get; }

    internal bool IsSuccess => Value is not null;

    internal static CheckpointResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new CheckpointResult<T>(value, null);
    }

    internal static CheckpointResult<T> Rejected(
        CheckpointValidationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new CheckpointResult<T>(null, failure);
    }
}

