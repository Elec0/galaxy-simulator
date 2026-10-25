namespace GalaxyCommand.Simulation;

public sealed partial class GameSession
{
    /// <summary>Captures immutable current movement evidence. Evaluation, unhealthy, and disposed sessions reject access.</summary>
    public InteractionSpatialReadView CaptureSpatialInteractionView()
    {
        EnsureHealthy();
        return _runtime.CaptureInteractionReadView();
    }

    /// <summary>
    /// Atomically hands off or updates intervals for a registered domain at CurrentTime.
    /// The caller owns authorization, roster, and order policy. No renewal work is scheduled.
    /// Missing/stale/conflicting claims return false; invalid input throws before mutation.
    /// Successful direct handoffs reconcile player observation at the completed boundary.
    /// </summary>
    public bool TryHandoffInteractionMotions(IEnumerable<InteractionMotionRequest> requests, InteractionMotionOwner owner)
    {
        EnsureHealthy();
        RequireInteractionMotionDomain(owner);
        bool committed = _runtime.TryHandoffInteractionMotions(requests, owner);
        if (committed) CompletePlayerDisclosureIfIdle();
        return committed;
    }

    /// <summary>
    /// Atomically releases a registered domain's matching intervals at CurrentTime and returns
    /// immutable identity-ordered exact kinematics. Rejected claims return false with null results.
    /// This chooses no replacement orders or navigation plans.
    /// Player observation joins the released, materialized position at the completed boundary.
    /// </summary>
    public bool TryReleaseInteractionMotions(IEnumerable<InteractionMotionReleaseRequest> requests, InteractionMotionOwner owner,
        out IReadOnlyDictionary<ShipId, ShipKinematicState>? released)
    {
        EnsureHealthy();
        RequireInteractionMotionDomain(owner);
        bool committed = _runtime.TryReleaseInteractionMotions(requests, owner, out released);
        if (committed) CompletePlayerDisclosureIfIdle();
        return committed;
    }

    /// <summary>Requires a live registration without allocating domain identity or inferring gameplay authority.</summary>
    private void RequireInteractionMotionDomain(InteractionMotionOwner owner)
    {
        if (_spatialInteractions.Get(owner.DomainKind) is null)
            throw new InvalidOperationException("Register the spatial interaction domain before controlling motion.");
    }
}
