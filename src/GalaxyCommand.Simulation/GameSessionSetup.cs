using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// Initial authoritative spatial state for one ship. Runtime spawning remains
/// outside this setup-only contract.
/// </summary>
public sealed record InitialShipSetup
{
    /// <summary>
    /// Creates validated initial state for one principal-owned ship.
    /// </summary>
    public InitialShipSetup(
        EntityId entityId,
        ShipId id,
        InventoryId cargoInventoryId,
        PrincipalId principalId,
        ShipDesign design,
        SystemPosition position,
        ActorController baseController)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entityId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(id.Value);
        ArgumentOutOfRangeException.ThrowIfZero(cargoInventoryId.Value);
        ArgumentOutOfRangeException.ThrowIfZero(principalId.Value);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentOutOfRangeException.ThrowIfZero(position.SystemId.Value);
        ArgumentNullException.ThrowIfNull(baseController);
        if (baseController.Kind == ActorControllerKind.Script)
        {
            throw new ArgumentException(
                "A script cannot be an actor's persistent base controller.",
                nameof(baseController));
        }

        EntityId = entityId;
        Id = id;
        CargoInventoryId = cargoInventoryId;
        PrincipalId = principalId;
        Design = design;
        Position = position;
        BaseController = baseController;
    }

    public EntityId EntityId { get; }

    public ShipId Id { get; }

    public InventoryId CargoInventoryId { get; }

    public PrincipalId PrincipalId { get; }

    public ShipDesign Design { get; }

    public SystemPosition Position { get; }

    public ActorController BaseController { get; }
}

/// <summary>
/// Explicit immutable input used to construct a clean game session.
/// </summary>
public sealed class GameSessionSetup
{
    /// <summary>
    /// Creates setup with a resolved random seed and without connector
    /// topology or materialization policies.
    /// </summary>
    public GameSessionSetup(
        IEnumerable<StarSystem> systems,
        IEnumerable<InitialShipSetup> ships,
        RelationshipSetup relationships,
        RandomRootSeed randomRootSeed,
        int factRetentionCapacity)
        : this(
            systems,
            ships,
            new ConnectorTopology(
                Array.Empty<ConnectorEndpoint>(),
                Array.Empty<TransitConnection>()),
            Array.Empty<ShipMaterializationPolicy>(),
            null,
            relationships,
            randomRootSeed,
            factRetentionCapacity)
    {
    }

    /// <summary>
    /// Creates setup with a resolved random seed, connector topology, and no
    /// materialization policies.
    /// </summary>
    public GameSessionSetup(
        IEnumerable<StarSystem> systems,
        IEnumerable<InitialShipSetup> ships,
        ConnectorTopology connectorTopology,
        RelationshipSetup relationships,
        RandomRootSeed randomRootSeed,
        int factRetentionCapacity)
        : this(
            systems,
            ships,
            connectorTopology,
            Array.Empty<ShipMaterializationPolicy>(),
            null,
            relationships,
            randomRootSeed,
            factRetentionCapacity)
    {
    }

    /// <summary>
    /// Validates and canonicalizes the complete initial clean-session state,
    /// including its required resolved random seed.
    /// </summary>
    public GameSessionSetup(
        IEnumerable<StarSystem> systems,
        IEnumerable<InitialShipSetup> ships,
        ConnectorTopology connectorTopology,
        IEnumerable<ShipMaterializationPolicy> materializationPolicies,
        RelationshipSetup relationships,
        RandomRootSeed randomRootSeed,
        int factRetentionCapacity)
        : this(
            systems,
            ships,
            connectorTopology,
            materializationPolicies,
            null,
            relationships,
            randomRootSeed,
            factRetentionCapacity)
    {
    }

    /// <summary>
    /// Validates and canonicalizes complete initial state, including optional
    /// session-owned economic configuration and the required resolved random
    /// seed.
    /// </summary>
    public GameSessionSetup(
        IEnumerable<StarSystem> systems,
        IEnumerable<InitialShipSetup> ships,
        ConnectorTopology connectorTopology,
        IEnumerable<ShipMaterializationPolicy> materializationPolicies,
        GameSessionEconomySetup? economy,
        RelationshipSetup relationships,
        RandomRootSeed randomRootSeed,
        int factRetentionCapacity)
    {
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(ships);
        ArgumentNullException.ThrowIfNull(connectorTopology);
        ArgumentNullException.ThrowIfNull(materializationPolicies);
        ArgumentNullException.ThrowIfNull(relationships);
        ArgumentNullException.ThrowIfNull(randomRootSeed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            factRetentionCapacity);

        StarSystem[] systemValues = systems.ToArray();
        InitialShipSetup[] shipValues = ships.ToArray();
        ShipMaterializationPolicy[] policyValues = materializationPolicies.ToArray();
        var principalIds = new HashSet<PrincipalId>(
            relationships.Principals.Select(principal => principal.Id));
        var systemIds = new HashSet<SystemId>();
        foreach (StarSystem system in systemValues)
        {
            ArgumentNullException.ThrowIfNull(system);
            if (!systemIds.Add(system.Id))
            {
                throw new ArgumentException(
                    $"Duplicate system {system.Id}.",
                    nameof(systems));
            }
        }

        var shipIds = new HashSet<ShipId>();
        var entityIds = new HashSet<EntityId>();
        var cargoInventoryIds = new HashSet<InventoryId>();
        foreach (InitialShipSetup ship in shipValues)
        {
            ArgumentNullException.ThrowIfNull(ship);
            if (!shipIds.Add(ship.Id))
            {
                throw new ArgumentException(
                    $"Duplicate ship {ship.Id}.",
                    nameof(ships));
            }

            if (!entityIds.Add(ship.EntityId))
            {
                throw new ArgumentException(
                    $"Duplicate entity {ship.EntityId}.",
                    nameof(ships));
            }

            if (!cargoInventoryIds.Add(ship.CargoInventoryId))
            {
                throw new ArgumentException(
                    $"Duplicate cargo inventory {ship.CargoInventoryId}.",
                    nameof(ships));
            }

            if (!systemIds.Contains(ship.Position.SystemId))
            {
                throw new ArgumentException(
                    $"Ship {ship.Id} references unknown system {ship.Position.SystemId}.",
                    nameof(ships));
            }

            if (!principalIds.Contains(ship.PrincipalId))
            {
                throw new ArgumentException(
                    $"Ship {ship.Id} references unknown principal {ship.PrincipalId}.",
                    nameof(ships));
            }
        }

        foreach (ConnectorEndpoint endpoint in connectorTopology.Endpoints)
        {
            if (!systemIds.Contains(endpoint.Position.SystemId))
            {
                throw new ArgumentException(
                    $"Connector endpoint {endpoint.Id} references unknown system {endpoint.Position.SystemId}.",
                    nameof(connectorTopology));
            }
        }

        var policyFacilityIds = new HashSet<FacilityId>();
        foreach (ShipMaterializationPolicy policy in policyValues)
        {
            ArgumentNullException.ThrowIfNull(policy);
            if (!policyFacilityIds.Add(policy.FacilityId))
            {
                throw new ArgumentException(
                    $"Duplicate materialization policy for facility {policy.FacilityId}.",
                    nameof(materializationPolicies));
            }

            if (!systemIds.Contains(policy.Position.SystemId))
            {
                throw new ArgumentException(
                    $"Materialization policy {policy.FacilityId} references unknown system {policy.Position.SystemId}.",
                    nameof(materializationPolicies));
            }

            if (!principalIds.Contains(policy.PrincipalId))
            {
                throw new ArgumentException(
                    $"Materialization policy {policy.FacilityId} references unknown principal {policy.PrincipalId}.",
                    nameof(materializationPolicies));
            }
        }

        ValidateEconomy(
            economy,
            systemIds,
            shipIds,
            cargoInventoryIds,
            principalIds,
            policyValues);

        Systems = new ReadOnlyCollection<StarSystem>(systemValues);
        Ships = new ReadOnlyCollection<InitialShipSetup>(shipValues);
        ConnectorTopology = connectorTopology;
        MaterializationPolicies =
            new ReadOnlyCollection<ShipMaterializationPolicy>(policyValues);
        Economy = economy;
        Relationships = relationships;
        RandomRootSeed = randomRootSeed;
        FactRetentionCapacity = factRetentionCapacity;
    }

    public IReadOnlyList<StarSystem> Systems { get; }

    public IReadOnlyList<InitialShipSetup> Ships { get; }

    public ConnectorTopology ConnectorTopology { get; }

    public IReadOnlyList<ShipMaterializationPolicy> MaterializationPolicies { get; }

    /// <summary>
    /// Gets the optional generic new-game economic seed used to construct the
    /// session's private economy, construction, and transport owners.
    /// </summary>
    public GameSessionEconomySetup? Economy { get; }

    public RelationshipSetup Relationships { get; }

    /// <summary>
    /// Gets the resolved authoritative seed required before session creation.
    /// </summary>
    public RandomRootSeed RandomRootSeed { get; }

    public int FactRetentionCapacity { get; }

    private static void ValidateEconomy(
        GameSessionEconomySetup? economy,
        HashSet<SystemId> systemIds,
        HashSet<ShipId> shipIds,
        HashSet<InventoryId> cargoInventoryIds,
        HashSet<PrincipalId> principalIds,
        IEnumerable<ShipMaterializationPolicy> policies)
    {
        if (economy is null)
        {
            return;
        }

        var facilityIds = new HashSet<FacilityId>();
        var policiesByFacility = policies.ToDictionary(policy => policy.FacilityId);
        foreach (EconomyFacilitySetup facility in economy.Facilities)
        {
            if (!systemIds.Contains(facility.Position.SystemId)
                || cargoInventoryIds.Contains(facility.InventoryId)
                || !facilityIds.Add(facility.FacilityId))
            {
                throw new ArgumentException(
                    $"Economy facility {facility.FacilityId} has an unknown system, conflicts with ship cargo, or is duplicated.",
                    nameof(economy));
            }

            if (economy.MaterialCompatibility is not null
                && (facility.ControllingPrincipalId is not { } principalId
                    || !principalIds.Contains(principalId)
                    || policiesByFacility.TryGetValue(
                        facility.FacilityId,
                        out ShipMaterializationPolicy? policy)
                    && policy.PrincipalId != principalId))
            {
                throw new ArgumentException(
                    $"Economy facility {facility.FacilityId} has an unknown or inconsistent controlling principal.",
                    nameof(economy));
            }
        }

        foreach (InitialFreighterSetup freighter in economy.Freighters)
        {
            if (!shipIds.Contains(freighter.ShipId))
            {
                throw new ArgumentException(
                    $"Economy freighter {freighter.ShipId} is not a session ship.",
                    nameof(economy));
            }
        }

        foreach (InitialConstructionOrderSetup order in economy.ConstructionOrders)
        {
            if (!policiesByFacility.TryGetValue(order.FacilityId, out ShipMaterializationPolicy? policy)
                || policy.GetDesign(order.DesignId) is null)
            {
                throw new ArgumentException(
                    $"Construction order {order.DesignId} has no matching materialization policy at facility {order.FacilityId}.",
                    nameof(economy));
            }
        }
    }
}
