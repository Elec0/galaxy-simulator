using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Disclosure-safe notice without an authoritative causal payload.</summary>
public sealed record GamePresentationFactEnvelope(GameFactSequence Sequence, SimulationTime Timestamp, GameFact Fact);

/// <summary>A bounded player notice suffix retaining source cursor and gap semantics.</summary>
public sealed record GamePresentationFactReadResult(
    IReadOnlyList<GamePresentationFactEnvelope> Facts,
    GameFactSequence? OldestRetainedSequence,
    GameFactSequence? NewestCommittedSequence,
    bool CursorGap);

public enum ObservedShipChangeKind { MotionChanged, Departed, Arrived, Appeared, Disappeared }

public enum ObservedSensorChangeKind { Appeared, Disappeared }

/// <summary>Covered sensor evidence without actor, command, inventory, cause or removal reason.</summary>
public sealed record ObservedSensorChangeFact(EntityId EntityId, QualifiedContentKey DefinitionKey,
    PrincipalId PrincipalId, SystemPosition Position, ObservedSensorChangeKind Kind) : GameFact;

/// <summary>The configured player's command result, without source identity or diagnostic cause data.</summary>
public sealed record PlayerCommandOutcomeFact(
    CommandSequence CommandSequence,
    string CommandKind,
    CommandResultStatus Status,
    CommandRejectionCode? RejectionCode) : GameFact;

/// <summary>External spatial evidence without private motion, order, or removal details.</summary>
public sealed record ObservedShipChangeFact(
    ShipId ShipId,
    ObservedShipChangeKind Kind,
    SystemPosition Position,
    EntityId? EntityId = null,
    PrincipalId? PrincipalId = null,
    ConstructionDesignId? DesignId = null) : GameFact;

internal sealed record PlayerFactDisclosure(PrincipalId ObserverPrincipalId, GameFact? Fact,
    PrincipalId? SubjectPrincipalId = null);

internal sealed record PlayerObservationCheckpoint(
    int BehaviorVersion,
    PrincipalId ObserverPrincipalId,
    CommandSource PlayerCommandSource);
