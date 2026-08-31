using GalaxyCommand.Simulation;

namespace GalaxyCommand.GodotClient.Tests;

public sealed class SystemMapCoordinateTransformTests
{
    [Fact]
    public void ScreenPointInvertsThePannedAndZoomedSystemView()
    {
        SpatialPosition position = SystemMapCoordinateTransform.ScreenToSystemPosition(
            screenX: 840,
            screenY: 160,
            viewportWidth: 1280,
            viewportHeight: 720,
            cameraX: 100,
            cameraY: -50,
            zoom: 2.0f);

        Assert.Equal(new SpatialCoordinate(200), position.X);
        Assert.Equal(new SpatialCoordinate(150), position.Y);
    }
}
