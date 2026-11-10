using System.Globalization;

namespace GalaxyCommand.Simulation;

/// <summary>Stable station identity linked to one session-wide entity identity.</summary>
public readonly record struct StationId : IEntityId<StationId>
{
    public StationId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static StationId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Stable attached module identity; removal never permits identity reuse.</summary>
public readonly record struct StationModuleId : IEntityId<StationModuleId>
{
    public StationModuleId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static StationModuleId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Stable construction, upgrade or dismantling project identity.</summary>
public readonly record struct StationProjectId : IEntityId<StationProjectId>
{
    public StationProjectId(ulong value) { ArgumentOutOfRangeException.ThrowIfZero(value); Value = value; }
    public ulong Value { get; }
    public static StationProjectId Create(ulong value) => new(value);
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
