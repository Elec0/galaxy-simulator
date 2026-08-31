using GalaxyCommand.Simulation;

namespace GalaxyCommand.GodotClient;

/// <summary>
/// Converts a pointer in the system canvas into the authoritative integer
/// system-local coordinates represented by the current local camera state.
/// </summary>
internal static class SystemMapCoordinateTransform
{
    /// <summary>
    /// Inverts the system-view render transform before rounding to the exact
    /// spatial coordinate accepted by a movement command.
    /// </summary>
    internal static SpatialPosition ScreenToSystemPosition(
        float screenX,
        float screenY,
        float viewportWidth,
        float viewportHeight,
        float cameraX,
        float cameraY,
        float zoom)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoom);

        // Rendering inverts the authoritative Y axis. Preserve that inversion
        // here so a cursor location round-trips through the current pan/zoom.
        float presentationX = cameraX + ((screenX - (viewportWidth / 2)) / zoom);
        float presentationY = cameraY + ((screenY - (viewportHeight / 2)) / zoom);
        return new SpatialPosition(
            new SpatialCoordinate((long)Math.Round(presentationX)),
            new SpatialCoordinate((long)Math.Round(-presentationY)));
    }
}
