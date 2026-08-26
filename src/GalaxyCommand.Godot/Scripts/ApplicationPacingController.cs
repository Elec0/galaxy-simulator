using System.Collections.ObjectModel;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.GodotClient;

/// <summary>
/// Applies local player pacing to wall-clock frames before the client requests
/// authoritative advancement. This controller never mutates session state or
/// creates gameplay commands.
/// </summary>
public sealed class ApplicationPacingController
{
    private static readonly IReadOnlyList<double> DefaultMultipliers =
        Array.AsReadOnly([1d, 2d, 5d, 10d, 30d]);

    private readonly double[] _runningSpeedMultipliers;
    private readonly ReadOnlyCollection<double> _readOnlyRunningSpeedMultipliers;
    private double _fractionalMilliseconds;
    private bool _responseRequiredDialogueIsOpen;
    private bool _automaticDialoguePauseIsActive;

    /// <summary>
    /// Creates pacing with the accepted default running-speed ladder.
    /// </summary>
    public ApplicationPacingController()
        : this(DefaultMultipliers)
    {
    }

    /// <summary>
    /// Creates pacing from a validated, mod-supplied running-speed ladder.
    /// Pause remains a separate local state rather than a ladder entry.
    /// </summary>
    public ApplicationPacingController(IEnumerable<double> runningSpeedMultipliers)
    {
        ArgumentNullException.ThrowIfNull(runningSpeedMultipliers);
        double[] values = runningSpeedMultipliers.ToArray();
        ValidateRunningSpeedMultipliers(values);
        _runningSpeedMultipliers = values;
        _readOnlyRunningSpeedMultipliers = Array.AsReadOnly(values);
        SelectedSpeedMultiplier = values[0];
    }

    /// <summary>
    /// Gets the validated running-speed presets in their configured order.
    /// </summary>
    public IReadOnlyList<double> RunningSpeedMultipliers => _readOnlyRunningSpeedMultipliers;

    /// <summary>
    /// Gets whether local pacing currently suppresses further advancement.
    /// </summary>
    public bool IsPaused { get; private set; }

    /// <summary>
    /// Gets the remembered running-speed multiplier, including while paused.
    /// </summary>
    public double SelectedSpeedMultiplier { get; private set; }

    /// <summary>
    /// Pauses local advancement while retaining the selected running speed for
    /// a later direct unpause. This explicit player action overrides any
    /// automatic dialogue pause that is currently active.
    /// </summary>
    public void Pause()
    {
        OverrideAutomaticDialoguePause();
        IsPaused = true;
    }

    /// <summary>
    /// Resumes local advancement at the running speed retained by pause and
    /// overrides any automatic dialogue pause that is currently active.
    /// </summary>
    public void Unpause()
    {
        OverrideAutomaticDialoguePause();
        IsPaused = false;
    }

    /// <summary>
    /// Moves one running-speed step upward. When paused, it changes the
    /// remembered speed without resuming local advancement.
    /// </summary>
    public void IncreaseSpeed()
    {
        int index = Array.IndexOf(_runningSpeedMultipliers, SelectedSpeedMultiplier);
        if (IsPaused)
        {
            if (index < _runningSpeedMultipliers.Length - 1)
            {
                // A relative speed adjustment is an explicit player override,
                // but pause remains active until the player resumes directly.
                OverrideAutomaticDialoguePause();
                SelectedSpeedMultiplier = _runningSpeedMultipliers[index + 1];
            }

            return;
        }

        if (index < _runningSpeedMultipliers.Length - 1)
        {
            SelectedSpeedMultiplier = _runningSpeedMultipliers[index + 1];
        }
    }

    /// <summary>
    /// Moves one running-speed step downward. When paused, it changes the
    /// remembered speed without resuming local advancement.
    /// </summary>
    public void DecreaseSpeed()
    {
        int index = Array.IndexOf(_runningSpeedMultipliers, SelectedSpeedMultiplier);
        if (IsPaused)
        {
            if (index > 0)
            {
                // A relative speed adjustment is an explicit player override,
                // but pause remains active until the player resumes directly.
                OverrideAutomaticDialoguePause();
                SelectedSpeedMultiplier = _runningSpeedMultipliers[index - 1];
            }

            return;
        }

        if (index == 0)
        {
            Pause();
            return;
        }

        SelectedSpeedMultiplier = _runningSpeedMultipliers[index - 1];
    }

    /// <summary>
    /// Selects one configured running-speed preset and resumes advancement,
    /// overriding any automatic dialogue pause that is currently active.
    /// </summary>
    public void SelectSpeed(double multiplier)
    {
        if (!_runningSpeedMultipliers.Contains(multiplier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(multiplier),
                multiplier,
                "The selected multiplier is not present in the configured speed ladder.");
        }

        OverrideAutomaticDialoguePause();
        SelectedSpeedMultiplier = multiplier;
        IsPaused = false;
    }

    /// <summary>
    /// Offers a response-required dialogue opening to local pacing. The caller
    /// supplies the already-classified dialogue event and the persisted player
    /// preference; this controller owns neither.
    /// </summary>
    public void OpenResponseRequiredDialogue(
        bool pauseWhenResponseRequiredDialogueOpens)
    {
        if (_responseRequiredDialogueIsOpen)
        {
            // A later screen in the same conversation cannot reacquire a pause
            // after the player has manually overridden its original one.
            return;
        }

        _responseRequiredDialogueIsOpen = true;
        if (!pauseWhenResponseRequiredDialogueOpens || IsPaused)
        {
            return;
        }

        IsPaused = true;
        _automaticDialoguePauseIsActive = true;
    }

    /// <summary>
    /// Closes the active response-required dialogue and restores running pace
    /// only when that dialogue still owns the automatic pause it acquired.
    /// </summary>
    public void CloseResponseRequiredDialogue()
    {
        if (!_responseRequiredDialogueIsOpen)
        {
            return;
        }

        _responseRequiredDialogueIsOpen = false;
        if (!_automaticDialoguePauseIsActive)
        {
            return;
        }

        // The dialogue acquired this pause while running, so it alone may
        // release it. A manual action clears this ownership first.
        _automaticDialoguePauseIsActive = false;
        IsPaused = false;
    }

    /// <summary>
    /// Returns the completed-boundary target for one rendered wall-clock frame.
    /// Paused wall-clock time is deliberately not accumulated for later replay.
    /// </summary>
    public SimulationTime Advance(SimulationTime currentTime, TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsed),
                elapsed,
                "Elapsed wall-clock time cannot be negative.");
        }

        if (IsPaused)
        {
            return currentTime;
        }

        double requestedMilliseconds =
            _fractionalMilliseconds + elapsed.TotalMilliseconds * SelectedSpeedMultiplier;
        if (!double.IsFinite(requestedMilliseconds) ||
            requestedMilliseconds > ulong.MaxValue - currentTime.Milliseconds)
        {
            throw new InvalidOperationException(
                "The selected speed and elapsed frame duration exceed the supported simulation-time range.");
        }

        ulong wholeMilliseconds = (ulong)Math.Floor(requestedMilliseconds);
        _fractionalMilliseconds = requestedMilliseconds - wholeMilliseconds;
        return new SimulationTime(checked(currentTime.Milliseconds + wholeMilliseconds));
    }

    /// <summary>
    /// Enforces the accepted mod-configurable speed-ladder contract before the
    /// application begins a session.
    /// </summary>
    private static void ValidateRunningSpeedMultipliers(
        double[] runningSpeedMultipliers)
    {
        if (runningSpeedMultipliers.Length == 0)
        {
            throw new ArgumentException(
                "The configured speed ladder must contain 1x as its first running step.",
                nameof(runningSpeedMultipliers));
        }

        if (runningSpeedMultipliers[0] != 1d)
        {
            throw new ArgumentException(
                "The configured speed ladder must start with 1x.",
                nameof(runningSpeedMultipliers));
        }

        double previous = 0d;
        for (int index = 0; index < runningSpeedMultipliers.Length; index++)
        {
            double multiplier = runningSpeedMultipliers[index];
            if (!double.IsFinite(multiplier) || multiplier <= 0d)
            {
                throw new ArgumentException(
                    "Each configured speed multiplier must be positive and finite.",
                    nameof(runningSpeedMultipliers));
            }

            if (index > 0 && multiplier <= previous)
            {
                throw new ArgumentException(
                    "Configured speed multipliers must be unique and strictly increasing.",
                    nameof(runningSpeedMultipliers));
            }

            previous = multiplier;
        }
    }

    /// <summary>
    /// Prevents a later dialogue close from changing pace after a direct player
    /// action has taken ownership of the current pause or speed selection.
    /// </summary>
    private void OverrideAutomaticDialoguePause() =>
        _automaticDialoguePauseIsActive = false;
}
