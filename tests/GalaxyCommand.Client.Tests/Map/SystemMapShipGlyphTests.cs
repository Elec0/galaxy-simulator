using System.Numerics;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client.Tests;

public sealed class SystemMapShipGlyphTests
{
    [Theory]
    [InlineData(0, 6.0f, 0.0f)]
    [InlineData(90_000, 0.0f, 6.0f)]
    [InlineData(180_000, -6.0f, 0.0f)]
    [InlineData(270_000, 0.0f, -6.0f)]
    public void TriangleTipFollowsTheScreenProjectionOfTheLocalHeading(
        uint headingMillidegrees,
        float expectedX,
        float expectedY)
    {
        SystemMapShipTriangle triangle = SystemMapShipGlyph.CreateTriangle(
            new ShipHeading(headingMillidegrees),
            radius: 6.0f);

        Assert.Equal(expectedX, triangle.Tip.X, precision: 4);
        Assert.Equal(expectedY, triangle.Tip.Y, precision: 4);
    }
}
