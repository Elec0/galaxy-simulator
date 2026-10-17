namespace GalaxyCommand.Simulation;

/// <summary>Movement-owner revision used solely to correlate spatial read views and forecasts.</summary>
public readonly record struct SpatialRevision(ulong Value)
{
    /// <summary>Advances without wrapping, so old correlations cannot regain validity.</summary>
    public SpatialRevision Next() => new(checked(Value + 1));
}
