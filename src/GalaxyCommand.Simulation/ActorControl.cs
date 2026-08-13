namespace GalaxyCommand.Simulation;

public enum ActorControllerKind
{
    Player,
    Autonomous,
    Script,
}

/// <summary>
/// Local simulation identity for the controller currently directing an actor.
/// This is not authentication or multiplayer authority.
/// </summary>
public sealed record ActorController
{
    public ActorController(ActorControllerKind kind, CommandSourceId id)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown controller kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        Kind = kind;
        Id = id;
    }

    public ActorControllerKind Kind { get; }

    public CommandSourceId Id { get; }

    public bool Matches(CommandSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ToSourceKind(Kind) == source.Kind
            && Id == source.Id;
    }

    public static ActorController FromScriptSource(CommandSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != CommandSourceKind.Script)
        {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                source.Kind,
                "A scripted controller requires a script command source.");
        }

        return new ActorController(ActorControllerKind.Script, source.Id);
    }

    private static CommandSourceKind ToSourceKind(ActorControllerKind kind) =>
        kind switch
        {
            ActorControllerKind.Player => CommandSourceKind.Player,
            ActorControllerKind.Autonomous => CommandSourceKind.Autonomous,
            ActorControllerKind.Script => CommandSourceKind.Script,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown controller kind."),
        };
}

public readonly record struct ActorControlRevision(ulong Value)
{
    public ActorControlRevision Next() =>
        new(checked(Value + 1));
}

/// <summary>
/// Stable opaque explanation for why temporary scripted control was taken.
/// Presentation may map this ID to localized text without changing simulation
/// state.
/// </summary>
public readonly record struct ActorOverrideReasonId
{
    public ActorOverrideReasonId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public enum ScriptedOverrideReleasePolicy
{
    CancelOutstanding,
}

public sealed record ActorControlSnapshot(
    ActorController BaseController,
    ActorController ActiveController,
    ActorController? TemporaryOverride,
    ActorOverrideReasonId? TemporaryOverrideReason,
    ActorControlRevision Revision);

internal enum ActorCommandEligibility
{
    Eligible,
    MissingActor,
    ActorOverridden,
    IneligibleSource,
}

internal enum ActorOverrideValidation
{
    Valid,
    MissingActor,
    InvalidSource,
    Conflict,
    StaleRevision,
}

internal sealed class ActorControlRegistry
{
    private readonly SortedDictionary<ShipId, ControlState> _actors =
        new(EntityIdComparer<ShipId>.Instance);

    /// <summary>
    /// Captures base control, any active scripted override, and exact revisions
    /// in stable ship order.
    /// </summary>
    internal ActorControlRegistryCheckpoint CaptureCheckpoint() =>
        new(_actors.Select(pair => new ActorControlCheckpoint(
            pair.Key,
            pair.Value.BaseController,
            pair.Value.Override,
            pair.Value.OverrideReason,
            pair.Value.Revision)));

    /// <summary>
    /// Validates and directly restores actor control without beginning or
    /// ending overrides and therefore without advancing saved revisions.
    /// </summary>
    internal static CheckpointResult<ActorControlRegistry> RestoreCheckpoint(
        ActorControlRegistryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        const string path = "$.checkpoint.control.actors";
        var restored = new ActorControlRegistry();
        for (int index = 0; index < checkpoint.Actors.Count; index++)
        {
            ActorControlCheckpoint? actor = checkpoint.Actors[index];
            if (actor is null)
            {
                return Rejected(
                    $"{path}[{index}]",
                    "An actor control checkpoint is missing.");
            }

            if (actor.ShipId.Value == 0)
            {
                return Rejected(
                    $"{path}[{index}].shipId",
                    "A controlled ship identifier must be nonzero.");
            }

            if (restored._actors.ContainsKey(actor.ShipId))
            {
                return Rejected(
                    $"{path}[{index}].shipId",
                    $"Duplicate controlled ship {actor.ShipId}.");
            }

            if (!IsValidBaseController(actor.BaseController))
            {
                return Rejected(
                    $"{path}[{index}].baseController",
                    "A base controller must be a valid player or autonomous controller.");
            }

            bool hasOverride = actor.TemporaryOverride is not null;
            bool hasReason = actor.TemporaryOverrideReason is { } reason
                && !string.IsNullOrWhiteSpace(reason.Value);
            if (hasOverride != hasReason)
            {
                return Rejected(
                    $"{path}[{index}]",
                    "A temporary override and its reason must either both be present or both be absent.");
            }

            if (actor.TemporaryOverride is { } temporaryOverride
                && (temporaryOverride.Kind != ActorControllerKind.Script
                    || string.IsNullOrWhiteSpace(temporaryOverride.Id.Value)))
            {
                return Rejected(
                    $"{path}[{index}].temporaryOverride",
                    "A temporary override must be a valid script controller.");
            }

            // Begin and end each advance the revision once, so odd revisions
            // are precisely the checkpoints with an active override.
            bool revisionHasOverride = (actor.Revision.Value & 1UL) != 0;
            if (revisionHasOverride != hasOverride)
            {
                return Rejected(
                    $"{path}[{index}].revision",
                    "Control revision parity does not match the saved override state.");
            }

            var state = new ControlState(actor.BaseController!)
            {
                Override = actor.TemporaryOverride,
                OverrideReason = actor.TemporaryOverrideReason,
                Revision = actor.Revision,
            };
            restored._actors.Add(actor.ShipId, state);
        }

        return CheckpointResult<ActorControlRegistry>.Success(restored);
    }

    internal void Add(ShipId shipId, ActorController baseController)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shipId.Value);
        ArgumentNullException.ThrowIfNull(baseController);
        if (baseController.Kind == ActorControllerKind.Script)
        {
            throw new ArgumentException(
                "A script cannot be an actor's persistent base controller.",
                nameof(baseController));
        }

        if (!_actors.TryAdd(shipId, new ControlState(baseController)))
        {
            throw new InvalidOperationException($"Duplicate controlled actor {shipId}.");
        }
    }

    internal bool Contains(ShipId shipId) =>
        _actors.ContainsKey(shipId);

    internal ActorCommandEligibility CheckCommand(
        ShipId shipId,
        CommandSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_actors.TryGetValue(shipId, out ControlState? state))
        {
            return ActorCommandEligibility.MissingActor;
        }

        if (state.Override is { } temporaryOverride)
        {
            if (temporaryOverride.Matches(source))
            {
                return ActorCommandEligibility.Eligible;
            }

            return state.BaseController.Matches(source)
                ? ActorCommandEligibility.ActorOverridden
                : ActorCommandEligibility.IneligibleSource;
        }

        return state.BaseController.Matches(source)
            ? ActorCommandEligibility.Eligible
            : ActorCommandEligibility.IneligibleSource;
    }

    internal ActorOverrideValidation ValidateBeginOverride(
        ShipId shipId,
        CommandSource source,
        ActorControlRevision expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_actors.TryGetValue(shipId, out ControlState? state))
        {
            return ActorOverrideValidation.MissingActor;
        }

        if (source.Kind != CommandSourceKind.Script)
        {
            return ActorOverrideValidation.InvalidSource;
        }

        if (state.Revision != expectedRevision)
        {
            return ActorOverrideValidation.StaleRevision;
        }

        return state.Override is null
            ? ActorOverrideValidation.Valid
            : ActorOverrideValidation.Conflict;
    }

    internal ActorOverrideValidation ValidateEndOverride(
        ShipId shipId,
        CommandSource source,
        ActorControlRevision expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_actors.TryGetValue(shipId, out ControlState? state))
        {
            return ActorOverrideValidation.MissingActor;
        }

        if (state.Revision != expectedRevision)
        {
            return ActorOverrideValidation.StaleRevision;
        }

        if (state.Override is null)
        {
            return ActorOverrideValidation.Conflict;
        }

        return state.Override.Matches(source)
            ? ActorOverrideValidation.Valid
            : ActorOverrideValidation.InvalidSource;
    }

    internal void BeginOverride(
        ShipId shipId,
        CommandSource source,
        ActorOverrideReasonId reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason.Value);
        ControlState state = GetRequired(shipId);
        if (state.Override is not null)
        {
            throw new InvalidOperationException($"Actor {shipId} is already overridden.");
        }

        state.Override = ActorController.FromScriptSource(source);
        state.OverrideReason = reason;
        state.Revision = state.Revision.Next();
    }

    internal void EndOverride(ShipId shipId)
    {
        ControlState state = GetRequired(shipId);
        if (state.Override is null)
        {
            throw new InvalidOperationException($"Actor {shipId} is not overridden.");
        }

        state.Override = null;
        state.OverrideReason = null;
        state.Revision = state.Revision.Next();
    }

    internal bool Remove(ShipId shipId) =>
        _actors.Remove(shipId);

    internal ActorControlSnapshot Capture(ShipId shipId)
    {
        ControlState state = GetRequired(shipId);
        return new ActorControlSnapshot(
            state.BaseController,
            state.Override ?? state.BaseController,
            state.Override,
            state.OverrideReason,
            state.Revision);
    }

    private ControlState GetRequired(ShipId shipId) =>
        _actors.GetValueOrDefault(shipId)
        ?? throw new KeyNotFoundException($"Unknown controlled actor {shipId}.");

    /// <summary>
    /// Accepts only persistent controller kinds with a usable local identity.
    /// Script controllers are valid exclusively as temporary overrides.
    /// </summary>
    private static bool IsValidBaseController(ActorController? controller) =>
        controller is not null
        && controller.Kind is ActorControllerKind.Player
            or ActorControllerKind.Autonomous
        && !string.IsNullOrWhiteSpace(controller.Id.Value);

    private static CheckpointResult<ActorControlRegistry> Rejected(
        string path,
        string message) =>
        CheckpointResult<ActorControlRegistry>.Rejected(
            new CheckpointValidationFailure(path, message));

    private sealed class ControlState
    {
        internal ControlState(ActorController baseController)
        {
            BaseController = baseController;
        }

        internal ActorController BaseController { get; }

        internal ActorController? Override { get; set; }

        internal ActorOverrideReasonId? OverrideReason { get; set; }

        internal ActorControlRevision Revision { get; set; }
    }
}
