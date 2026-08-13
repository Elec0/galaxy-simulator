using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

internal sealed record ResolvedRuntimePolicies(
    ISpatialNavigationPlanner Navigation,
    IReadOnlyList<ShipMaterializationPolicy> MaterializationPolicies,
    int FactRetentionCapacity);

/// <summary>
/// Stable capture and resolution boundary for injected behavior that can alter
/// future authoritative results after load.
/// </summary>
internal static class RuntimePolicyManifest
{
    private const string DirectLocalKind = "direct-local";
    private const string HierarchicalKind = "hierarchical";
    private const string ChebyshevTravelTimeKind = "chebyshev-map-distance";
    private const int CurrentBehaviorVersion = 1;
    private const string Path = "$.checkpoint.runtimePolicies";

    /// <summary>
    /// Captures only registered policy implementations and exact configuration;
    /// arbitrary interface implementations are explicitly unsupported for saves.
    /// </summary>
    internal static CheckpointResult<RuntimePolicyManifestCheckpoint> Capture(
        WorldTopology topology,
        ISpatialNavigationPlanner navigation,
        IEnumerable<ShipMaterializationPolicy> materializationPolicies,
        int factRetentionCapacity)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(materializationPolicies);
        if (factRetentionCapacity <= 0)
        {
            return RejectedManifest(
                $"{Path}.factRetentionCapacity",
                "Fact retention capacity must be positive.");
        }

        string navigationKind;
        ILocalTravelTimeEstimator travelTime;
        switch (navigation)
        {
            case DirectLocalNavigationPlanner direct:
                navigationKind = DirectLocalKind;
                travelTime = direct.TravelTime;
                break;
            case HierarchicalNavigationPlanner hierarchical:
                if (!ReferenceEquals(hierarchical.Topology, topology.Connectors))
                {
                    return RejectedManifest(
                        $"{Path}.navigation.topology",
                        "The hierarchical planner is not bound to the authoritative topology.");
                }

                navigationKind = HierarchicalKind;
                travelTime = hierarchical.TravelTime;
                break;
            default:
                return RejectedManifest(
                    $"{Path}.navigation",
                    "The navigation implementation is not registered for save compatibility.");
        }

        if (travelTime is not ChebyshevLocalTravelTimeEstimator chebyshev)
        {
            return RejectedManifest(
                $"{Path}.travelTime",
                "The travel-time implementation is not registered for save compatibility.");
        }

        MaterializationPolicyCheckpoint[] policies = materializationPolicies
            .OrderBy(policy => policy.FacilityId.Value)
            .Select(CaptureMaterializationPolicy)
            .ToArray();
        return CheckpointResult<RuntimePolicyManifestCheckpoint>.Success(
            new RuntimePolicyManifestCheckpoint(
                new NavigationPolicyCheckpoint(
                    navigationKind,
                    CurrentBehaviorVersion),
                new TravelTimePolicyCheckpoint(
                    ChebyshevTravelTimeKind,
                    CurrentBehaviorVersion,
                    chebyshev.MillisecondsPerMapUnit),
                new ReadOnlyCollection<MaterializationPolicyCheckpoint?>(policies),
                factRetentionCapacity));
    }

    /// <summary>
    /// Resolves only exact registered kind and version pairs, validates all
    /// materialization references, and returns isolated runtime policies.
    /// </summary>
    internal static CheckpointResult<ResolvedRuntimePolicies> Resolve(
        RuntimePolicyManifestCheckpoint checkpoint,
        WorldTopology topology,
        IEnumerable<PrincipalId> registeredPrincipals)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(registeredPrincipals);
        if (checkpoint.FactRetentionCapacity <= 0)
        {
            return RejectedResolved(
                $"{Path}.factRetentionCapacity",
                "Fact retention capacity must be positive.");
        }

        CheckpointResult<ILocalTravelTimeEstimator> travelResult =
            ResolveTravelTime(checkpoint.TravelTime);
        if (!travelResult.IsSuccess)
        {
            return CheckpointResult<ResolvedRuntimePolicies>.Rejected(travelResult.Failure!);
        }

        CheckpointResult<ISpatialNavigationPlanner> navigationResult = ResolveNavigation(
            checkpoint.Navigation,
            topology,
            travelResult.Value!);
        if (!navigationResult.IsSuccess)
        {
            return CheckpointResult<ResolvedRuntimePolicies>.Rejected(
                navigationResult.Failure!);
        }

        HashSet<SystemId> systems = topology.Systems.Select(system => system.Id).ToHashSet();
        HashSet<PrincipalId> principals = registeredPrincipals.ToHashSet();
        CheckpointResult<IReadOnlyList<ShipMaterializationPolicy>> materializationResult =
            ResolveMaterializationPolicies(checkpoint, systems, principals);
        if (!materializationResult.IsSuccess)
        {
            return CheckpointResult<ResolvedRuntimePolicies>.Rejected(
                materializationResult.Failure!);
        }

        return CheckpointResult<ResolvedRuntimePolicies>.Success(
            new ResolvedRuntimePolicies(
                navigationResult.Value!,
                materializationResult.Value!,
                checkpoint.FactRetentionCapacity));
    }

    private static MaterializationPolicyCheckpoint CaptureMaterializationPolicy(
        ShipMaterializationPolicy policy) =>
        new(
            policy.FacilityId,
            policy.PrincipalId,
            policy.Position.SystemId,
            policy.Position.Position.X,
            policy.Position.Position.Y,
            policy.BaseController.Kind,
            policy.BaseController.Id.Value,
            policy.InitialOrderPolicy,
            new ReadOnlyCollection<ShipDesignPolicyCheckpoint?>(policy.AllowedDesigns.Values
                .Select(design => new ShipDesignPolicyCheckpoint(
                    design.Id,
                    design.Name,
                    new ReadOnlyCollection<ConstructionInputPolicyCheckpoint?>(
                        design.Recipe.Inputs.Select(input =>
                            new ConstructionInputPolicyCheckpoint(
                                input.Key,
                                input.Value)).ToArray()),
                    design.Recipe.RequiredWork,
                    design.CargoCapacity))
                .ToArray()));

    /// <summary>
    /// Resolves the closed travel-time vocabulary without using runtime type names.
    /// </summary>
    private static CheckpointResult<ILocalTravelTimeEstimator> ResolveTravelTime(
        TravelTimePolicyCheckpoint? checkpoint)
    {
        if (checkpoint is null || checkpoint.Kind != ChebyshevTravelTimeKind)
        {
            return RejectedTravel(
                $"{Path}.travelTime.kind",
                "The travel-time policy kind is unavailable.");
        }

        if (checkpoint.BehaviorVersion != CurrentBehaviorVersion)
        {
            return RejectedTravel(
                $"{Path}.travelTime.behaviorVersion",
                "The travel-time behavior version is unavailable.");
        }

        if (checkpoint.MillisecondsPerMapUnit == 0)
        {
            return RejectedTravel(
                $"{Path}.travelTime.millisecondsPerMapUnit",
                "Milliseconds per map unit must be positive.");
        }

        return CheckpointResult<ILocalTravelTimeEstimator>.Success(
            new ChebyshevLocalTravelTimeEstimator(checkpoint.MillisecondsPerMapUnit));
    }

    /// <summary>
    /// Binds a registered planner implementation to the restored authoritative topology.
    /// </summary>
    private static CheckpointResult<ISpatialNavigationPlanner> ResolveNavigation(
        NavigationPolicyCheckpoint? checkpoint,
        WorldTopology topology,
        ILocalTravelTimeEstimator travelTime)
    {
        if (checkpoint is null
            || checkpoint.Kind is not (DirectLocalKind or HierarchicalKind))
        {
            return RejectedNavigation(
                $"{Path}.navigation.kind",
                "The navigation policy kind is unavailable.");
        }

        if (checkpoint.BehaviorVersion != CurrentBehaviorVersion)
        {
            return RejectedNavigation(
                $"{Path}.navigation.behaviorVersion",
                "The navigation behavior version is unavailable.");
        }

        ISpatialNavigationPlanner navigation = checkpoint.Kind == DirectLocalKind
            ? new DirectLocalNavigationPlanner(travelTime)
            : new HierarchicalNavigationPlanner(topology.Connectors, travelTime);
        return CheckpointResult<ISpatialNavigationPlanner>.Success(navigation);
    }

    /// <summary>
    /// Reconstructs exact facility policy and design definitions only after all
    /// identities, content values, and cross-owner references validate.
    /// </summary>
    private static CheckpointResult<IReadOnlyList<ShipMaterializationPolicy>>
        ResolveMaterializationPolicies(
            RuntimePolicyManifestCheckpoint checkpoint,
            HashSet<SystemId> systems,
            HashSet<PrincipalId> principals)
    {
        if (checkpoint.MaterializationPolicies is null)
        {
            return RejectedMaterialization(Path, "Materialization policies are required.");
        }

        var facilityIds = new HashSet<FacilityId>();
        var policies = new List<ShipMaterializationPolicy>();
        for (int index = 0; index < checkpoint.MaterializationPolicies.Count; index++)
        {
            MaterializationPolicyCheckpoint? policy =
                checkpoint.MaterializationPolicies[index];
            string policyPath = $"{Path}.materializationPolicies[{index}]";
            if (policy is null || policy.FacilityId.Value == 0
                || !facilityIds.Add(policy.FacilityId))
            {
                return RejectedMaterialization(
                    $"{policyPath}.facilityId",
                    "The facility identity is missing or duplicated.");
            }

            if (!principals.Contains(policy.PrincipalId))
            {
                return RejectedMaterialization(
                    $"{policyPath}.principalId",
                    "The materialization principal is not registered.");
            }

            if (!systems.Contains(policy.SystemId))
            {
                return RejectedMaterialization(
                    $"{policyPath}.systemId",
                    "The materialization system is not registered.");
            }

            if (!Enum.IsDefined(policy.BaseControllerKind)
                || policy.BaseControllerKind == ActorControllerKind.Script
                || string.IsNullOrWhiteSpace(policy.BaseControllerId)
                || policy.InitialOrderPolicy != InitialShipOrderPolicy.NoInitialOrder)
            {
                return RejectedMaterialization(
                    policyPath,
                    "The controller or initial-order policy is invalid.");
            }

            CheckpointResult<IReadOnlyList<ShipDesign>> designsResult =
                ResolveDesigns(policy, policyPath);
            if (!designsResult.IsSuccess)
            {
                return CheckpointResult<IReadOnlyList<ShipMaterializationPolicy>>.Rejected(
                    designsResult.Failure!);
            }

            policies.Add(new ShipMaterializationPolicy(
                policy.FacilityId,
                policy.PrincipalId,
                new SystemPosition(
                    policy.SystemId,
                    new SpatialPosition(policy.X, policy.Y)),
                new ActorController(
                    policy.BaseControllerKind,
                    new CommandSourceId(policy.BaseControllerId)),
                policy.InitialOrderPolicy,
                designsResult.Value!));
        }

        policies.Sort((left, right) => left.FacilityId.Value.CompareTo(right.FacilityId.Value));
        return CheckpointResult<IReadOnlyList<ShipMaterializationPolicy>>.Success(
            new ReadOnlyCollection<ShipMaterializationPolicy>(policies));
    }

    /// <summary>
    /// Restores complete ship design definitions with unique ordered material inputs.
    /// </summary>
    private static CheckpointResult<IReadOnlyList<ShipDesign>> ResolveDesigns(
        MaterializationPolicyCheckpoint policy,
        string policyPath)
    {
        if (policy.AllowedDesigns is null || policy.AllowedDesigns.Count == 0)
        {
            return RejectedDesigns(
                $"{policyPath}.allowedDesigns",
                "At least one allowed ship design is required.");
        }

        var ids = new HashSet<ConstructionDesignId>();
        var designs = new List<ShipDesign>();
        for (int index = 0; index < policy.AllowedDesigns.Count; index++)
        {
            ShipDesignPolicyCheckpoint? design = policy.AllowedDesigns[index];
            string designPath = $"{policyPath}.allowedDesigns[{index}]";
            if (design is null || design.Id.Value == 0 || !ids.Add(design.Id))
            {
                return RejectedDesigns(
                    $"{designPath}.id",
                    "The ship design identity is missing or duplicated.");
            }

            if (string.IsNullOrWhiteSpace(design.Name) || design.Inputs is null)
            {
                return RejectedDesigns(designPath, "The ship design definition is incomplete.");
            }

            var inputs = new SortedDictionary<MaterialId, Quantity>(
                EntityIdComparer<MaterialId>.Instance);
            for (int inputIndex = 0; inputIndex < design.Inputs.Count; inputIndex++)
            {
                ConstructionInputPolicyCheckpoint? input = design.Inputs[inputIndex];
                if (input is null || input.MaterialId.Value == 0
                    || !inputs.TryAdd(input.MaterialId, input.Quantity))
                {
                    return RejectedDesigns(
                        $"{designPath}.inputs[{inputIndex}].materialId",
                        "The material input identity is missing or duplicated.");
                }
            }

            designs.Add(new ShipDesign(
                design.Id,
                design.Name,
                new ConstructionRecipe(inputs, design.RequiredWork),
                design.CargoCapacity));
        }

        designs.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
        return CheckpointResult<IReadOnlyList<ShipDesign>>.Success(
            new ReadOnlyCollection<ShipDesign>(designs));
    }

    private static CheckpointResult<T> Rejected<T>(string path, string message)
        where T : class =>
        CheckpointResult<T>.Rejected(new CheckpointValidationFailure(path, message));

    private static CheckpointResult<RuntimePolicyManifestCheckpoint> RejectedManifest(
        string path,
        string message) => Rejected<RuntimePolicyManifestCheckpoint>(path, message);

    private static CheckpointResult<ResolvedRuntimePolicies> RejectedResolved(
        string path,
        string message) => Rejected<ResolvedRuntimePolicies>(path, message);

    private static CheckpointResult<ILocalTravelTimeEstimator> RejectedTravel(
        string path,
        string message) => Rejected<ILocalTravelTimeEstimator>(path, message);

    private static CheckpointResult<ISpatialNavigationPlanner> RejectedNavigation(
        string path,
        string message) => Rejected<ISpatialNavigationPlanner>(path, message);

    private static CheckpointResult<IReadOnlyList<ShipMaterializationPolicy>>
        RejectedMaterialization(string path, string message) =>
        Rejected<IReadOnlyList<ShipMaterializationPolicy>>(path, message);

    private static CheckpointResult<IReadOnlyList<ShipDesign>> RejectedDesigns(
        string path,
        string message) => Rejected<IReadOnlyList<ShipDesign>>(path, message);
}
