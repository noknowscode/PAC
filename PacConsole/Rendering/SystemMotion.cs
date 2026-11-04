using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using SkiaSharp;

namespace PacConsole.Rendering;

/// <summary>
/// Contextual motion for the isolated system, drawn live over the plant (never recorded): smoke from the stacks (fuel
/// and flue gas), vapour plumes over the fan stacks (cooling), wisps from the drum's safety valve (steam and water),
/// energy beads along overhead conductors and pulse rings at the bushings (electrical), and the breathing halo round the
/// equipment in alarm. Every value follows from the clock, so a still frame and a moving one agree.
/// </summary>
internal sealed class SystemMotion(SolidPalette palette) : IDisposable
{
    private readonly SKPathBuilder _pb = new();
    private readonly SKMaskFilter _puffBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f);
    private readonly SKPaint _puff = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _ring = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1.6f };
    private readonly SKPaint _glow = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 3, StrokeJoin = SKStrokeJoin.Round };

    // Active alarms: the halo round the equipment in alarm, and its breath.
    private readonly SKMaskFilter _alarmBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4);
    // The halo's outline changes only with the camera, a lift or the units in alarm; the breath is paint.
    private SKPath? _alarmHalo;
    private (object? Shapes, object? Lifts, int Alerts) _alarmHaloKey;
    private const double AlarmBreathSeconds = 1.6;

    /// <summary>Where a system's motion starts: a point on a part, and the unit it lifts with.</summary>
    private readonly record struct Emitter(Vec3 At, UnitId? Unit, int Index);

    private IReadOnlyList<SolidPart>? _emitterParts;
    private Emitter[] _smoke = [], _plumes = [], _vents = [], _bushings = [];

    // Electrical: each generator bushing pairs with a transformer bushing (west to east), joined by
    // an overhead conductor that only the electrical view draws.
    private (Emitter From, Emitter To)[] _conductors = [];
    // Each conductor's arch follows the camera and its two units' lifts; the beads move along it.
    private SKPath?[] _arcs = [];
    private (object? Shapes, object? Lifts, object? Conductors) _arcsKey;
    private readonly SKPaint _wire = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1.2f, StrokeCap = SKStrokeCap.Round };

    /// <summary>How high a conductor arches above the straight line between its bushings, in world units.</summary>
    private const float ConductorArch = 6;

    /// <summary>Draws the isolated system's motion over the plant, with each unit at its lift in <paramref name="geometry"/>.</summary>
    public void Draw(SKCanvas canvas, IReadOnlyList<SolidPart> parts, OrbitProjection projection, SolidFrame frame, SolidGeometry geometry)
    {
        if (!frame.Motion || !frame.Details || frame.System == PlantSystem.All) return;
        if (!palette.Systems.TryGetValue(frame.System, out var ink)) return;
        if (!SolidGeometry.SameParts(parts, _emitterParts)) FindEmitters(parts);
        var t = frame.Seconds;
        var scale = projection.Scale;
        float LiftOf(UnitId? unit) => SolidGeometry.LiftOf(frame, unit);

        Vec2 At(Emitter e, float dx, float dy) =>
            projection.Project(new Vec3(e.At.X + dx, e.At.Y + dy + LiftOf(e.Unit), e.At.Z));

        // A soft puff rises dy world units over its life while it widens and fades; puffs are staggered.
        void Puffs(Emitter[] emitters, int count, double life, float rise, float drift, float r0, float r1, SKColor color, byte alpha)
        {
            _puff.MaskFilter = _puffBlur;
            foreach (var e in emitters)
            {
                for (var k = 0; k < count; k++)
                {
                    var phase = (float)Frac(t / life + (double)k / count + e.Index * 0.37);
                    var rising = 1 - (1 - phase) * (1 - phase); // ease-out: fast off the lip, slowing as it cools
                    var p = At(e, drift * phase, rise * rising);
                    var fade = (1 - phase) * Math.Min(1, phase * 6); // in over the first sixth, then out
                    _puff.Color = color.WithAlpha((byte)(alpha * fade));
                    canvas.DrawCircle(p.X, p.Y, (r0 + (r1 - r0) * phase) * scale, _puff);
                }
            }
            _puff.MaskFilter = null;
        }

        switch (frame.System)
        {
            case PlantSystem.Alarms:
                // The equipment in alarm breathes in the alert ink: one blurred halo round all of its
                // parts, 1.6 s a breath (the status dots' rhythm), drawn live over the cached plant.
                var breath = (float)(0.5 + 0.5 * Math.Cos(t / AlarmBreathSeconds * 2 * Math.PI));
                // The shapes are replaced when the camera moves and the lifts when a lift does.
                var haloKey = ((object?)geometry.Shapes, (object?)geometry.Lifts, SolidGeometry.AlertMask(frame.Alerting));
                if (haloKey != _alarmHaloKey)
                {
                    _alarmHaloKey = haloKey;
                    for (var i = 0; i < parts.Count; i++)
                    {
                        if (parts[i].Unit is not { } unit || !frame.Alerting.Contains(unit) || SolidGeometry.IsStand(parts[i])) continue;
                        var dy = projection.Rise(LiftOf(unit));
                        foreach (var line in geometry.Shapes[i].Outline)
                        {
                            if (line.Length < 2) continue;
                            _pb.MoveTo(line[0].X, line[0].Y + dy);
                            for (var k = 1; k < line.Length; k++) _pb.LineTo(line[k].X, line[k].Y + dy);
                        }
                    }
                    _alarmHalo?.Dispose();
                    _alarmHalo = _pb.Detach();
                }
                if (_alarmHalo is null || _alarmHalo.IsEmpty) break;
                _glow.Color = ink.WithAlpha((byte)(0x28 + 0xA8 * breath));
                _glow.StrokeWidth = 3 + 4 * breath;
                _glow.MaskFilter = _alarmBlur;
                canvas.DrawPath(_alarmHalo, _glow);
                _glow.MaskFilter = null;
                _glow.StrokeWidth = 3;
                break;
            case PlantSystem.FuelFlue:
                Puffs(_smoke, 6, 3.2, 30, 6, 3, 9, PartPainter.Lerp(ink, palette.Lo, 0.4f), 0x90);
                break;
            case PlantSystem.Cooling:
                Puffs(_plumes, 4, 2.6, 24, 3, 3.5f, 10, PartPainter.Lerp(ink, palette.Paper, 0.3f), 0x58);
                break;
            case PlantSystem.SteamWater:
                Puffs(_vents, 5, 2.2, 18, 4, 1.5f, 6, PartPainter.Lerp(ink, palette.Paper, 0.2f), 0xB0);
                break;
            case PlantSystem.Electrical:
                // Energy along the overhead conductors: two beads per conductor make the 1.1 s trip from
                // the generator to the transformer, fading in and out at the ends, each a blurred halo
                // round a bright core.
                Vec2 Along((Emitter From, Emitter To) c, float u)
                {
                    var (a, b) = (c.From.At, c.To.At);
                    var lift = LiftOf(c.From.Unit) * (1 - u) + LiftOf(c.To.Unit) * u;
                    var y = a.Y + (b.Y - a.Y) * u + 0.5f + ConductorArch * 4 * u * (1 - u) + lift;
                    return projection.Project(new Vec3(a.X + (b.X - a.X) * u, y, a.Z + (b.Z - a.Z) * u));
                }
                var arcsKey = ((object?)geometry.Shapes, (object?)geometry.Lifts, (object?)_conductors);
                if (arcsKey != _arcsKey)
                {
                    _arcsKey = arcsKey;
                    foreach (var old in _arcs) old?.Dispose();
                    _arcs = new SKPath?[_conductors.Length];
                    for (var i = 0; i < _conductors.Length; i++)
                    {
                        var start = Along(_conductors[i], 0);
                        _pb.MoveTo(start.X, start.Y);
                        for (var j = 1; j <= 16; j++)
                        {
                            var q = Along(_conductors[i], j / 16f);
                            _pb.LineTo(q.X, q.Y);
                        }
                        _arcs[i] = _pb.Detach();
                    }
                }
                for (var i = 0; i < _conductors.Length; i++)
                {
                    var c = _conductors[i];
                    _wire.Color = ink.WithAlpha(0x99);
                    canvas.DrawPath(_arcs[i]!, _wire);
                    for (var k = 0; k < 2; k++)
                    {
                        var u = (float)Frac(t / 1.1 + k / 2.0 + i * 0.15);
                        var bead = Along(c, u);
                        var strength = MathF.Sin(MathF.PI * u);
                        _puff.MaskFilter = _puffBlur;
                        _puff.Color = ink.WithAlpha((byte)(0xB0 * strength));
                        canvas.DrawCircle(bead.X, bead.Y, 2.4f * scale, _puff);
                        _puff.MaskFilter = null;
                        _puff.Color = ink.WithAlpha((byte)(0xFF * strength));
                        canvas.DrawCircle(bead.X, bead.Y, 0.7f * scale, _puff);
                    }
                }
                // A ring leaves each bushing every 1.4 s and widens from 2 to 12 units as it fades.
                foreach (var e in _bushings)
                {
                    var phase = (float)Frac(t / 1.4 + e.Index * 0.5);
                    var p = At(e, 0, 0.5f);
                    _ring.Color = ink.WithAlpha((byte)(0xFF * (1 - phase)));
                    canvas.DrawCircle(p.X, p.Y, (2 + 10 * phase) * scale, _ring);
                }
                break;
        }
    }

    private static double Frac(double x) => x - Math.Floor(x);

    private void FindEmitters(IReadOnlyList<SolidPart> parts)
    {
        _emitterParts = parts;
        Emitter[] Tops(Func<SolidPart, bool> match) =>
            [.. parts.Where(match).Select((p, i) => new Emitter(TopCentre(p.Volume), p.Unit, i))];
        _smoke = Tops(p => p.Name == "stk.lip");
        _plumes = Tops(p => p.Name == "clg.stack");
        _vents = Tops(p => p.Name == "boiler.safety");
        _bushings = Tops(p => p.Name is "gen.bushing.cap" or "xfmr.bushing.cap");
        var from = Tops(p => p.Name == "gen.bushing.cap").OrderBy(e => e.At.X).ToArray();
        var to = Tops(p => p.Name == "xfmr.bushing.cap").OrderBy(e => e.At.X).ToArray();
        _conductors = [.. from.Zip(to)];
    }

    private static Vec3 TopCentre(Volume v)
    {
        float x = 0, z = 0;
        foreach (var q in v.Top) { x += q.X; z += q.Z; }
        return new Vec3(x / v.Top.Length, v.Y1, z / v.Top.Length);
    }

    public void Dispose()
    {
        _pb.Dispose();
        _puff.Dispose();
        _puffBlur.Dispose();
        _ring.Dispose();
        _glow.Dispose();
        _wire.Dispose();
        foreach (var arc in _arcs) arc?.Dispose();
        _alarmHalo?.Dispose();
        _alarmBlur.Dispose();
    }
}
