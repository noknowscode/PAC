namespace PacConsole.Core.Plant;

/// <summary>
/// A process system the plant view can isolate. Equipment outside it is drawn faded. <see cref="Alarms"/>
/// is the equipment in alarm right now, so its members come from the alarm list, not the plant's piping.
/// </summary>
public enum PlantSystem
{
    All,
    SteamWater,
    Cooling,
    FuelFlue,
    Electrical,
    Alarms,
}

public static class PlantSystems
{
    public static string Label(PlantSystem s) => s switch
    {
        PlantSystem.SteamWater => "Steam & water",
        PlantSystem.Cooling => "Cooling",
        PlantSystem.FuelFlue => "Fuel & flue gas",
        PlantSystem.Electrical => "Electrical",
        PlantSystem.Alarms => "Active alarms",
        _ => "All systems",
    };

    /// <summary>The plant panel's title for the system.</summary>
    public static string Title(PlantSystem s) => s switch
    {
        PlantSystem.SteamWater => "Steam and water cycle",
        PlantSystem.Cooling => "Cooling",
        PlantSystem.FuelFlue => "Fuel and flue gas",
        PlantSystem.Electrical => "Electrical generation",
        PlantSystem.Alarms => "Active alarms",
        _ => "Plant overview",
    };

    /// <summary>
    /// The unit to select when the operator isolates <paramref name="system"/>: the current one if it
    /// belongs to the system; else the system's first unit in alert, the likely reason for isolating
    /// it; else its first unit. Ties go by plant order, as the alert pill's do. So the inspector and
    /// the trends follow the drawing.
    /// </summary>
    public static UnitId SelectionFor(PlantSystem system, UnitId? current, IReadOnlyCollection<UnitId>? alerting = null)
    {
        if (current is { } unit && Contains(system, unit, alerting)) return unit;
        var units = Enum.GetValues<UnitId>().Where(u => Contains(system, u, alerting)).ToList();
        // Active alarms with nothing in alarm holds no equipment: the selection stays where it is.
        if (units.Count == 0) return current ?? Enum.GetValues<UnitId>()[0];
        return alerting is null ? units[0] : units.FirstOrDefault(alerting.Contains, units[0]);
    }

    /// <summary>
    /// The system to show once <paramref name="unit"/> is selected some other way (a callout, the alert
    /// pill, a connection chip): the current one if it holds the unit, else all systems, so the
    /// selection is never a unit the view has faded out.
    /// </summary>
    public static PlantSystem SystemFor(PlantSystem current, UnitId unit, IReadOnlyCollection<UnitId>? alerting = null) =>
        Contains(current, unit, alerting) ? current : PlantSystem.All;

    /// <summary>
    /// The equipment in a system (all of it for <see cref="PlantSystem.All"/>; for
    /// <see cref="PlantSystem.Alarms"/>, the units in <paramref name="alerting"/>).
    /// </summary>
    public static bool Contains(PlantSystem s, UnitId unit, IReadOnlyCollection<UnitId>? alerting = null) => s switch
    {
        PlantSystem.Alarms => alerting?.Contains(unit) == true,
        PlantSystem.All => true,
        PlantSystem.SteamWater => unit is UnitId.Trb or UnitId.Cnd,
        PlantSystem.Cooling => unit is UnitId.Clg or UnitId.Cnd,
        PlantSystem.FuelFlue => unit is UnitId.Tka or UnitId.Stk or UnitId.Sil,
        PlantSystem.Electrical => unit is UnitId.Gen or UnitId.Trb,
        _ => false,
    };
}
