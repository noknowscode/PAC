using PacConsole.Core.Plant;

namespace PacConsole.Core.Solids;

/// <summary>Which process system each part of the plant belongs to, so the plant view can fade the rest.</summary>
public static class PartSystems
{
    private static readonly string[] SteamWaterParts = ["boiler.", "fw."];
    private static readonly string[] FuelFlueParts = ["boiler.", "flue.", "ash"];
    private static readonly string[] ElectricalParts = ["xfmr.", "cab."];

    /// <summary>
    /// Whether a part belongs to a process system. A unit's parts follow <see cref="PlantSystems.Contains"/>; pipes
    /// follow their circuit; the boiler serves both the steam cycle and the fuel and flue-gas path; the transformer and
    /// cabinets are electrical. The stand is the ground, so it is in every system.
    /// </summary>
    public static bool InSystem(SolidPart part, PlantSystem system, IReadOnlyCollection<UnitId>? alerting = null)
    {
        if (system == PlantSystem.All || part.Name.StartsWith("stand.", StringComparison.Ordinal)) return true;
        if (part.Unit is { } unit) return PlantSystems.Contains(system, unit, alerting);
        return system switch
        {
            PlantSystem.SteamWater => part.Circuit is Circuit.Steam or Circuit.Water || Named(part, SteamWaterParts),
            PlantSystem.Cooling => part.Circuit == Circuit.Cooling,
            PlantSystem.FuelFlue => part.Circuit == Circuit.Fuel || Named(part, FuelFlueParts),
            PlantSystem.Electrical => Named(part, ElectricalParts),
            _ => false,
        };
    }

    private static bool Named(SolidPart part, string[] prefixes)
    {
        foreach (var p in prefixes)
        {
            if (part.Name.StartsWith(p, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
