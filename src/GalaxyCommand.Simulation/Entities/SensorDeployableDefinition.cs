using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Immutable portable sensor definition shared by stored items and deployed entities.</summary>
public sealed record SensorDeployableDefinition
{
    /// <summary>
    /// Defines explicit cargo cost and independent deployment/sensor ranges in meters.
    /// Zero ranges are valid; capacity must be positive and the key must name this category.
    /// </summary>
    public SensorDeployableDefinition(QualifiedContentKey key, string name,
        Quantity capacityCost, ulong deploymentRangeUnits, ulong sensorRadiusUnits)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (key.ContentKind.Value != "sensor-deployable")
            throw new ArgumentException("The key must identify a sensor-deployable definition.", nameof(key));
        Physical = new PhysicalDefinition(key, PhysicalHoldingKind.Discrete, capacityCost);
        Name = name;
        DeploymentRangeUnits = deploymentRangeUnits;
        SensorRadiusUnits = sensorRadiusUnits;
    }

    public QualifiedContentKey Key => Physical.Key;
    public string Name { get; }
    public PhysicalDefinition Physical { get; }
    public ulong DeploymentRangeUnits { get; }
    public ulong SensorRadiusUnits { get; }
}
