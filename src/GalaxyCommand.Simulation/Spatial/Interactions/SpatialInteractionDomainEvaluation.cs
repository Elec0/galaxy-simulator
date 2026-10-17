namespace GalaxyCommand.Simulation;

/// <summary>Domain-owned pure evaluation of eligible contact evidence from one stable wave.</summary>
public interface ISpatialInteractionDomainEvaluator
{
    /// <summary>
    /// Reads stable post-completion state and contact evidence for this domain.
    /// Must not mutate authoritative state. Returns only effects correlated to
    /// supplied contacts, with distinct stable effect kinds and local ordinals.
    /// The runtime copies proposals before any effect commits.
    /// </summary>
    IReadOnlyList<SpatialInteractionEffectProposal> Evaluate(InteractionSpatialReadView view,
        IReadOnlyList<InteractionCandidate> contacts);
}

/// <summary>Domain-owned buffered effect committed through authoritative session APIs.</summary>
public interface ISpatialInteractionEffect
{
    /// <summary>
    /// Commits through the appropriate authoritative owner at the contact timestamp.
    /// The domain retains eligibility and conflict policy. A failure closes the
    /// session to further mutation and checkpoints; partial effects are not rolled back.
    /// </summary>
    void Commit(GameSession session);
}

/// <summary>Stable per-contact effect order, independent of evaluation completion order.</summary>
public sealed record SpatialInteractionEffectProposal(InteractionCandidateKey CandidateKey,
    int EffectKind, uint LocalOrdinal, ISpatialInteractionEffect Effect);
