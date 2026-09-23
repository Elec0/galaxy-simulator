using System.Numerics;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client;

/// <summary>
/// Computes the presentation-space triangle used to draw a ship on a system map.
/// </summary>
internal static class SystemMapShipGlyph
{
    /// <summary>
    /// Creates an equilateral triangle centered on the ship whose tip follows
    /// the screen projection of its authoritative local heading.
    /// </summary>
    internal static SystemMapShipTriangle CreateTriangle(ShipHeading heading, float radius)
    {
        // Screen Y grows downward, matching the clockwise-positive local-heading convention.
        float headingRadians = heading.Millidegrees * (MathF.Tau / ShipHeading.MillidegreesPerRevolution);
        return new SystemMapShipTriangle(
            VertexAt(headingRadians, radius),
            VertexAt(headingRadians + ((2.0f * MathF.Tau) / 3.0f), radius),
            VertexAt(headingRadians - ((2.0f * MathF.Tau) / 3.0f), radius));
    }

    private static Vector2 VertexAt(float angleRadians, float radius) => new(
        radius * MathF.Cos(angleRadians),
        radius * MathF.Sin(angleRadians));
}

/// <summary>
/// Three local offsets for a centered system-map ship triangle.
/// </summary>
internal readonly record struct SystemMapShipTriangle(
    Vector2 Tip,
    Vector2 FirstBase,
    Vector2 SecondBase);
