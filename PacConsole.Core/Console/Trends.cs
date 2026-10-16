using System.Globalization;
using PacConsole.Core.Operations;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Console;

/// <summary>How far back the synchronized trends reach.</summary>
public enum TrendRange
{
    Minutes15,
    Hour1,
    Hours8,
}

/// <summary>
/// One trend row: a reading's samples over the range, its value range, and its reference lines.
/// A class, not a record: a chart takes it whole through one dependency property, and MVUX would
/// update a nested record in place (the property would never see a new value).
/// </summary>
public sealed class TrendSeries(string key, string label, string code, string unit, string format, ImmutableArray<double> values, double min, double max, double? target, double? limit)
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Code { get; } = code;
    public string Unit { get; } = unit;
    public string Format { get; } = format;
    public ImmutableArray<double> Values { get; } = values;
    public double Min { get; } = min;
    public double Max { get; } = max;
    public double? Target { get; } = target;
    public double? Limit { get; } = limit;

    public string Format1(double v) => v.ToString(Format, CultureInfo.InvariantCulture);
}

/// <summary>An alert onset inside the range: the sample index, the clock time and what happened.</summary>
public sealed record TrendEvent(int Index, string Time, string Text);

/// <summary>The trends for one moment: rows, sample times, events and the shared cursor.</summary>
public sealed class TrendSet(ImmutableArray<TrendSeries> series, ImmutableArray<DateTimeOffset> times, ImmutableArray<TrendEvent> events, int cursor)
{
    public ImmutableArray<TrendSeries> Series { get; } = series;
    public ImmutableArray<DateTimeOffset> Times { get; } = times;
    public ImmutableArray<TrendEvent> Events { get; } = events;

    /// <summary>The shared cursor's sample index (the newest when the operator has not moved it).</summary>
    public int Cursor { get; } = cursor;

    public const int Samples = 90;

    /// <summary>Ticks per range (one tick is 2 s): 15 min, 1 h, 8 h.</summary>
    public static int TicksIn(TrendRange r) => r switch { TrendRange.Hour1 => 1800, TrendRange.Hours8 => 14400, _ => 450 };

    /// <summary>
    /// Samples the net output and each compared reading over the range (bucket means), ending at
    /// <paramref name="tick"/>; finds alert onsets; places the cursor (-1 = newest).
    /// </summary>
    public static TrendSet Compose(long tick, DateTimeOffset now, Setpoints setpoints, PlantOps ops, TrendRange range, IEnumerable<string> compared, int cursor)
    {
        var step = TicksIn(range) / (double)(Samples - 1);
        var ticks = Enumerable.Range(0, Samples).Select(k => tick - (long)Math.Round((Samples - 1 - k) * step)).ToArray();
        var times = ticks.Select(t => now - TimeSpan.FromSeconds((tick - t) * 2)).ToImmutableArray();
        var samples = ticks.Select(t => SampleMemo.At(t, setpoints, ops)).ToArray();
        var readings = samples.Select(s => s.Readings).ToArray();

        // Each sample is the mean over its time bucket, as a historian reports it: point samples of a
        // fast-moving signal alias into a zigzag. Up to 12 evenly spaced ticks per bucket.
        var specs = compared.Select(k => EquipmentCatalog.Find(k)).OfType<ReadingSpec>().ToList();
        var units = specs.Select(spec => EquipmentCatalog.UnitOf(spec.Key)).ToArray();
        var sub = (int)Math.Clamp(Math.Round(step), 1, 12);
        var net = new double[Samples];
        var series = specs.Select(_ => new double[Samples]).ToList();
        for (var k = 0; k < Samples; k++)
        {
            for (var j = 0; j < sub; j++)
            {
                var t = ticks[k] - (long)Math.Round(j * step / sub);
                var point = SampleMemo.At(t, setpoints, ops);
                var mw = point.NetMw;
                net[k] += mw / sub;
                if (specs.Count == 0) continue;
                var sample = point.Readings;
                for (var i = 0; i < specs.Count; i++)
                {
                    var unit = units[i];
                    series[i][k] += specs[i].Value(new ReadingContext(sample.First(r => r.Id == unit), t, mw, setpoints, ops)) / sub;
                }
            }
        }

        var rows = ImmutableArray.CreateBuilder<TrendSeries>();
        rows.Add(Row("net", "Net generation", "", "MW", "0", net, setpoints.TargetMw, null));
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var unit = EquipmentCatalog.UnitOf(spec.Key);
            rows.Add(Row(spec.Key, spec.Label, UnitCatalog.Get(unit).Code, spec.Unit, spec.Format, series[i], spec.Target?.Invoke(setpoints), spec.Limit?.Invoke(setpoints)));
        }

        // An event is an alarm that comes in inside the range (absent at the previous sample).
        var events = ImmutableArray.CreateBuilder<TrendEvent>();
        var before = AlarmRules.At(ticks[0], setpoints, ops).Where(a => a.Raised).ToImmutableArray();
        for (var k = 1; k < Samples; k++)
        {
            var alarms = AlarmRules.Standing(before, AlarmRules.At(ticks[k], setpoints, ops));
            foreach (var a in alarms.Where(a => !AlarmRules.Contains(before, a.Key)))
            {
                var spec = EquipmentCatalog.Find(a.Key)!;
                events.Add(new(k, times[k].ToString("HH:mm", CultureInfo.InvariantCulture),
                    $"{AlarmBook.Title(a.Key)} ({AlarmBook.Onset(a.Key, a.Value)})"));
            }
            before = alarms;
        }

        var at = cursor < 0 || cursor >= Samples ? Samples - 1 : cursor;
        return new TrendSet(rows.ToImmutable(), times, events.ToImmutable(), at);
    }

    private static TrendSeries Row(string key, string label, string code, string unit, string format, double[] raw, double? target, double? limit)
    {
        var values = Smooth(raw);
        var lo = values.Min();
        var hi = values.Max();
        foreach (var r in new[] { target, limit })
        {
            if (r is { } v) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        }
        var pad = Math.Max((hi - lo) * 0.15, Math.Max(Math.Abs(hi) * 1e-4, 1e-3));
        return new TrendSeries(key, label, code, unit, format, [.. values], lo - pad, hi + pad, target, limit);
    }

    /// <summary>
    /// A 1-2-3-2-1 weighted moving average, so a trend reads as the signal's drift rather than the
    /// simulation's per-tick noise. The ends use the weights that fall inside the series.
    /// </summary>
    internal static double[] Smooth(double[] values)
    {
        ReadOnlySpan<double> weights = [1, 2, 3, 2, 1];
        var result = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            double sum = 0, total = 0;
            for (var w = 0; w < weights.Length; w++)
            {
                var j = i + w - 2;
                if (j < 0 || j >= values.Length) continue;
                sum += values[j] * weights[w];
                total += weights[w];
            }
            result[i] = sum / total;
        }
        return result;
    }
}
