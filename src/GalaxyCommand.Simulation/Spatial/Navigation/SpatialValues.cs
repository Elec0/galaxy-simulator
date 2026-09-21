using System.Collections.ObjectModel;

namespace GalaxyCommand.Simulation;

/// <summary>
/// One coordinate on an authoritative two-dimensional system map. The scale
/// represented by one unit remains a gameplay and benchmarking decision.
/// </summary>
public readonly record struct SpatialCoordinate(long Units)
{
    public override string ToString() =>
        Units.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// One authoritative position within a system-local coordinate space.
/// </summary>
public readonly record struct SpatialPosition(
    SpatialCoordinate X,
    SpatialCoordinate Y);

/// <summary>
/// A position qualified by the system whose coordinate space gives it meaning.
/// </summary>
public readonly record struct SystemPosition
{
    public SystemPosition(SystemId systemId, SpatialPosition position)
    {
        ArgumentOutOfRangeException.ThrowIfZero(systemId.Value);
        SystemId = systemId;
        Position = position;
    }

    public SystemId SystemId { get; }

    public SpatialPosition Position { get; }
}

/// <summary>
/// One distinct local navigable space.
/// </summary>
