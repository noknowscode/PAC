using System.Globalization;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Console;

/// <summary>The operator's view choices that shape the page: system, trend range, compared readings and cursor.</summary>
public sealed record ViewInputs(PlantSystem System, TrendRange Range, ImmutableArray<string> Compared, int Cursor)
{
    public static readonly ViewInputs Default = new(PlantSystem.All, TrendRange.Minutes15, [], -1);
}

/// <summary>One reading in the inspector: a button that adds it to, or removes it from, the trends.</summary>
public sealed record ReadingTileView(string Key, string Label, string Value, string Unit, bool IsHigh, bool IsCompared)
{
    /// <summary>"high" or "normal": the value's ink.</summary>
    public string Tone => IsHigh ? "high" : "normal";

    /// <summary>"compared" or "normal": the tile's surface.</summary>
    public string Surface => IsCompared ? "compared" : "normal";

    /// <summary>The word that goes with the alert colour, so the state never rests on colour alone.</summary>
    public string StatusText => IsHigh ? "Above limit" : "";

    public string AutomationName =>
        $"{Label} {Value} {Unit}{(IsHigh ? ", alert" : "")}. {(IsCompared ? "In the trends; select to remove" : "Select to compare in the trends")}";
}

/// <summary>A connected item of equipment, as a chip that selects it.</summary>
public sealed record ConnectionView(UnitId Id, string Title)
{
    public override string ToString() => Title;
}

/// <summary>A valve slot in the Control tab: shown only on the equipment it belongs to.</summary>
public sealed record ValveSlotView(int Id, string Number, bool Open, bool IsHere, string AutomationName);

/// <summary>The console page v3 (SPEC-CONSOLE.md): status strip, systems, inspector, synchronized trends.</summary>
public sealed partial record ConsoleView
{
    // Header and status strip
    public string ActiveAlertText { get; init; } = "";
    public string TargetGapText { get; init; } = "";
    public string TargetText { get; init; } = "";
    public string ModeLine { get; init; } = "";
    public string SnapshotText { get; init; } = "";

    // Systems
    public PlantSystem System { get; init; }
    public string SystemTitle { get; init; } = "";
    public bool IsAllSystems => System == PlantSystem.All;
    public bool IsSteamWater => System == PlantSystem.SteamWater;
    public bool IsCooling => System == PlantSystem.Cooling;
    public bool IsFuelFlue => System == PlantSystem.FuelFlue;
    public bool IsElectrical => System == PlantSystem.Electrical;

    // Equipment picker. The choices are one shared array, so the picker's ItemsSource keeps the same
    // instance across recompositions and never resets (and closes) while it is open.
    private static readonly Dictionary<PlantSystem, ConnectionView[]> Choices = Enum.GetValues<PlantSystem>().ToDictionary(
        s => s,
        s => EquipmentCatalog.All.Where(e => PlantSystems.Contains(s, e.Id)).Select(e => new ConnectionView(e.Id, e.Title)).ToArray());

    /// <summary>The picker's choices: the equipment of the system in view.</summary>
    public IReadOnlyList<ConnectionView> EquipmentChoices => Choices[System];
    public int SelectedIndex { get; init; }

    // Inspector
    public string InspectorCode { get; init; } = "";
    public string InspectorName { get; init; } = "";
    public string InspectorDescription { get; init; } = "";
    public string InspectorState { get; init; } = "";
    public string ServiceText { get; init; } = "";
    public ImmutableArray<ReadingTileView> OverviewTiles { get; init; } = [];
    public ImmutableArray<ReadingTileView> ConditionTiles { get; init; } = [];
    public ImmutableArray<ConnectionView> Connections { get; init; } = [];
    public ImmutableArray<ValveSlotView> ValveSlots { get; init; } = [];
    public string ValvesNote { get; init; } = "";

    // Fixed slots (see the TempTile note in ConsoleView): buttons and charts stay put while their
    // records update in place.
    public ReadingTileView? Overview0 => At(OverviewTiles, 0);
    public ReadingTileView? Overview1 => At(OverviewTiles, 1);
    public ReadingTileView? Overview2 => At(OverviewTiles, 2);
    public ReadingTileView? Overview3 => At(OverviewTiles, 3);
    public ReadingTileView? Overview4 => At(OverviewTiles, 4);
    public ReadingTileView? Overview5 => At(OverviewTiles, 5);
    public ReadingTileView? Condition0 => At(ConditionTiles, 0);
    public ReadingTileView? Condition1 => At(ConditionTiles, 1);
    public ReadingTileView? Condition2 => At(ConditionTiles, 2);
    public ConnectionView? Connection0 => At(Connections, 0);
    public ConnectionView? Connection1 => At(Connections, 1);
    public ValveSlotView? Slot1 => At(ValveSlots, 0);
    public ValveSlotView? Slot2 => At(ValveSlots, 1);
    public ValveSlotView? Slot3 => At(ValveSlots, 2);
    public ValveSlotView? Slot4 => At(ValveSlots, 3);
    public ValveSlotView? Slot5 => At(ValveSlots, 4);
    public ValveSlotView? Slot6 => At(ValveSlots, 5);
    public ValveSlotView? Slot7 => At(ValveSlots, 6);
    public ValveSlotView? Slot8 => At(ValveSlots, 7);
    public ValveSlotView? Slot9 => At(ValveSlots, 8);

    // Synchronized trends
    public TrendSet? Trends { get; init; }
    public TrendSeries? Trend0 => Trends is { } t && t.Series.Length > 0 ? t.Series[0] : null;
    public TrendSeries? Trend1 => Trends is { } t && t.Series.Length > 1 ? t.Series[1] : null;
    public TrendSeries? Trend2 => Trends is { } t && t.Series.Length > 2 ? t.Series[2] : null;
    public TrendSeries? Trend3 => Trends is { } t && t.Series.Length > 3 ? t.Series[3] : null;
    public int CursorIndex => Trends?.Cursor ?? TrendSet.Samples - 1;
    public string CursorTime { get; init; } = "";
    public bool CursorIsLatest { get; init; } = true;
    public string Trend0Value => ValueAtCursor(Trend0);
    public string Trend1Value => ValueAtCursor(Trend1);
    public string Trend2Value => ValueAtCursor(Trend2);
    public string Trend3Value => ValueAtCursor(Trend3);
    public string TimeStart { get; init; } = "";
    public string TimeMid { get; init; } = "";
    public string TimeEnd { get; init; } = "";
    public string EventText { get; init; } = "";

    /// <summary>The sample index of the latest alert onset in the range, or -1: every trend marks it.</summary>
    public int EventIndex => Trends is { Events.IsEmpty: false } t ? t.Events[^1].Index : -1;
    public TrendRange Range { get; init; }
    public bool IsRange15 => Range == TrendRange.Minutes15;
    public bool IsRange1h => Range == TrendRange.Hour1;
    public bool IsRange8h => Range == TrendRange.Hours8;

    private static T? At<T>(ImmutableArray<T> items, int i) where T : class => i < items.Length ? items[i] : null;

    private string ValueAtCursor(TrendSeries? s) => s is null ? "" : s.Format1(s.Values[Math.Clamp(CursorIndex, 0, s.Values.Length - 1)]);

    /// <summary>Which valves sit on which equipment: steam admission, condensate, cooling water, fuel, damper bypass.</summary>
    public static UnitId? ValveOwner(int id) => id switch
    {
        101 or 102 or 103 => UnitId.Trb,
        104 or 105 => UnitId.Cnd,
        106 or 107 => UnitId.Clg,
        108 => UnitId.Tka,
        109 => UnitId.Stk,
        _ => null,
    };

    /// <summary>Adds the v3 page's fields to a composed view.</summary>
    private static ConsoleView WithV3(ConsoleView view, PlantSnapshot snapshot, Setpoints setpoints, ImmutableArray<Valve> valves,
        ControlMode mode, ImmutableArray<UnitReading> readings, ImmutableArray<Alert> alerts, double netMw, DateTimeOffset now, int age, ViewInputs inputs)
    {
        var selected = view.Selected;
        var equipment = EquipmentCatalog.Get(selected);
        var info = equipment.Info;
        var reading = readings.First(r => r.Id == selected);
        var context = new ReadingContext(reading, snapshot.Tick, netMw, setpoints);
        var highKeys = alerts.Where(a => a.Unit == selected).Select(EquipmentCatalog.KeyFor).ToHashSet();

        // Compared readings of the selected equipment; none picked yet means its first reading.
        var compared = inputs.Compared.Where(k => EquipmentCatalog.Find(k) is not null && EquipmentCatalog.UnitOf(k) == selected).ToList();
        if (compared.Count == 0) compared.Add(equipment.Readings[0].Key);

        ReadingTileView Tile(ReadingSpec spec) => new(
            spec.Key,
            spec.Label,
            spec.Value(context).ToString(spec.Format, CultureInfo.InvariantCulture),
            spec.Unit,
            highKeys.Contains(spec.Key),
            compared.Contains(spec.Key));

        var trends = TrendSet.Compose(snapshot.Tick, now, setpoints, valves.Count(v => v.Open), inputs.Range, compared, inputs.Cursor);
        var gap = (int)Math.Round(netMw - setpoints.TargetMw);
        var alertCount = view.AlertingUnits.Length;
        var here = valves.Where(v => ValveOwner(v.Id) == selected).ToList();

        return view with
        {
            ActiveAlertText = alertCount == 0 ? "No active alerts" : $"{alertCount} active alert{(alertCount > 1 ? "s" : "")}",
            TargetGapText = gap == 0 ? "On target" : $"{Math.Abs(gap)} MW {(gap < 0 ? "below" : "above")} target",
            TargetText = $"Target {setpoints.TargetMw.ToString("0", CultureInfo.InvariantCulture)} MW",
            ModeLine = mode switch
            {
                ControlMode.Manual => "Generating · Manual",
                ControlMode.Hold => view.DataIsStale ? $"Data held {age} s · stale" : $"Data held {age} s",
                _ => "Generating · Automatic",
            },
            SnapshotText = $"Snapshot {snapshot.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}",

            System = inputs.System,
            SystemTitle = PlantSystems.Title(inputs.System),

            SelectedIndex = Array.FindIndex(Choices[inputs.System], c => c.Id == selected),

            InspectorCode = info.Code,
            InspectorName = info.Name,
            InspectorDescription = equipment.Description,
            InspectorState = view.SelectedIsAlert ? "Alert" : "Nominal",
            ServiceText = $"Up {info.Uptime} · serviced {info.LastService}",
            OverviewTiles = [.. equipment.Readings.Where(r => r.Group == ReadingGroup.Overview).Select(Tile)],
            ConditionTiles = [.. equipment.Readings.Where(r => r.Group == ReadingGroup.Condition).Select(Tile)],
            Connections = [.. equipment.Connections.Select(c => new ConnectionView(c, EquipmentCatalog.Get(c).Title))],
            ValveSlots = [.. valves.Select(v => new ValveSlotView(v.Id, v.Id.ToString(CultureInfo.InvariantCulture), v.Open, ValveOwner(v.Id) == selected,
                $"Valve {v.Code} {(v.Open ? "open" : "shut")}"))],
            ValvesNote = here.Count == 0
                ? $"No valves on {info.Code}."
                : $"{here.Count(v => v.Open)} of {here.Count} open on {info.Code}",

            Trends = trends,
            Range = inputs.Range,
            CursorTime = trends.Times[trends.Cursor].ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            CursorIsLatest = trends.Cursor == TrendSet.Samples - 1,
            TimeStart = trends.Times[0].ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
            TimeMid = trends.Times[TrendSet.Samples / 2].ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
            TimeEnd = trends.Times[^1].ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
            EventText = trends.Events.IsEmpty
                ? "No alerts started in this range"
                : $"{trends.Events[^1].Time} · {trends.Events[^1].Text}",
        };
    }
}
