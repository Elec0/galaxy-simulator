using Godot;

namespace GalaxyCommand.GodotClient;

public partial class GalaxyMap : Control
{
    private static readonly Vector2[] NormalizedLocations =
    [
        new(0.22f, 0.48f),
        new(0.50f, 0.34f),
        new(0.76f, 0.53f),
    ];

    private static readonly string[] LocationNames =
    [
        "MINE",
        "REFINERY",
        "SHIPYARD",
    ];

    public override void _Ready()
    {
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        Vector2[] locations = GetLocations();

        DrawLine(locations[0], locations[1], new Color("294864"), 2.0f, true);
        DrawLine(locations[1], locations[2], new Color("294864"), 2.0f, true);

        for (int index = 0; index < locations.Length; index++)
        {
            DrawCircle(locations[index], 19.0f, new Color("102c45"));
            DrawArc(locations[index], 19.0f, 0, Mathf.Tau, 48, new Color("70c7e8"), 2.0f, true);
            DrawString(
                ThemeDB.FallbackFont,
                locations[index] + new Vector2(-32, 43),
                LocationNames[index],
                HorizontalAlignment.Center,
                64,
                13,
                new Color("8aaac0"));
        }

        DrawCircle(locations[0].Lerp(locations[1], 0.62f), 5.0f, new Color("f5c86b"));
        DrawCircle(locations[1].Lerp(locations[2], 0.36f), 5.0f, new Color("f5c86b"));
    }

    private Vector2[] GetLocations()
    {
        var result = new Vector2[NormalizedLocations.Length];
        for (int index = 0; index < NormalizedLocations.Length; index++)
        {
            result[index] = NormalizedLocations[index] * Size;
        }

        return result;
    }
}
