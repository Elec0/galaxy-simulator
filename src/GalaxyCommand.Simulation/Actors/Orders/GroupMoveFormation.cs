namespace GalaxyCommand.Simulation;

/// <summary>
/// Derives one position destination for each canonical member of a one-shot
/// group move without mutating authoritative actor, order, or spatial state.
/// </summary>
public interface IGroupMoveFormationResolver
{
    /// <summary>
    /// Returns destinations in the same ascending <paramref name="memberIds"/>
    /// order. Callers must provide at least one nonzero, strictly ascending
    /// ship identifier.
    /// </summary>
    IReadOnlyList<SystemPosition> Resolve(
        IReadOnlyList<ShipId> memberIds,
        SystemPosition destination);
}

/// <summary>
/// Provides the initial disposable formation rule for one-shot group moves.
/// A later formation implementation may replace this resolver without changing
/// group-command admission or individual order ownership.
/// </summary>
public sealed class BasicGroupMoveFormationResolver : IGroupMoveFormationResolver
{
    /// <summary>
    /// The initial radial offset around a selected destination. It is an
    /// abstract coordinate distance, not a real-world measurement.
    /// </summary>
    public const long RadiusUnits = 100;

    /// <inheritdoc />
    public IReadOnlyList<SystemPosition> Resolve(
        IReadOnlyList<ShipId> memberIds,
        SystemPosition destination)
    {
        ArgumentNullException.ThrowIfNull(memberIds);
        if (memberIds.Count == 0)
        {
            throw new ArgumentException(
                "A group move needs at least one member.",
                nameof(memberIds));
        }

        var destinations = new SystemPosition[memberIds.Count];
        ShipId previous = default;
        for (int index = 0; index < memberIds.Count; index++)
        {
            ShipId memberId = memberIds[index];
            if (memberId.Value == 0 || (index > 0 && memberId.Value <= previous.Value))
            {
                throw new ArgumentException(
                    "Group move members must be nonzero and strictly ascending.",
                    nameof(memberIds));
            }

            previous = memberId;
        }

        if (memberIds.Count == 1)
        {
            destinations[0] = destination;
            return destinations;
        }

        for (int index = 0; index < memberIds.Count; index++)
        {
            double angle = (Math.PI * 2D * index) / memberIds.Count;

            // This temporary resolver emits only integer authoritative
            // destinations while spacing canonical members clockwise from east.
            long xOffset = checked((long)Math.Round(
                RadiusUnits * Math.Cos(angle),
                MidpointRounding.AwayFromZero));
            long yOffset = checked((long)Math.Round(
                -RadiusUnits * Math.Sin(angle),
                MidpointRounding.AwayFromZero));
            destinations[index] = new SystemPosition(
                destination.SystemId,
                new SpatialPosition(
                    new SpatialCoordinate(checked(destination.Position.X.Units + xOffset)),
                    new SpatialCoordinate(checked(destination.Position.Y.Units + yOffset))));
        }

        return destinations;
    }
}
