using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using PacConsole.Styles;
using SkiaSharp;

namespace PacConsole.Rendering;

/// <summary>Colours and widths for painting solids, read once (never inside RenderOverride).</summary>
public sealed record SolidPalette(
    SKColor Paper,
    SKColor[] Shades,
    SKColor Top,
    SKColor LitFace,
    SKColor LitShade,
    SKColor LitTop,
    SKColor Hi,
    SKColor Mid,
    SKColor Lo,
    SKColor Faint,
    SKColor Lit,
    SKColor Alert,
    SKColor Leader,
    SKColor Shadow,
    SKColor Silhouette,
    float Hairline,
    float SilhouetteStroke,
    float LitStroke)
{
    /// <summary>Base and deep tone per pipe circuit (steam, water, cooling, fuel).</summary>
    public IReadOnlyDictionary<Circuit, (SKColor Base, SKColor Deep)> Circuits { get; init; } = new Dictionary<Circuit, (SKColor, SKColor)>();

    /// <summary>Each process system's ink: its outline, glow and motion when the plant view isolates it.</summary>
    public IReadOnlyDictionary<PlantSystem, SKColor> Systems { get; init; } = new Dictionary<PlantSystem, SKColor>();

    /// <summary>
    /// The drawing's colours for one theme, so every plant drawing matches its panel. The paper is the palette's
    /// Background (the figure sits on the panel with no frame), the leaders its Outline, and the selected unit is lit in
    /// its selection colours (Primary, PrimaryContainer): a change to the palette (Styles/ColorPaletteOverride.xaml)
    /// reaches the drawing. The plant's own shading, line inks, circuits and process systems have no palette role and
    /// come from App.xaml. Stroke widths are theme-invariant and come from <c>Styles/Layout.xaml</c>.
    /// </summary>
    public static SolidPalette For(ElementTheme theme)
    {
        SKColor C(string key)
        {
            var c = ThemeBrushes.Color(key, theme);
            return new SKColor(c.R, c.G, c.B, c.A);
        }
        float D(string key) => (float)(double)Application.Current.Resources[key];

        // The selected unit's faces take PrimaryContainer; its shaded sides step toward the darker of Primary and black,
        // its top toward the lighter of Primary and white (Primary is darker than its container in Light, lighter in
        // Graphite), as the hand-picked lit tones did.
        var primary = C("PrimaryColor");
        var container = C("PrimaryContainerColor");
        var primaryIsDarker = Luma(primary) < Luma(container);
        return new(
            Paper: C("BackgroundColor"),
            Shades: [C("SolidShade0Color"), C("SolidShade1Color"), C("SolidShade2Color"), C("SolidShade3Color")],
            Top: C("SolidTopColor"),
            LitFace: container,
            LitShade: primaryIsDarker ? PartPainter.Lerp(container, primary, 0.1f) : Darken(container, 0.18f),
            LitTop: primaryIsDarker ? Lighten(container, 0.5f) : PartPainter.Lerp(container, primary, 0.085f),
            Hi: C("SolidLineHiColor"),
            Mid: C("SolidLineMidColor"),
            Lo: C("SolidLineLoColor"),
            Faint: C("SolidLineFaintColor"),
            Lit: primary,
            Alert: C("AlertColor"),
            Leader: C("OutlineColor"),
            Shadow: C("SolidShadowColor"),
            Silhouette: C("SolidSilhouetteColor"),
            Hairline: D("SolidStrokeHairline"),
            SilhouetteStroke: D("SolidStrokeSilhouette"),
            LitStroke: D("SolidStrokeLit"))
        {
            Circuits = new Dictionary<Circuit, (SKColor, SKColor)>
            {
                [Circuit.Steam] = (C("SolidSteamColor"), C("CircuitSteamColor")),
                [Circuit.Water] = (C("SolidWaterColor"), C("CircuitWaterColor")),
                [Circuit.Cooling] = (C("SolidCoolingColor"), C("CircuitCoolingColor")),
                [Circuit.Fuel] = (C("SolidFuelColor"), C("CircuitFuelColor")),
            },
            Systems = new Dictionary<PlantSystem, SKColor>
            {
                [PlantSystem.SteamWater] = C("SystemSteamWaterColor"),
                [PlantSystem.Cooling] = C("SystemCoolingColor"),
                [PlantSystem.FuelFlue] = C("SystemFuelFlueColor"),
                [PlantSystem.Electrical] = C("SystemElectricalColor"),
                [PlantSystem.Alarms] = C("AlertColor"),
            },
        };
    }

    private static float Luma(SKColor c) => 0.2126f * c.Red + 0.7152f * c.Green + 0.0722f * c.Blue;

    private static SKColor Darken(SKColor c, float t) =>
        new((byte)MathF.Round(c.Red * (1 - t)), (byte)MathF.Round(c.Green * (1 - t)), (byte)MathF.Round(c.Blue * (1 - t)), c.Alpha);

    private static SKColor Lighten(SKColor c, float t) =>
        new((byte)MathF.Round(c.Red + (255 - c.Red) * t), (byte)MathF.Round(c.Green + (255 - c.Green) * t), (byte)MathF.Round(c.Blue + (255 - c.Blue) * t), c.Alpha);
}

/// <summary>What changes between frames: selection, alerts, each unit's lift and the clock.</summary>
public readonly record struct SolidFrame(
    UnitId? Selected,
    IReadOnlyCollection<UnitId> Alerting,
    IReadOnlyDictionary<UnitId, float>? Lift = null,
    double Seconds = 0,
    bool Details = true,
    UnitId? Hovered = null,
    PlantSystem System = PlantSystem.All,
    bool Motion = true);
