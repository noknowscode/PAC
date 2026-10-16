using System.Globalization;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Operations;

/// <summary>
/// A reading past its alarm's clear threshold at one tick. <see cref="Raised"/> when past the limit
/// itself; an alarm already standing holds while the reading stays past the clear threshold.
/// </summary>
public sealed record Alarm(string Key, UnitId Unit, AlarmPriority Priority, double Value, double Limit, bool Raised = true);

/// <summary>Every alarm comes from a reading spec with an <see cref="AlarmSpec"/>, so tiles, trends and the alarm list agree.</summary>
public static class AlarmRules
{
    private static readonly (Equipment Equipment, ReadingSpec Spec)[] Alarmed =
        [.. EquipmentCatalog.All.SelectMany(e => e.Readings.Where(r => r.Alarm is not null).Select(r => (e, r)))];

    /// <summary>The readings past their clear threshold at <paramref name="tick"/>, in plant order; <see cref="Alarm.Raised"/> past the limit.</summary>
    public static ImmutableArray<Alarm> Evaluate(long tick, ImmutableArray<UnitReading> readings, double netMw, Setpoints setpoints, PlantOps ops)
    {
        var alarms = ImmutableArray.CreateBuilder<Alarm>();
        foreach (var (equipment, spec) in Alarmed)
        {
            var reading = readings[(int)equipment.Id];
            var value = spec.Value(new ReadingContext(reading, tick, netMw, setpoints, ops));
            var limit = spec.Limit!(setpoints);
            var a = spec.Alarm!;
            var held = a.Above ? value > limit - a.Deadband : value < limit + a.Deadband;
            if (held)
            {
                alarms.Add(new Alarm(spec.Key, equipment.Id, a.Priority, value, limit, a.Above ? value > limit : value < limit));
            }
        }
        return alarms.ToImmutable();
    }

    /// <summary>
    /// The alarms standing after one more tick, with hysteresis: a raised reading brings an alarm in;
    /// one already standing stays while its reading is past the clear threshold.
    /// </summary>
    public static ImmutableArray<Alarm> Standing(ImmutableArray<Alarm> before, ImmutableArray<Alarm> now)
    {
        var standing = ImmutableArray.CreateBuilder<Alarm>();
        foreach (var a in now)
        {
            if (a.Raised || Contains(before, a.Key)) standing.Add(a);
        }
        return standing.ToImmutable();
    }

    public static bool Contains(ImmutableArray<Alarm> alarms, string key)
    {
        foreach (var a in alarms) if (a.Key == key) return true;
        return false;
    }

    /// <summary>The alarms at a tick (readings and output from the simulation, remembered in <see cref="SampleMemo"/>).</summary>
    public static ImmutableArray<Alarm> At(long tick, Setpoints setpoints, PlantOps ops) => SampleMemo.AlarmsAt(tick, setpoints, ops);

    /// <summary>Units with an alarm that is not shelved, in plant order.</summary>
    public static ImmutableArray<UnitId> AlertingUnits(IEnumerable<Alarm> alarms, PlantOps ops, long tick) =>
        [.. alarms.Where(a => !ops.IsShelved(a.Key, tick)).Select(a => a.Unit).Distinct().OrderBy(u => u)];
}

public enum AlarmState { Unacknowledged, Acknowledged, ReturnedUnacknowledged, Shelved }

/// <summary>One line of the alarm summary.</summary>
public sealed record AlarmEntry(
    string Key,
    UnitId Unit,
    AlarmPriority Priority,
    string Title,
    double Value,
    double Limit,
    string Format,
    string UnitOfMeasure,
    long OnsetTick,
    bool OnsetBeforeWindow,
    AlarmState State,
    long ShelvedUntil)
{
    public bool IsActive => State is AlarmState.Unacknowledged or AlarmState.Acknowledged;
    public bool NeedsAck => State is AlarmState.Unacknowledged or AlarmState.ReturnedUnacknowledged;
}

/// <summary>An alarm coming in or going out, for the log.</summary>
public sealed record AlarmTransition(long Tick, string Key, UnitId Unit, bool Raised, string Text);

/// <summary>
/// The alarm summary at one tick: alarms standing now and those that returned to normal without an
/// acknowledgement, with their state. Derived by replaying the last <see cref="WindowTicks"/> ticks
/// through the simulation with the operator's actions, so nothing but acknowledgements and shelves
/// needs to be stored.
/// </summary>
public sealed record AlarmBook(ImmutableArray<AlarmEntry> Entries, ImmutableArray<AlarmTransition> Transitions)
{
    /// <summary>Fifteen minutes of ticks (2 s).</summary>
    public const int WindowTicks = 450;

    public static AlarmBook Compose(long tick, Setpoints setpoints, PlantOps ops)
    {
        var start = Math.Max(0, tick - WindowTicks + 1);
        var onset = new Dictionary<string, long>();
        var lastOnset = new Dictionary<string, long>();
        var transitions = ImmutableArray.CreateBuilder<AlarmTransition>();
        // A tick stands a handful of alarms at most, so consecutive ticks compare by a linear scan:
        // no set is built per tick (a HashSet per tick was most of the replay's cost).
        static bool Has(ImmutableArray<Alarm> alarms, string key) => AlarmRules.Contains(alarms, key);
        ImmutableArray<Alarm> before = [];
        ImmutableArray<Alarm> now = [];
        for (var t = start; t <= tick; t++)
        {
            var alarms = AlarmRules.Standing(before, AlarmRules.At(t, setpoints, ops));
            foreach (var a in alarms)
            {
                if (Has(before, a.Key)) continue;
                onset[a.Key] = t;
                lastOnset[a.Key] = t;
                if (t > start) transitions.Add(new AlarmTransition(t, a.Key, a.Unit, true, $"{Title(a.Key)}: {Format(a.Key, a.Value)} (limit {Format(a.Key, a.Limit)})"));
            }
            foreach (var b in before)
            {
                if (Has(alarms, b.Key)) continue;
                transitions.Add(new AlarmTransition(t, b.Key, b.Unit, false, $"Returned to normal: {Title(b.Key)}"));
            }
            before = alarms;
            if (t == tick) now = alarms;
        }

        var entries = new List<AlarmEntry>();
        foreach (var a in now)
        {
            var spec = EquipmentCatalog.Find(a.Key)!;
            var since = onset[a.Key];
            var acked = ops.Acks.TryGetValue(a.Key, out var ack) && ack >= since;
            var state = ops.IsShelved(a.Key, tick) ? AlarmState.Shelved : acked ? AlarmState.Acknowledged : AlarmState.Unacknowledged;
            entries.Add(Entry(a.Key, spec, a.Value, a.Limit, since, since == start, state, ops, tick));
        }
        // Returned to normal in the window without an acknowledgement since it came in: it stays until acknowledged.
        foreach (var (key, since) in lastOnset.Where(kv => now.All(a => a.Key != kv.Key)))
        {
            if (ops.Acks.TryGetValue(key, out var ack) && ack >= since) continue;
            if (ops.IsShelved(key, tick)) continue;
            var spec = EquipmentCatalog.Find(key)!;
            entries.Add(Entry(key, spec, double.NaN, spec.Limit!(setpoints), since, since == start, AlarmState.ReturnedUnacknowledged, ops, tick));
        }

        var ordered = entries
            .OrderBy(e => e.State switch { AlarmState.Unacknowledged => 0, AlarmState.ReturnedUnacknowledged => 1, AlarmState.Acknowledged => 2, _ => 3 })
            .ThenByDescending(e => e.Priority)
            .ThenBy(e => e.OnsetTick)
            .ToImmutableArray();
        return new AlarmBook(ordered, transitions.ToImmutable());
    }

    private static AlarmEntry Entry(string key, ReadingSpec spec, double value, double limit, long since, bool beforeWindow, AlarmState state, PlantOps ops, long tick) =>
        new(key, EquipmentCatalog.UnitOf(key), spec.Alarm!.Priority, Title(key), value, limit, spec.Format, spec.Unit, since, beforeWindow, state,
            ops.Shelves.TryGetValue(key, out var until) ? until : 0);

    /// <summary>"CLG-05 cooling fan vibration high".</summary>
    public static string Title(string key)
    {
        var spec = EquipmentCatalog.Find(key)!;
        var title = spec.Alarm!.Title;
        return $"{UnitCatalog.Get(EquipmentCatalog.UnitOf(key)).Code} {char.ToLowerInvariant(title[0])}{title[1..]}";
    }

    private static string Format(string key, double value) =>
        $"{value.ToString(EquipmentCatalog.Find(key)!.Format, CultureInfo.InvariantCulture)} {EquipmentCatalog.Find(key)!.Unit}".TrimEnd();

    public int ActiveCount => Entries.Count(e => e.IsActive);
    public int UnacknowledgedCount => Entries.Count(e => e.NeedsAck);
    public int ShelvedCount => Entries.Count(e => e.State == AlarmState.Shelved);
}
