using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

public sealed class RelationshipSetup
{
    /// <summary>
    /// Validates and canonicalizes principal definitions and standing overrides.
    /// </summary>
    public RelationshipSetup(
        IEnumerable<PrincipalDefinition> principals,
        PrincipalId playerPrincipalId,
        StandingPolicy standingPolicy,
        IEnumerable<InitialStandingSetup> standings)
        : this(principals, playerPrincipalId, standingPolicy, standings, [], [])
    {
    }

    /// <summary>
    /// Validates and canonicalizes complete principal, standing, diplomacy, and
    /// initial issued-grant state.
    /// </summary>
    public RelationshipSetup(
        IEnumerable<PrincipalDefinition> principals,
        PrincipalId playerPrincipalId,
        StandingPolicy standingPolicy,
        IEnumerable<InitialStandingSetup> standings,
        IEnumerable<InitialDiplomaticConditionSetup> diplomaticConditions,
        IEnumerable<InitialRelationshipGrantSetup> grants)
    {
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentOutOfRangeException.ThrowIfZero(playerPrincipalId.Value);
        ArgumentNullException.ThrowIfNull(standingPolicy);
        ArgumentNullException.ThrowIfNull(standings);
        ArgumentNullException.ThrowIfNull(diplomaticConditions);
        ArgumentNullException.ThrowIfNull(grants);

        PrincipalDefinition[] principalValues = principals.ToArray();
        foreach (PrincipalDefinition principal in principalValues)
        {
            ArgumentNullException.ThrowIfNull(principal);
        }

        Array.Sort(
            principalValues,
            (left, right) => left.Id.Value.CompareTo(right.Id.Value));
        var principalIds = new HashSet<PrincipalId>();
        var contentIds = new HashSet<PrincipalContentId>();
        foreach (PrincipalDefinition principal in principalValues)
        {
            if (!principalIds.Add(principal.Id))
            {
                throw new ArgumentException(
                    $"Duplicate principal {principal.Id}.",
                    nameof(principals));
            }

            if (!contentIds.Add(principal.ContentId))
            {
                throw new ArgumentException(
                    $"Duplicate principal content identity {principal.ContentId}.",
                    nameof(principals));
            }
        }

        if (!principalIds.Contains(playerPrincipalId))
        {
            throw new ArgumentException(
                $"Player principal {playerPrincipalId} is not registered.",
                nameof(playerPrincipalId));
        }

        InitialStandingSetup[] standingValues = standings.ToArray();
        foreach (InitialStandingSetup standing in standingValues)
        {
            ArgumentNullException.ThrowIfNull(standing);
        }

        Array.Sort(
            standingValues,
            (left, right) =>
            {
                int assessing = left.AssessingPrincipalId.Value.CompareTo(
                    right.AssessingPrincipalId.Value);
                return assessing != 0
                    ? assessing
                    : left.SubjectPrincipalId.Value.CompareTo(
                        right.SubjectPrincipalId.Value);
            });
        var standingKeys = new HashSet<(PrincipalId Assessing, PrincipalId Subject)>();
        foreach (InitialStandingSetup standing in standingValues)
        {
            if (!principalIds.Contains(standing.AssessingPrincipalId)
                || !principalIds.Contains(standing.SubjectPrincipalId))
            {
                throw new ArgumentException(
                    "Initial standing references an unknown principal.",
                    nameof(standings));
            }

            if (standing.Value.Value < standingPolicy.Minimum.Value
                || standing.Value.Value > standingPolicy.Maximum.Value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(standings),
                    standing.Value,
                    "Initial standing must be within the configured bounds.");
            }

            if (!standingKeys.Add((
                    standing.AssessingPrincipalId,
                    standing.SubjectPrincipalId)))
            {
                throw new ArgumentException(
                    "Duplicate initial directional standing.",
                    nameof(standings));
            }
        }

        InitialDiplomaticConditionSetup[] diplomaticValues = diplomaticConditions.ToArray();
        foreach (InitialDiplomaticConditionSetup diplomatic in diplomaticValues)
        {
            ArgumentNullException.ThrowIfNull(diplomatic);
        }

        Array.Sort(
            diplomaticValues,
            (left, right) =>
            {
                int lower = left.LowerPrincipalId.Value.CompareTo(
                    right.LowerPrincipalId.Value);
                return lower != 0
                    ? lower
                    : left.UpperPrincipalId.Value.CompareTo(right.UpperPrincipalId.Value);
            });
        var diplomaticKeys = new HashSet<(PrincipalId Lower, PrincipalId Upper)>();
        foreach (InitialDiplomaticConditionSetup diplomatic in diplomaticValues)
        {
            if (!principalIds.Contains(diplomatic.LowerPrincipalId)
                || !principalIds.Contains(diplomatic.UpperPrincipalId))
            {
                throw new ArgumentException(
                    "Initial diplomacy references an unknown principal.",
                    nameof(diplomaticConditions));
            }

            if (!diplomaticKeys.Add((
                    diplomatic.LowerPrincipalId,
                    diplomatic.UpperPrincipalId)))
            {
                throw new ArgumentException(
                    "Duplicate initial diplomatic pair.",
                    nameof(diplomaticConditions));
            }
        }

        InitialRelationshipGrantSetup[] grantValues = grants.ToArray();
        foreach (InitialRelationshipGrantSetup grant in grantValues)
        {
            ArgumentNullException.ThrowIfNull(grant);
        }

        Array.Sort(grantValues, (left, right) => left.Id.Value.CompareTo(right.Id.Value));
        var grantIds = new HashSet<RelationshipGrantId>();
        foreach (InitialRelationshipGrantSetup grant in grantValues)
        {
            if (!principalIds.Contains(grant.IssuerPrincipalId)
                || !principalIds.Contains(grant.HolderPrincipalId))
            {
                throw new ArgumentException(
                    "Initial relationship grant references an unknown principal.",
                    nameof(grants));
            }

            if (!grantIds.Add(grant.Id))
            {
                throw new ArgumentException(
                    $"Duplicate initial relationship grant {grant.Id.Value}.",
                    nameof(grants));
            }
        }

        Principals = new ReadOnlyCollection<PrincipalDefinition>(principalValues);
        PlayerPrincipalId = playerPrincipalId;
        StandingPolicy = standingPolicy;
        Standings = new ReadOnlyCollection<InitialStandingSetup>(standingValues);
        DiplomaticConditions = new ReadOnlyCollection<InitialDiplomaticConditionSetup>(
            diplomaticValues);
        Grants = new ReadOnlyCollection<InitialRelationshipGrantSetup>(grantValues);
    }

    public IReadOnlyList<PrincipalDefinition> Principals { get; }

    public PrincipalId PlayerPrincipalId { get; }

    public StandingPolicy StandingPolicy { get; }

    public IReadOnlyList<InitialStandingSetup> Standings { get; }

    public IReadOnlyList<InitialDiplomaticConditionSetup> DiplomaticConditions { get; }

    public IReadOnlyList<InitialRelationshipGrantSetup> Grants { get; }
}

/// <summary>
/// Immutable diagnostic snapshot of one registered principal.
/// </summary>
