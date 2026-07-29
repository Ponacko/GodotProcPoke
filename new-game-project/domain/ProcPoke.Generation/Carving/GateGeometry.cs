using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Which border of an area a gate necks off. The spine no longer always exits east, so this has to be read
/// from the embedding rather than assumed: the gated exit is the border the area shares with the next
/// critical-path area. Shared by <see cref="GateCarver"/>, which orients the barrier, and
/// <see cref="OpeningAligner"/>, which keeps other openings clear of it.
/// </summary>
public static class GateGeometry
{
    public static EdgeSide ToEdge(this Heading heading) => heading switch
    {
        Heading.East => EdgeSide.Right,
        Heading.West => EdgeSide.Left,
        Heading.South => EdgeSide.Bottom,
        _ => EdgeSide.Top,
    };

    /// <summary>
    /// The border <paramref name="area"/> shares with the next critical-path area, or null if it has none —
    /// the League, and every off-spine area.
    /// </summary>
    public static EdgeSide? SpineExitSideOf(RegionGraph graph, Area area)
    {
        if (!area.OnCriticalPath) return null;

        var next = graph.Areas.FirstOrDefault(a => a.OnCriticalPath && a.PathIndex == area.PathIndex + 1);
        return next is null ? null : area.Cell.HeadingTo(next.Cell).ToEdge();
    }

    /// <summary>The side a gate necks off for <paramref name="area"/>, or null if no gate blocks its exit.</summary>
    public static EdgeSide? GatedExitSideOf(RegionGraph graph, GatingPlan gating, Area area)
        => AreaCarver.GateOnExitOf(area, gating) is null ? null : SpineExitSideOf(graph, area);
}
