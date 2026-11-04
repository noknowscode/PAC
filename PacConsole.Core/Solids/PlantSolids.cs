using System.Collections.Concurrent;
using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;

namespace PacConsole.Core.Solids;

/// <summary>
/// The plant as solids, from the reference figure's <c>docs/anatomy/plant/build.mjs</c>: every part in drawing order, the
/// pad under each unit and where each unit's tag hangs. <see cref="UnitSolids"/> and <see cref="CycleSolids"/> build the
/// parts; <see cref="PlantFraming"/>, <see cref="UnitPicker"/> and <see cref="PartSystems"/> read them.
/// </summary>
public sealed class PlantSolids
{
    public const float PadHeight = 1.2f;
    private const float PadHalf = 36;
    private const float CondenserPadHalf = 24;

    /// <summary>Half the side of a unit's pad: the condenser stands on a smaller one in the boiler bay.</summary>
    public static float PadHalfOf(UnitId id) => id == UnitId.Cnd ? CondenserPadHalf : PadHalf;

    private static readonly Lazy<PlantSolids> _default = new(() => new PlantSolids());

    /// <summary>The one plant this app draws, built on first use.</summary>
    public static PlantSolids Default => _default.Value;

    /// <summary>Unit centres on the plan, from the figure's UNITS table.</summary>
    public static readonly ImmutableDictionary<UnitId, PlanPoint> Centres = new Dictionary<UnitId, PlanPoint>
    {
        [UnitId.Stk] = new(-95, -48),
        [UnitId.Tka] = new(0, -48),
        [UnitId.Gen] = new(95, -48),
        [UnitId.Trb] = new(0, 48),
        [UnitId.Clg] = new(95, 48),
        [UnitId.Sil] = new(-95, 48),
        // The water-cooled condenser (console v3) sits in the boiler bay's free southern half, fed
        // by an exhaust run from the turbine along the south margin.
        [UnitId.Cnd] = new(-197, 64),
    }.ToImmutableDictionary();

    // One array per unit, so a renderer's shape cache (keyed on the parts list) holds across frames.
    private readonly ConcurrentDictionary<UnitId, ImmutableArray<SolidPart>> _unitWithPad = new();

    private PlantSolids()
    {
        var kit = new PartKit();
        new UnitSolids(kit).Build();
        new CycleSolids(kit).Build();
        All = kit.Parts.ToImmutable();
        Pads = kit.Pads.ToImmutableDictionary();

        // Each tag hangs from the top of its unit's tallest part, so the leader lands on the unit.
        Anchors = Centres.Keys.ToImmutableDictionary(id => id, id =>
        {
            var top = All.Where(p => p.Unit == id).MaxBy(p => p.Volume.Y1)!.Volume;
            var c = top.Centre;
            return new Vec3(c.X, top.Y1, c.Z);
        });
    }

    /// <summary>Every solid: stand, pads, the units, the pipework and the rest of the thermal cycle.</summary>
    public ImmutableArray<SolidPart> All { get; }

    /// <summary>The pad under each unit. Pads belong to the stand: they stay down when a unit lifts.</summary>
    public ImmutableDictionary<UnitId, SolidPart> Pads { get; }

    /// <summary>Where each unit's tag attaches: the top centre of its tallest part.</summary>
    public ImmutableDictionary<UnitId, Vec3> Anchors { get; }

    /// <summary>One unit's solids, standing on its pad.</summary>
    public ImmutableArray<SolidPart> UnitWithPad(UnitId id) => _unitWithPad.GetOrAdd(id, u => [Pads[u], .. All.Where(p => p.Unit == u)]);
}
