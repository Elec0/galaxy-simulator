using GalaxyCommand.Simulation;

namespace GalaxyCommand.GodotClient;

/// <summary>
/// Captures local pacing actions and gameplay commands in one application-owned
/// arrival order until the next completed simulation timestamp boundary.
/// </summary>
internal sealed class ApplicationInputBuffer
{
    private readonly Queue<BufferedApplicationInput> _pending = [];

    public int Count => _pending.Count;

    /// <summary>
    /// Captures one gameplay command without assigning simulation time or a
    /// command sequence. The session assigns both only when this input drains.
    /// </summary>
    public void EnqueueGameplay(CommandSource source, GameplayCommand command)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(command);
        _pending.Enqueue(new BufferedApplicationInput.Gameplay(source, command));
    }

    /// <summary>
    /// Captures one local pacing action without treating it as authoritative
    /// gameplay input or assigning it a command sequence.
    /// </summary>
    public void EnqueuePacing(ApplicationPacingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _pending.Enqueue(new BufferedApplicationInput.Pacing(action));
    }

    /// <summary>
    /// Returns every captured input in FIFO order and clears the buffer so a
    /// later boundary cannot apply the same input twice.
    /// </summary>
    public IReadOnlyList<BufferedApplicationInput> Drain()
    {
        var drained = new List<BufferedApplicationInput>(_pending.Count);
        while (_pending.TryDequeue(out BufferedApplicationInput? input))
        {
            drained.Add(input);
        }

        return drained;
    }
}

/// <summary>
/// One application input captured before the client reaches a completed
/// timestamp boundary.
/// </summary>
internal abstract record BufferedApplicationInput
{
    internal sealed record Gameplay(
        CommandSource Source,
        GameplayCommand Command) : BufferedApplicationInput;

    internal sealed record Pacing(
        ApplicationPacingAction Action) : BufferedApplicationInput;
}

/// <summary>
/// A local pacing request that can be applied at a completed timestamp boundary
/// without becoming a gameplay command.
/// </summary>
internal abstract record ApplicationPacingAction
{
    internal abstract void Apply(ApplicationPacingController pacing);

    internal sealed record Pause : ApplicationPacingAction
    {
        internal override void Apply(ApplicationPacingController pacing)
        {
            ArgumentNullException.ThrowIfNull(pacing);
            pacing.Pause();
        }
    }

    internal sealed record Unpause : ApplicationPacingAction
    {
        internal override void Apply(ApplicationPacingController pacing)
        {
            ArgumentNullException.ThrowIfNull(pacing);
            pacing.Unpause();
        }
    }

    internal sealed record IncreaseSpeed : ApplicationPacingAction
    {
        internal override void Apply(ApplicationPacingController pacing)
        {
            ArgumentNullException.ThrowIfNull(pacing);
            pacing.IncreaseSpeed();
        }
    }

    internal sealed record DecreaseSpeed : ApplicationPacingAction
    {
        internal override void Apply(ApplicationPacingController pacing)
        {
            ArgumentNullException.ThrowIfNull(pacing);
            pacing.DecreaseSpeed();
        }
    }

    internal sealed record SelectSpeed(double Multiplier) : ApplicationPacingAction
    {
        internal override void Apply(ApplicationPacingController pacing)
        {
            ArgumentNullException.ThrowIfNull(pacing);
            pacing.SelectSpeed(Multiplier);
        }
    }
}
