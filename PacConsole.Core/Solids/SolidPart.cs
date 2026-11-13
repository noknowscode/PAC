using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;

namespace PacConsole.Core.Solids;

/// <summary>Line tones of the reference figure, brightest first.</summary>
public enum EdgeTone
{
    Hi,
    Mid,
    Lo,
    Faint,
}

/// <summary>The fluid a pipe carries. Pipes are tinted by circuit so a run reads end to end.</summary>
public enum Circuit
{
    None,
    Steam,
    Water,
    Cooling,
    Fuel,
}

/// <summary>
/// One solid of the plant: its hull for ordering and a builder that draws it for a camera.
/// <see cref="Lit"/> parts take the selection colours when their unit is selected.
/// </summary>
public sealed record SolidPart(string Name, UnitId? Unit, Volume Volume, Func<OrbitProjection, SolidShape> Build, EdgeTone Edge = EdgeTone.Mid, bool Lit = false, bool Crease = true)
{
    /// <summary>Line work drawn over the part (seams, bolts, fan blades), given the camera and the time in seconds.</summary>
    public Func<OrbitProjection, double, IEnumerable<DetailLine>>? Details { get; init; }

    /// <summary>True when <see cref="Details"/> change with time (fan blades); the rest can be cached per camera.</summary>
    public bool AnimatedDetails { get; init; }

    /// <summary>Set on a pad: the unit standing on it, whose lift shadow is drawn on this pad.</summary>
    public UnitId? PadOf { get; init; }

    /// <summary>The fluid a pipe part carries; the renderer tints the part by it.</summary>
    public Circuit Circuit { get; init; }
}
