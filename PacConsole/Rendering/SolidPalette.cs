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
    /// The console palette for one theme (the plant's Solid*, circuit and system colours in App.xaml, the selection
    /// colour from the theme's palette), so every plant
    /// drawing matches its card. Stroke widths are theme-invariant and come from <c>Styles/Tokens.xaml</c>.
    /// </summary>
    public static SolidPalette For(ElementTheme theme)
    {
        SKColor C(string key)
        {
            var c = ThemeBrushes.Color(key, theme);
            return new SKColor(c.R, c.G, c.B, c.A);
        }
        float D(string key) => (float)(double)Application.Current.Resources[key];
        return new(
            Paper: C("SolidPaperColor"),
            Shades: [C("SolidShade0Color"), C("SolidShade1Color"), C("SolidShade2Color"), C("SolidShade3Color")],
            Top: C("SolidTopColor"),
            LitFace: C("SolidLitFaceColor"),
            LitShade: C("SolidLitShadeColor"),
            LitTop: C("SolidLitTopColor"),
            Hi: C("SolidLineHiColor"),
            Mid: C("SolidLineMidColor"),
            Lo: C("SolidLineLoColor"),
            Faint: C("SolidLineFaintColor"),
            Lit: C("PrimaryColor"),
            Alert: C("AlertColor"),
            Leader: C("SolidLeaderColor"),
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
