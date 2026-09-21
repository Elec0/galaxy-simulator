using System.Collections.ObjectModel;
using System.Globalization;

namespace GalaxyCommand.Simulation;

public sealed record StandingChangedFact : GameFact
{
    /// <summary>
    /// Creates a fact from one changed prepared standing outcome.
    /// </summary>
    public StandingChangedFact(StandingChangeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentOutOfRangeException.ThrowIfZero(outcome.AssessingPrincipalId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(outcome.SubjectPrincipalId.Value);
        if (!outcome.Changed)
        {
            throw new ArgumentException(
                "A standing-change fact requires an authoritative value change.",
                nameof(outcome));
        }

        AssessingPrincipalId = outcome.AssessingPrincipalId;
        SubjectPrincipalId = outcome.SubjectPrincipalId;
        PriorValue = outcome.PriorValue;
        PriorBand = outcome.PriorBand;
        CombinedDelta = outcome.CombinedDelta;
        ResultingValue = outcome.ResultingValue;
        ResultingBand = outcome.ResultingBand;
        Contributions = GameSnapshotCollection.Copy(outcome.Contributions);
    }

    public PrincipalId AssessingPrincipalId { get; }

    public PrincipalId SubjectPrincipalId { get; }

    public StandingValue PriorValue { get; }

    public StandingBand PriorBand { get; }

    public long CombinedDelta { get; }

    public StandingValue ResultingValue { get; }

    public StandingBand ResultingBand { get; }

    public IReadOnlyList<StandingChangeContribution> Contributions { get; }
}

/// <summary>
/// Semantic record of one mutual diplomatic condition changing.
/// </summary>
public sealed record DiplomaticConditionChangedFact : GameFact
{
    /// <summary>
    /// Creates a fact from one changed diplomatic outcome.
    /// </summary>
    public DiplomaticConditionChangedFact(DiplomaticConditionChangeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!outcome.Changed)
        {
            throw new ArgumentException(
                "A diplomatic fact requires an authoritative condition change.",
                nameof(outcome));
        }

        LowerPrincipalId = outcome.LowerPrincipalId;
        UpperPrincipalId = outcome.UpperPrincipalId;
        PriorCondition = outcome.PriorCondition;
        ResultingCondition = outcome.ResultingCondition;
        Reason = outcome.Reason;
    }

    public PrincipalId LowerPrincipalId { get; }

    public PrincipalId UpperPrincipalId { get; }

    public DiplomaticCondition PriorCondition { get; }

    public DiplomaticCondition ResultingCondition { get; }

    public RelationshipPolicyChangeReason Reason { get; }
}

/// <summary>
/// Semantic record of one explicit relationship grant being issued.
/// </summary>
public sealed record RelationshipGrantIssuedFact : GameFact
{
    /// <summary>
    /// Creates a fact from one grant issuance outcome.
    /// </summary>
    public RelationshipGrantIssuedFact(RelationshipGrantChangeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.PriorIssued || !outcome.ResultingIssued)
        {
            throw new ArgumentException("A grant-issued fact requires issuance.", nameof(outcome));
        }

        Id = outcome.Id;
        IssuerPrincipalId = outcome.IssuerPrincipalId;
        HolderPrincipalId = outcome.HolderPrincipalId;
        Kind = outcome.Kind;
        MinimumStandingBand = outcome.MinimumStandingBand;
        Reason = outcome.Reason;
    }

    public RelationshipGrantId Id { get; }

    public PrincipalId IssuerPrincipalId { get; }

    public PrincipalId HolderPrincipalId { get; }

    public RelationshipGrantKind Kind { get; }

    public StandingBand MinimumStandingBand { get; }

    public RelationshipPolicyChangeReason Reason { get; }
}

/// <summary>
/// Semantic record of one explicit relationship grant being revoked.
/// </summary>
public sealed record RelationshipGrantRevokedFact : GameFact
{
    /// <summary>
    /// Creates a fact from one grant revocation outcome.
    /// </summary>
    public RelationshipGrantRevokedFact(RelationshipGrantChangeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!outcome.PriorIssued || outcome.ResultingIssued)
        {
            throw new ArgumentException("A grant-revoked fact requires revocation.", nameof(outcome));
        }

        Id = outcome.Id;
        Reason = outcome.Reason;
    }

    public RelationshipGrantId Id { get; }

    public RelationshipPolicyChangeReason Reason { get; }
}

