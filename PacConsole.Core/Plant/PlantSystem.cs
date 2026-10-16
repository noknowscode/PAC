namespace PacConsole.Core.Plant;

/// <summary>A process system the plant view can isolate. Equipment outside it is drawn faded.</summary>
public enum PlantSystem
{
    All,
    SteamWater,
    Cooling,
    FuelFlue,
    Electrical,
}

public static class PlantSystems
{
    public static string Label(PlantSystem s) => s switch
    {
        PlantSystem.SteamWater => "Steam & water",
        PlantSystem.Cooling => "Cooling",
        PlantSystem.FuelFlue => "Fuel & flue gas",
        PlantSystem.Electrical => "Electrical",
        _ => "All systems",
    };

    /// <summary>The plant panel's title for the system.</summary>
    public static string Title(PlantSystem s) => s switch
    {
        PlantSystem.SteamWater => "Steam and water cycle",
        PlantSystem.Cooling => "Cooling",
        PlantSystem.FuelFlue => "Fuel and flue gas",
        PlantSystem.Electrical => "Electrical generation",
        _ => "Plant overview",
    };

    /// <summary>
    /// The unit to select when the operator isolates <paramref name="system"/>: the current one if it
    /// belongs to the system, else the system's first unit in plant order, so the inspector and the
    /// trends follow the drawing.
    /// </summary>
    public static UnitId SelectionFor(PlantSystem system, UnitId? current) =>
        current is { } unit && Contains(system, unit)
            ? unit
            : Enum.GetValues<UnitId>().First(u => Contains(system, u));

    /// <summary>
    /// The system to show once <paramref name="unit"/> is selected some other way (a callout, the alert
    /// pill, a connection chip): the current one if it holds the unit, else all systems, so the
    /// selection is never a unit the view has faded out.
    /// </summary>
    public static PlantSystem SystemFor(PlantSystem current, UnitId unit) =>
        Contains(current, unit) ? current : PlantSystem.All;

    /// <summary>The equipment in a system (all of it for <see cref="PlantSystem.All"/>).</summary>
    public static bool Contains(PlantSystem s, UnitId unit) => s switch
    {
        PlantSystem.All => true,
        PlantSystem.SteamWater => unit is UnitId.Trb or UnitId.Cnd,
        PlantSystem.Cooling => unit is UnitId.Clg or UnitId.Cnd,
        PlantSystem.FuelFlue => unit is UnitId.Tka or UnitId.Stk or UnitId.Sil,
        PlantSystem.Electrical => unit is UnitId.Gen or UnitId.Trb,
        _ => false,
    };
}
