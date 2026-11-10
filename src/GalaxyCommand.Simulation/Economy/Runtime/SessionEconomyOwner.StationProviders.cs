namespace GalaxyCommand.Simulation;

internal sealed record StationProviderAgendaEvent(SimulationTime Timestamp, EventGeneration Generation, EconomicEvent Event);

/// <summary>Immutable job and agenda changes prepared before station publication.</summary>
internal sealed class PreparedStationProviderChange
{
    internal required SessionEconomyOwner Owner { get; init; }
    internal required ulong Revision { get; init; }
    internal required Inventory Inventory { get; init; }
    internal required StationModuleDefinition Definition { get; init; }
    internal required bool Available { get; init; }
    internal EconomyFacilityCheckpoint? ExpectedFacility { get; init; }
    internal EconomyFacilityCheckpoint? ReplacementFacility { get; init; }
    internal bool AllocatesFacility { get; init; }
    internal bool AllocatesLocation { get; init; }
    internal PreparedProductionProviderTransition? Production { get; init; }
    internal PreparedConstructionProviderTransition? Construction { get; init; }
    internal PreparedStationShipPolicy? ShipPolicy { get; init; }
    internal FacilityId? FacilityId => ReplacementFacility?.FacilityId;
    internal IReadOnlyList<StationProviderAgendaEvent> Cancellations { get; init; } = [];
    internal IReadOnlyList<StationProviderAgendaEvent> Completions { get; init; } = [];
    internal bool CanCommit() => Owner.CanCommitStationProvider(this);
    internal void Apply() => Owner.ApplyStationProvider(this);
}

internal sealed partial class SessionEconomyOwner
{
    private IdSequence<FacilityId> _facilityIds = new();
    private IdSequence<LocationId> _locationIds = new();
    private ulong _stationProviderRevision;
    private readonly Dictionary<(InventoryId Inventory, MaterialId Material), LocationId> _stationSupplies = [];
    private readonly HashSet<InventoryId> _stationLogisticsInventories = [];

    /// <summary>Physical cargo remains station supply after its original producer is retired.</summary>
    private void RememberStationSupply(EconomyFacilityCheckpoint facility)
    {
        if (facility.StationBinding is null) return;
        _stationLogisticsInventories.Add(facility.InventoryId);
        if (facility.ProductionOutput is { } material)
            _stationSupplies[(facility.InventoryId, material)] = facility.LocationId;
    }

    /// <summary>Seeds fresh allocators; checkpoint restoration replaces these positions with the exact retained highwater.</summary>
    private void InitializeProviderIdentities()
    {
        foreach (var facility in _facilities.Values)
        {
            RememberStationSupply(facility);
            _facilityIds.AdvancePast(facility.FacilityId);
            _locationIds.AdvancePast(facility.LocationId);
        }
    }

    /// <summary>Rejects incompatible upgrade targets before structural projects or reservations change.</summary>
    internal bool CanUseStationProviderDefinition(FacilityId? facility, StationModuleDefinition target)
    {
        if (facility is not { } id) return true;
        if (!_facilities.ContainsKey(id)) return false;
        return (!_productionLines.TryGetValue(id, out var line) || target.ProductionProvider is { } production
                && line.SupportsProviderRecipe(production.Recipe))
            && (!_constructionProcesses.TryGetValue(id, out var process) || target.ShipConstructionProvider is { } ships
                && process.SupportsProviderDesigns(ships.AllowedDesigns));
    }

    /// <summary>Prepares compatible provider state and candidate identities without allocating jobs, locations or facilities.</summary>
    internal PreparedStationProviderChange? PrepareStationProvider(StationId station, EntityId entity,
        InventoryId inventory, PrincipalId principal, SystemPosition position, StationModuleId module,
        FacilityId? currentFacility, StationModuleDefinition target, bool available, SimulationTime now)
    {
        if (_stationProviderRevision == ulong.MaxValue
            || _lifecycleInventories.Get(inventory) is not { } storage
            || storage.Custody is not InventoryCustody.PrincipalControlled custody
            || custody.ControllingPrincipalId != principal
            || custody.PhysicalOwner is not InventoryOwnerReference.SessionEntity owner || owner.EntityId != entity)
            return null;
        var binding = new StationFacilityBinding(station, entity, module);
        EconomyFacilityCheckpoint? expected = currentFacility is { } id ? _facilities.GetValueOrDefault(id) : null;
        if (currentFacility is not null && (expected is null || expected.StationBinding != binding
                || expected.InventoryId != inventory || expected.Position != position)) return null;
        if (currentFacility is null && _facilities.Values.Any(value => value.StationBinding == binding)) return null;
        bool produces = target.ProductionProvider is not null;
        bool constructs = target.ShipConstructionProvider is not null;
        if (expected is null && !produces && !constructs)
            return new() { Owner = this, Revision = _stationProviderRevision, Inventory = storage,
                Definition = target, Available = available };
        if (_navigation is not HierarchicalLogisticsNavigation logistics) return null;
        bool allocatesFacility = expected is null;
        FacilityId facility;
        if (expected is not null) facility = expected.FacilityId;
        else if (!_facilityIds.TryPeek(out facility)) return null;
        EconomyFacilityCheckpoint? stationAnchor = _facilities.Values.FirstOrDefault(value => value.StationBinding?.StationId == station);
        LocationId location;
        bool allocatesLocation = expected is null && stationAnchor is null;
        if (expected is not null) location = expected.LocationId;
        else if (stationAnchor is not null) location = stationAnchor.LocationId;
        else if (!_locationIds.TryPeek(out location)) return null;
        if (!logistics.CanRegisterStationAnchor(location, position)) return null;
        _productionLines.TryGetValue(facility, out ProductionLine? line);
        _constructionProcesses.TryGetValue(facility, out ConstructionProcess? process);
        if (line is not null && (!produces || !line.SupportsProviderRecipe(target.ProductionProvider!.Recipe))
            || process is not null && (!constructs || !process.SupportsProviderDesigns(target.ShipConstructionProvider!.AllowedDesigns))) return null;
        if (produces && line is null && _productionIds.CaptureCheckpoint().NextValue is null) return null;
        PreparedProductionProviderTransition? production = line?.PrepareProviderTransition(available,
            target.ProductionProvider!.Throughput, now);
        PreparedConstructionProviderTransition? construction = process?.PrepareProviderTransition(available,
            target.ShipConstructionProvider!.Throughput, now);
        if (line is not null && production is null || process is not null && construction is null) return null;
        PreparedStationShipPolicy? shipPolicy = null;
        if (target.ShipConstructionProvider is { } ships)
        {
            shipPolicy = _lifecycle.PrepareStationShipPolicy(new(facility, principal, position,
                ships.MaterializedShipController, ships.InitialOrderPolicy, ships.AllowedDesigns));
            if (shipPolicy is null) return null;
        }
        var cancellations = new List<StationProviderAgendaEvent>();
        var completions = new List<StationProviderAgendaEvent>();
        if (production is { ReplacesCompletion: true, Before: { Completion: { } productionTime } oldProduction })
            cancellations.Add(new(productionTime, oldProduction.Generation, new EconomicEvent.ProductionComplete(facility, oldProduction.Job.Id)));
        if (production?.After is { Status: ProductionJobStatus.Running, Completion: { } newProductionTime } newProduction
            && production.Before != production.After)
            completions.Add(new(newProductionTime, newProduction.Generation, new EconomicEvent.ProductionComplete(facility, newProduction.Job.Id)));
        if (construction is { ReplacesCompletion: true, Before: { Completion: { } constructionTime } oldConstruction })
            cancellations.Add(new(constructionTime, oldConstruction.Generation, new EconomicEvent.ConstructionComplete(facility, oldConstruction.Order.Id)));
        if (construction?.After is { Status: ConstructionOrderStatus.Running, Completion: { } newConstructionTime } newConstruction
            && construction.Before != construction.After)
            completions.Add(new(newConstructionTime, newConstruction.Generation, new EconomicEvent.ConstructionComplete(facility, newConstruction.Order.Id)));
        return new() { Owner = this, Revision = _stationProviderRevision, Inventory = storage, Definition = target,
            Available = available, ExpectedFacility = expected,
            ReplacementFacility = new(facility, inventory, location, position, target.ProductionProvider?.Recipe.OutputMaterial, binding, target.Key),
            AllocatesFacility = allocatesFacility, AllocatesLocation = allocatesLocation, Production = production,
            Construction = construction, ShipPolicy = shipPolicy,
            Cancellations = cancellations.AsReadOnly(), Completions = completions.AsReadOnly() };
    }

    /// <summary>Restores services only when module identity, authored policy and live workflow availability agree.</summary>
    internal bool ValidateStationProviderBinding(StationId station, EntityId entity, InventoryId inventory,
        PrincipalId principal, SystemPosition position, StationModuleId module, FacilityId? id,
        StationModuleDefinition definition, bool available)
    {
        if (id is not { } facilityId || !_facilities.TryGetValue(facilityId, out var facility)
            || facility.StationBinding != new StationFacilityBinding(station, entity, module)
            || facility.InventoryId != inventory || facility.Position != position
            || facility.StationModuleDefinitionKey != definition.Key
            || _lifecycleInventories.Get(inventory)?.Custody?.ControllingPrincipalId != principal) return false;
        _productionLines.TryGetValue(facilityId, out var line);
        _constructionProcesses.TryGetValue(facilityId, out var process);
        if ((line is null) != (definition.ProductionProvider is null)
            || (process is null) != (definition.ShipConstructionProvider is null)) return false;
        if (line is not null && (line.IsProviderAvailable != available || line.Throughput != definition.ProductionProvider!.Throughput
            || !line.SupportsProviderRecipe(definition.ProductionProvider.Recipe))) return false;
        if (process is not null)
        {
            var provider = definition.ShipConstructionProvider!;
            ShipMaterializationPolicy? policy = _lifecycle.MaterializationPolicies.FirstOrDefault(value => value.FacilityId == facilityId);
            if (process.IsProviderAvailable != available || process.Throughput != provider.Throughput
                || !process.SupportsProviderDesigns(provider.AllowedDesigns) || policy is null
                || policy.PrincipalId != principal || policy.Position != position
                || policy.BaseController != provider.MaterializedShipController || policy.InitialOrderPolicy != provider.InitialOrderPolicy
                || !policy.AllowedDesigns.Keys.OrderBy(value => value.Value).SequenceEqual(provider.AllowedDesigns.Select(value => value.Id))) return false;
        }
        return true;
    }

    /// <summary>Checks immutable retired workload meaning against the final authored provider and retained ship policy.</summary>
    internal bool ValidateArchivedProviderBinding(FacilityId id, StationModuleDefinition definition, PrincipalId principal)
    {
        if (!_retiredFacilities.TryGetValue(id, out var archive)
            || (archive.Production is null) != (definition.ProductionProvider is null)
            || (archive.Construction is null) != (definition.ShipConstructionProvider is null)) return false;
        if (archive.Production is { } line && definition.ProductionProvider is { } production
            && (line.Throughput != production.Throughput || line.Jobs.Any(job => job?.Recipe is not { } recipe
                || recipe.OutputMaterial != production.Recipe.OutputMaterial
                || recipe.OutputQuantity != production.Recipe.OutputQuantity || recipe.RequiredWork != production.Recipe.RequiredWork
                || !recipe.Inputs.Select(input => new KeyValuePair<MaterialId, Quantity>(input!.MaterialId, input.Quantity))
                    .OrderBy(input => input.Key.Value).SequenceEqual(production.Recipe.Inputs)))) return false;
        if (archive.Construction is { } process && definition.ShipConstructionProvider is { } ships)
        {
            ShipMaterializationPolicy? policy = _lifecycle.MaterializationPolicies.FirstOrDefault(value => value.FacilityId == id);
            if (process.Throughput != ships.Throughput || policy is null || policy.PrincipalId != principal
                || policy.Position != archive.Facility.Position || policy.BaseController != ships.MaterializedShipController
                || policy.InitialOrderPolicy != ships.InitialOrderPolicy
                || !policy.AllowedDesigns.Keys.OrderBy(value => value.Value).SequenceEqual(ships.AllowedDesigns.Select(value => value.Id))
                || process.Orders.Any(order => ships.AllowedDesigns.All(design => design.Id != order!.DesignId))) return false;
        }
        return true;
    }

    internal bool CanCommitStationProvider(PreparedStationProviderChange effect) =>
        ReferenceEquals(effect.Owner, this) && effect.Revision == _stationProviderRevision
        && ReferenceEquals(_lifecycleInventories.Get(effect.Inventory.Id), effect.Inventory)
        && (effect.ReplacementFacility is not { } facility
            || ReferenceEquals(_facilities.GetValueOrDefault(facility.FacilityId), effect.ExpectedFacility)
            && (!effect.AllocatesFacility || _facilityIds.TryPeek(out var next) && next == facility.FacilityId)
            && (!effect.AllocatesLocation || _locationIds.TryPeek(out var location) && location == facility.LocationId)
            && _navigation is HierarchicalLogisticsNavigation logistics && logistics.CanRegisterStationAnchor(facility.LocationId, facility.Position))
        && (effect.Production is null || effect.Production.Owner.CanApplyProviderTransition(effect.Production))
        && (effect.Construction is null || effect.Construction.Owner.CanApplyProviderTransition(effect.Construction))
        && (effect.ShipPolicy is null || _lifecycle.CanApplyStationShipPolicy(effect.ShipPolicy));

    /// <summary>Allocates identities only at accepted activation and joins exact work transitions without scheduling.</summary>
    internal void ApplyStationProvider(PreparedStationProviderChange effect)
    {
        if (!CanCommitStationProvider(effect)) throw new InvalidOperationException("Station provider changed after preparation.");
        if (effect.ReplacementFacility is { } facility)
        {
            if (effect.AllocatesFacility) _facilityIds.Allocate();
            if (effect.AllocatesLocation) _locationIds.Allocate();
            ((HierarchicalLogisticsNavigation)_navigation).RegisterStationAnchor(facility.LocationId, facility.Position);
            _facilities[facility.FacilityId] = facility;
            RememberStationSupply(facility);
            if (effect.Production is { } production) production.Owner.ApplyProviderTransition(production);
            else if (effect.Definition.ProductionProvider is { } provider)
            {
                var line = new ProductionLine(facility.FacilityId, facility.InventoryId, provider.Throughput);
                line.Enqueue(_productionIds, provider.Recipe, provider.Repeat);
                if (!effect.Available) line.ApplyProviderTransition(line.PrepareProviderTransition(false, provider.Throughput, SimulationTime.Zero)!);
                _productionLines.Add(facility.FacilityId, line);
                _productionOutputs.Add(facility.FacilityId, provider.Recipe.OutputMaterial);
                _productionLocations.Add(facility.FacilityId, facility.LocationId);
            }
            if (effect.Construction is { } construction) construction.Owner.ApplyProviderTransition(construction);
            else if (effect.Definition.ShipConstructionProvider is { } providerShips)
            {
                var process = new ConstructionProcess(facility.FacilityId, facility.InventoryId, providerShips.Throughput);
                if (!effect.Available) process.ApplyProviderTransition(process.PrepareProviderTransition(false, providerShips.Throughput, SimulationTime.Zero)!);
                _constructionProcesses.Add(facility.FacilityId, process);
                _constructionLocations.Add(facility.FacilityId, facility.LocationId);
            }
            if (effect.ShipPolicy is { } policy) _lifecycle.ApplyStationShipPolicy(policy);
        }
        _stationProviderRevision++;
    }
}

public sealed partial class ProductionLine
{
    internal bool SupportsProviderRecipe(Recipe recipe) => _jobs.Values.All(job =>
        job.Recipe.RequiredWork == recipe.RequiredWork && job.Recipe.OutputMaterial == recipe.OutputMaterial
        && job.Recipe.OutputQuantity == recipe.OutputQuantity && job.Recipe.Inputs.SequenceEqual(recipe.Inputs));
}

public sealed partial class ConstructionProcess
{
    internal bool SupportsProviderDesigns(IReadOnlyList<ShipDesign> designs)
    {
        foreach (var order in _orders.Values)
        {
            ShipDesign? target = designs.SingleOrDefault(design => design.Id == order.DesignId);
            if (target is null || order.Design is not ShipDesign original) return false;
            try { _ = new ShipDefinitionCatalog([original, target]); }
            catch (ArgumentException) { return false; }
        }
        return true;
    }
}
