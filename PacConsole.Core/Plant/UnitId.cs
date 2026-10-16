namespace PacConsole.Core.Plant;

/// <summary>
/// The plant's selectable equipment, in display order. The condenser came last (2026-10-15, console
/// v3) so every earlier unit keeps its index and code.
/// </summary>
public enum UnitId
{
    Stk,
    Tka,
    Gen,
    Trb,
    Clg,
    Sil,
    Cnd,
}

public enum ControlMode
{
    Auto,
    Manual,
    Hold,
}

/// <summary>Static facts about a unit: display names plus the base readings the simulation oscillates around.</summary>
public sealed record UnitInfo(
    UnitId Id,
    string Code,
    string Name,
    string Uptime,
    string LastService,
    double BaseTemp,
    double BasePress,
    double BaseFlow,
    double BaseLoad,
    double BaseEff,
    double BaseVib)
{
    /// <summary>Short code shown on control tiles, e.g. "TRB".</summary>
    public string ShortCode => Code[..3];

    /// <summary>Two-digit index shown under the short code, e.g. "04".</summary>
    public string Index => Code[4..];
}

public static class UnitCatalog
{
    public static readonly ImmutableArray<UnitInfo> All =
    [
        new(UnitId.Stk, "STK-01", "Exhaust stacks", "412 d", "2026-06-02", 412, 1.02, 310, 71, 93.4, 1.8),
        new(UnitId.Tka, "TKA-02", "Fuel tank farm", "1,208 d", "2026-03-19", 38, 6.4, 120, 64, 97.1, 0.6),
        new(UnitId.Gen, "GEN-03", "Generator", "287 d", "2026-07-21", 88, 3.2, 540, 82, 98.2, 2.4),
        new(UnitId.Trb, "TRB-04", "Steam turbine", "287 d", "2026-08-14", 538, 165, 540, 86, 91.7, 3.1),
        new(UnitId.Clg, "CLG-05", "Cooling bank", "96 d", "2026-05-30", 33, 2.1, 520, 77, 88.5, 4.6),
        new(UnitId.Sil, "SIL-06", "Ash silo", "640 d", "2026-04-11", 24, 1.0, 42, 58, 99.0, 0.9),
        new(UnitId.Cnd, "CND-07", "Condenser", "287 d", "2026-08-14", 33, 0.09, 520, 78, 96.0, 1.2),
    ];

    public static UnitInfo Get(UnitId id) => All[(int)id];
}
