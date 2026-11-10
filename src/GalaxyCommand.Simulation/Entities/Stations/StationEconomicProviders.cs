namespace GalaxyCommand.Simulation;

/// <summary>Explicit production recipe, rate and repetition policy for one completed module provider.</summary>
public sealed class StationProductionProvider
{
    /// <summary>Requires a positive authored rate/work/output and a closed immutable recipe with checked input totals.</summary>
    public StationProductionProvider(Recipe recipe, Throughput throughput, bool repeat)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentOutOfRangeException.ThrowIfZero(throughput.UnitsPerSecond);
        ArgumentOutOfRangeException.ThrowIfZero(recipe.RequiredWork.Units, nameof(recipe));
        ArgumentOutOfRangeException.ThrowIfZero(recipe.OutputQuantity.Units, nameof(recipe));
        ArgumentOutOfRangeException.ThrowIfZero(recipe.OutputMaterial.Value, nameof(recipe));
        ulong inputTotal = 0;
        foreach ((MaterialId material, Quantity amount) in recipe.Inputs)
        {
            ArgumentOutOfRangeException.ThrowIfZero(material.Value, nameof(recipe));
            ArgumentOutOfRangeException.ThrowIfZero(amount.Units, nameof(recipe));
            inputTotal = checked(inputTotal + amount.Units);
        }
        Recipe = recipe;
        Throughput = throughput;
        Repeat = repeat;
    }

    public Recipe Recipe { get; }
    public Throughput Throughput { get; }
    public bool Repeat { get; }
}

/// <summary>Explicit ship-producing designs and materialization choices, separate from station-builder work supply.</summary>
public sealed class StationShipConstructionProvider
{
    /// <summary>Copies unique retained ship definitions and explicit supported post-materialization control/order policy.</summary>
    public StationShipConstructionProvider(IEnumerable<ShipDesign> allowedDesigns, Throughput throughput,
        ActorController materializedShipController, InitialShipOrderPolicy initialOrderPolicy)
    {
        ArgumentNullException.ThrowIfNull(allowedDesigns);
        ArgumentNullException.ThrowIfNull(materializedShipController);
        ArgumentOutOfRangeException.ThrowIfZero(throughput.UnitsPerSecond);
        if (materializedShipController.Kind == ActorControllerKind.Script)
            throw new ArgumentException("A materialized ship requires an explicit persistent controller.", nameof(materializedShipController));
        if (initialOrderPolicy != InitialShipOrderPolicy.NoInitialOrder)
            throw new ArgumentOutOfRangeException(nameof(initialOrderPolicy));
        ShipDesign[] designs = allowedDesigns.ToArray();
        foreach (ShipDesign design in designs)
        {
            ArgumentNullException.ThrowIfNull(design);
            StationDefinitionValidation.ValidateRecipe(design.Recipe);
        }
        if (designs.Length == 0 || designs.Select(design => design.Id).Distinct().Count() != designs.Length)
            throw new ArgumentException("A ship provider requires unique explicit allowed designs.", nameof(allowedDesigns));
        AllowedDesigns = Array.AsReadOnly(designs.OrderBy(design => design.Id.Value).ToArray());
        Throughput = throughput;
        MaterializedShipController = materializedShipController;
        InitialOrderPolicy = initialOrderPolicy;
    }

    public IReadOnlyList<ShipDesign> AllowedDesigns { get; }
    public Throughput Throughput { get; }
    public ActorController MaterializedShipController { get; }
    public InitialShipOrderPolicy InitialOrderPolicy { get; }
}
