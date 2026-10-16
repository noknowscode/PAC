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
