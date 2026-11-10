namespace GalaxyCommand.Simulation;

internal sealed partial class StationRuntimeOwner
{
    /// <summary>
    /// Admits the complete authored operational batch before publishing any identity.
    /// Inventories are existing owner instances; no duplicate storage or fabricated construction history is created.
    /// </summary>
    private void InitializeStations()
    {
        var stations = new HashSet<StationId>();
        var entities = new HashSet<EntityId>();
        var inventories = new HashSet<InventoryId>();
        var modules = new HashSet<StationModuleId>();
        var facilities = new HashSet<FacilityId>();
        var prepared = new List<(InitialStationSetup Seed, StationState State)>();
        foreach (InitialStationSetup seed in _setup.InitialStations)
        {
            if (_agenda.CurrentTime != SimulationTime.Zero || seed.CreatedAt != SimulationTime.Zero
                || !stations.Add(seed.Id) || !entities.Add(seed.EntityId) || !inventories.Add(seed.InventoryId)
                || !_lifecycle.CanRegisterInitialStation(seed))
                throw new ArgumentException("Initial station identity, time or shared inventory custody is invalid.", nameof(_setup));
            StationDefinition definition = _setup.Definitions.Stations.FirstOrDefault(item => item.Key == seed.DefinitionKey)
                ?? throw new ArgumentException("An initial station references an unresolved base definition.", nameof(_setup));
            if (!definition.PlacementRules.Allows(seed.Position))
                throw new ArgumentException("An initial station violates authored placement rules.", nameof(_setup));
            var state = new StationState(seed.Id, seed.EntityId, _lifecycle.Inventories.Get(seed.InventoryId)!,
                seed.PrincipalId, definition, seed.Position) { Phase = StationPhase.Operational, CreatedAt = seed.CreatedAt };
            ulong occupied = 0;
            foreach (InitialStationModuleSetup moduleSeed in seed.Modules)
            {
                StationModuleDefinition moduleDefinition = _setup.Definitions.Modules.FirstOrDefault(item => item.Key == moduleSeed.DefinitionKey)
                    ?? throw new ArgumentException("An initial module references an unresolved definition.", nameof(_setup));
                if (!modules.Add(moduleSeed.Id) || !definition.AllowedModules.Contains(moduleSeed.DefinitionKey)
                    || moduleSeed.FacilityId is { } facility && !facilities.Add(facility))
                    throw new ArgumentException("Initial module identities, allowed types or facility bindings are invalid.", nameof(_setup));
                occupied = checked(occupied + moduleDefinition.CapacityCost);
                state.Modules.Add(moduleSeed.Id, new ModuleState(moduleSeed.Id, moduleDefinition, null)
                { Phase = StationModulePhase.Operational, Enabled = moduleSeed.Enabled, IsCompleted = true, FacilityId = moduleSeed.FacilityId });
            }
            (Quantity physical, Quantity enabled, Quantity? protection) = CalculateStorageLimits(state);
            if (occupied > definition.ModuleCapacity || state.Inventory.Capacity != physical
                || state.Inventory.EnabledIntakeCapacity != enabled || state.Inventory.ProtectedIntakeCapacity != protection)
                throw new ArgumentException("Initial modules exceed expansion capacity or disagree with shared storage limits.", nameof(_setup));
            prepared.Add((seed, state));
        }

        // No owner mutation precedes validation of the last station in the batch.
        foreach ((InitialStationSetup seed, StationState state) in prepared)
        {
            _lifecycle.ApplyRegisterInitialStation(seed);
            _stationIds.AdvancePast(seed.Id);
            foreach (StationModuleId module in state.Modules.Keys) _moduleIds.AdvancePast(module);
            _stations.Add(seed.Id, state);
        }
    }
}
