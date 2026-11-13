using PacConsole.Core.Plant;

namespace PacConsole.Core.Telemetry;

public enum Metric
{
    Temp,
    Press,
    Flow,
    Load,
    Eff,
    Vib,
}

/// <summary>One unit's readings at one tick.</summary>
public sealed record UnitReading(UnitId Id, double Temp, double Press, double Flow, double Load, double Eff, double Vib)
{
    public double Get(Metric metric) => metric switch
    {
        Metric.Temp => Temp,
        Metric.Press => Press,
        Metric.Flow => Flow,
        Metric.Load => Load,
        Metric.Eff => Eff,
        Metric.Vib => Vib,
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };
}

/// <summary>What the telemetry stream yields: a tick number, when it was sampled, and every unit's readings.</summary>
public sealed record PlantSnapshot(long Tick, DateTimeOffset At, ImmutableArray<UnitReading> Readings)
{
    public UnitReading For(UnitId id) => Readings.First(r => r.Id == id);
}

/// <summary>The operator's three setpoints, with the prototype's defaults.</summary>
public sealed record Setpoints(double TargetMw = 620, double TurbineLimitBar = 170, double DamperPct = 65)
{
    public static readonly Setpoints Default = new();
}

/// <summary>What a typed setpoint may be: whole units from <see cref="Min"/> to <see cref="Max"/>.</summary>
public sealed record SetpointRange(double Min, double Max)
{
    /// <summary>The value to apply for a typed draft, rounded and kept in range; null for an empty field (NaN).</summary>
    public double? Apply(double draft) => double.IsNaN(draft) ? null : Math.Clamp(Math.Round(draft), Min, Max);

    /// <summary>Each faceplate field's range. Its NumberBox and hint repeat these numbers, which XAML cannot bind.</summary>
    public static readonly SetpointRange Load = new(400, 700);
    public static readonly SetpointRange TurbineLimit = new(140, 180);
    public static readonly SetpointRange Damper = new(0, 100);
    public static readonly SetpointRange FanSpeed = new(60, 100);
    public static readonly SetpointRange Reactive = new(0, 200);
}

public sealed record Valve(int Id, bool Open)
{
    public string Code => $"V-{Id}";

    public static ImmutableArray<Valve> Defaults { get; } =
        [.. new[] { true, true, false, true, true, true, false, true, true }.Select((open, i) => new Valve(101 + i, open))];
}

