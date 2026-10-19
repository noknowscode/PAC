// PacArt: renders shots of the plant drawing offscreen with the product's own SolidRenderer.
//
//   dotnet run -c Release -- <shots.json> <outDir> [repoRoot]
//
// Each shot names a theme (light = the console's paper, dark = graphite), a logical size and scale,
// a camera, which parts to draw (the whole plant or one unit on its pad), the system in view, the
// selection, units in alarm, lifts, and the clock. A long exposure blends `exposure` frames stepped at
// 1/60 s: lighten on graphite, darken on paper. Every frame comes from a fresh renderer, so no cached
// layer or worker timing can differ between runs: the same shot file gives the same pixels.

using System.Text.Json;
using System.Xml.Linq;
using PacConsole.Core.Geometry;
using PacConsole.Core.Operations;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using PacConsole.Rendering;
using SkiaSharp;

var shotsPath = args.Length > 0 ? args[0] : throw new ArgumentException("usage: PacArt <shots.json> <outDir> [repoRoot]");
var outDir = args.Length > 1 ? args[1] : throw new ArgumentException("usage: PacArt <shots.json> <outDir> [repoRoot]");
var repo = args.Length > 2 ? args[2] : FindRepo(AppContext.BaseDirectory);
Directory.CreateDirectory(outDir);

var light = Tokens(Path.Combine(repo, "PacConsole", "Styles", "Tokens.xaml"));
var dark = new ResourceDictionary();
foreach (var (k, v) in light) dark[k] = v;
foreach (var (k, v) in Tokens(Path.Combine(repo, "PacConsole", "Styles", "Palette.Dark.xaml"))) dark[k] = v;
var palettes = new Dictionary<string, (SolidPalette Palette, SKColor Ground)>
{
    ["light"] = (SolidPalette.FromResources(light), ColorOf(light["GroundBrush"])),
    ["dark"] = (SolidPalette.FromResources(dark), ColorOf(dark["GroundBrush"])),
};

var shots = JsonSerializer.Deserialize<List<Shot>>(File.ReadAllText(shotsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var only = Environment.GetEnvironmentVariable("PACART_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
foreach (var shot in shots)
{
    if (only is not null && !only.Contains(shot.Id)) continue;
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var (palette, ground) = palettes[shot.Theme];
    var parts = shot.Unit is { } code ? PlantSolids.Default.UnitWithPad(Unit(code)) : PlantSolids.Default.All;
    var pose = shot.Pose(CameraPresets.All);
    var (w, h) = (shot.Size[0], shot.Size[1]);
    // Framing on a few units crops the whole plant around them; the rest still draws, cut by the edge.
    IReadOnlyList<SolidPart> framed = shot.Fit is { Length: > 0 } fit ? [.. fit.SelectMany(u => PlantSolids.Default.UnitWithPad(Unit(u)))] : parts;
    var projection = shot.Framing == "orbit"
        ? PlantSolids.ViewProjection(pose, w, h, shot.Padding)
        : PlantSolids.Fit(pose, framed, w, h, shot.Padding);
    if (shot.Offset is { Length: 2 } o) projection = new OrbitProjection(pose, projection.Scale * shot.Zoom, projection.CenterX + o[0], projection.CenterY + o[1]);
    else if (shot.Zoom != 1) projection = new OrbitProjection(pose, projection.Scale * shot.Zoom, projection.CenterX, projection.CenterY);

    var frame = new SolidFrame(
        Selected: shot.Selected is { } s ? Unit(s) : null,
        Alerting: [.. shot.Alerting.Select(Unit)],
        Lift: shot.Lift.ToDictionary(kv => Unit(kv.Key), kv => kv.Value),
        Seconds: shot.Seconds,
        Details: shot.Details,
        System: Enum.Parse<PlantSystem>(shot.System, ignoreCase: true),
        Motion: shot.Motion);

    var frames = Math.Max(1, shot.Exposure);
    byte[]? blend = null;
    var info = new SKImageInfo(w * shot.Scale, h * shot.Scale, SKColorType.Rgba8888, SKAlphaType.Premul);
    for (var k = 0; k < frames; k++)
    {
        // A sweep turns the camera through `sweep` degrees across the exposure, as the app's auto orbit does.
        var p = projection;
        if (shot.Sweep != 0 && frames > 1)
        {
            var turned = pose with { Yaw = pose.Yaw + shot.Sweep * MathF.PI / 180 * k / (frames - 1) };
            p = new OrbitProjection(turned, projection.Scale, projection.CenterX, projection.CenterY);
        }
        var pixels = Render(info, shot.Scale, ground, palette, parts, p, frame with { Seconds = shot.Seconds + k / 60.0 });
        if (blend is null) { blend = pixels; continue; }
        // Long exposure: lighten keeps the brightest a pixel ever was (light on graphite);
        // darken keeps the darkest (ink on paper).
        if (shot.Theme == "dark") for (var i = 0; i < blend.Length; i++) blend[i] = Math.Max(blend[i], pixels[i]);
        else for (var i = 0; i < blend.Length; i++) blend[i] = Math.Min(blend[i], pixels[i]);
    }
    Save(info, blend!, Path.Combine(outDir, shot.Id + ".png"));
    File.WriteAllText(Path.Combine(outDir, shot.Id + ".meta.json"), JsonSerializer.Serialize(Caption(shot)));
    Console.WriteLine($"{shot.Id}: {info.Width}x{info.Height}, {frames} frame(s), {watch.Elapsed.TotalSeconds:0.0}s");
}

static byte[] Render(SKImageInfo info, int scale, SKColor ground, SolidPalette palette, IReadOnlyList<SolidPart> parts, OrbitProjection projection, SolidFrame frame)
{
    using var surface = SKSurface.Create(info);
    var canvas = surface.Canvas;
    canvas.Clear(ground);
    canvas.Scale(scale);
    using (var renderer = new SolidRenderer { Palette = palette, Scale = scale }) renderer.Draw(canvas, parts, projection, frame);
    canvas.Flush();
    var bytes = new byte[info.BytesSize];
    using var pixmap = surface.PeekPixels();
    System.Runtime.InteropServices.Marshal.Copy(pixmap.GetPixels(), bytes, 0, bytes.Length);
    return bytes;
}

static void Save(SKImageInfo info, byte[] pixels, string path)
{
    using var bitmap = new SKBitmap(info);
    System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    using var file = File.Create(path);
    data.SaveTo(file);
}

// The caption's words, all from the product: catalogs, system titles, alarm guidance, camera presets.
static Dictionary<string, string> Caption(Shot shot)
{
    var system = Enum.Parse<PlantSystem>(shot.System, ignoreCase: true);
    string Units(PlantSystem s) => string.Join(", ", UnitCatalog.All.Where(u => PlantSystems.Contains(s, u.Id)).Select(u => $"{u.Code} {u.Name}"));
    var preset = CameraPresets.All.First(p => p.Key == shot.View);
    var (title, body) = shot.Caption switch
    {
        "unit" when shot.Unit is { } code => (UnitCatalog.Get(Unit(code)) is var u ? $"{u.Code} · {u.Name}" : "", EquipmentCatalog.Get(Unit(code)).Description),
        "system" => (PlantSystems.Title(system), "Isolates " + Units(system) + "."),
        "alarm" when shot.AlarmKey is { } key => (AlarmBook.Title(key), "Likely cause: " + EquipmentCatalog.Find(key)!.Alarm!.Guidance.Cause),
        "systems" => ("Process systems", string.Join(" · ", Enum.GetValues<PlantSystem>().Where(s => s is not (PlantSystem.All or PlantSystem.Alarms)).Select(PlantSystems.Label)) + " · Active alarms"),
        _ => ($"Plant 02 · {preset.Label} view", $"Steam-cycle station · {preset.Detail}"),
    };
    var caption = new Dictionary<string, string> { ["title"] = title, ["body"] = body, ["theme"] = shot.Theme };
    if (shot.Unit is { } unitCode)
    {
        // The unit's facts as the inspector shows them: code, uptime, last service, and each reading's normal band.
        var info = UnitCatalog.Get(Unit(unitCode));
        caption["code"] = info.Code;
        caption["name"] = info.Name;
        caption["uptime"] = info.Uptime;
        caption["service"] = info.LastService;
        caption["readings"] = string.Join("\n", EquipmentCatalog.Get(info.Id).Readings.Select(r =>
            $"{r.Label}\t{r.Band.NormalLow.ToString(r.Format, System.Globalization.CultureInfo.InvariantCulture)}–{r.Band.NormalHigh.ToString(r.Format, System.Globalization.CultureInfo.InvariantCulture)} {r.Unit}".TrimEnd()));
        caption["connections"] = string.Join(", ", EquipmentCatalog.Get(info.Id).Connections.Select(c => UnitCatalog.Get(c).Code));
    }
    return caption;
}

static UnitId Unit(string code) =>
    UnitCatalog.All.FirstOrDefault(u => u.Code.Equals(code, StringComparison.OrdinalIgnoreCase) || u.ShortCode.Equals(code, StringComparison.OrdinalIgnoreCase))?.Id
    ?? Enum.Parse<UnitId>(code, ignoreCase: true);

// The keyed colours and numbers of a theme dictionary, as the app's merged resources hold them.
static ResourceDictionary Tokens(string path)
{
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var dict = new ResourceDictionary();
    foreach (var e in XDocument.Load(path).Descendants())
    {
        if (e.Attribute(x + "Key")?.Value is not { } key) continue;
        switch (e.Name.LocalName)
        {
            case "SolidColorBrush" when e.Attribute("Color")?.Value is { } c:
                dict[key] = new SolidColorBrush { Color = Hex(c) };
                break;
            case "Color":
                dict[key] = Hex(e.Value.Trim());
                break;
            case "Double":
                dict[key] = double.Parse(e.Value.Trim(), System.Globalization.CultureInfo.InvariantCulture);
                break;
        }
    }
    return dict;
}

static Windows.UI.Color Hex(string s)
{
    var v = Convert.ToUInt32(s.TrimStart('#'), 16);
    return s.Length == 7
        ? new(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v)
        : new((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
}

static SKColor ColorOf(object value) => value switch
{
    SolidColorBrush b => new SKColor(b.Color.R, b.Color.G, b.Color.B, b.Color.A),
    Windows.UI.Color c => new SKColor(c.R, c.G, c.B, c.A),
    _ => SKColors.Magenta,
};

static string FindRepo(string from)
{
    for (var d = new DirectoryInfo(from); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "PacConsole.sln"))) return d.FullName;
    throw new DirectoryNotFoundException("PacConsole.sln not found above " + from);
}

/// <summary>One image: what to draw and how. Defaults match the app's main view.</summary>
public sealed record Shot
{
    public required string Id { get; init; }
    public string Theme { get; init; } = "dark";
    public int[] Size { get; init; } = [640, 360];
    public int Scale { get; init; } = 3;
    public string View { get; init; } = "iso";
    public float? Yaw { get; init; }
    public float? Elevation { get; init; }
    public string? Unit { get; init; }
    public string System { get; init; } = "All";
    public string? Selected { get; init; }
    public string[] Alerting { get; init; } = [];
    public Dictionary<string, float> Lift { get; init; } = [];
    public bool Details { get; init; } = true;
    public bool Motion { get; init; } = true;
    public double Seconds { get; init; } = 4;
    public int Exposure { get; init; }
    public float Padding { get; init; } = 24;
    public string Framing { get; init; } = "tight";
    public float Zoom { get; init; } = 1;
    public float[]? Offset { get; init; }
    public string[]? Fit { get; init; }
    public string Caption { get; init; } = "view";
    public string? AlarmKey { get; init; }
    public float Sweep { get; init; }

    public CameraPose Pose(IEnumerable<CameraPreset> presets)
    {
        var preset = presets.First(p => p.Key == View).Pose;
        return new CameraPose(Yaw ?? preset.Yaw, Elevation ?? preset.Elevation);
    }
}
