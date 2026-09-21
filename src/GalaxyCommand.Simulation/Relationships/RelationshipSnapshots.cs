using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

public sealed record PrincipalSnapshot(
    PrincipalId Id,
    PrincipalContentId ContentId,
    string Name);

/// <summary>
/// Immutable diagnostic snapshot of one resolved directional standing value.
/// </summary>
public sealed record StandingSnapshot(
    PrincipalId AssessingPrincipalId,
    PrincipalId SubjectPrincipalId,
    StandingValue Value,
    StandingBand Band);

/// <summary>
/// Immutable mutual diplomatic condition for one canonical principal pair.
/// </summary>
public sealed record DiplomaticConditionSnapshot(
    PrincipalId LowerPrincipalId,
    PrincipalId UpperPrincipalId,
    DiplomaticCondition Condition);

/// <summary>
/// Immutable explicit grant state and its standing-dependent effectiveness.
/// </summary>
public sealed record RelationshipGrantSnapshot(
    RelationshipGrantId Id,
    PrincipalId IssuerPrincipalId,
    PrincipalId HolderPrincipalId,
    RelationshipGrantKind Kind,
    StandingBand MinimumStandingBand,
    bool IsIssued,
    bool IsEffective);

/// <summary>
/// Complete authoritative relationship diagnostics at one commit boundary.
/// </summary>
public sealed record RelationshipSnapshot(
    PrincipalId PlayerPrincipalId,
    StandingPolicyId StandingPolicyId,
    IReadOnlyList<PrincipalSnapshot> Principals,
    IReadOnlyList<StandingSnapshot> Standings,
    IReadOnlyList<DiplomaticConditionSnapshot> DiplomaticConditions,
    IReadOnlyList<RelationshipGrantSnapshot> Grants);

/// <summary>
/// Deterministic owner of principal identity and directional standing state.
/// </summary>
