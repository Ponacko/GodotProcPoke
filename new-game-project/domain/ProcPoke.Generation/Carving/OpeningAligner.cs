using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Decides, for every Seamless connection, the shared edge/offset/width both endpoints carve — run before
/// any area is carved so neighbours never disagree on where the doorway is (ADR-0001 §4.2). Spine-adjacent
/// areas (consecutive critical-path indices) pair on Left/Right, matching the horizontal chain a carver
/// lays out; every other Seamless connection (a dead-end branch off its anchor, a loop-back between
/// non-adjacent spine areas) pairs on Bottom/Top instead. Warp connections need no alignment and are
/// skipped entirely.
/// </summary>
public static class OpeningAligner
{
    public static OpeningPlan Plan(RegionGraph graph)
    {
        var byArea = graph.Areas.ToDictionary(a => a.Id, _ => new List<AreaOpening>());

        var seamless = graph.Connections
            .Where(c => c.Kind == ConnectionKind.Seamless)
            .OrderBy(c => Math.Min(c.AreaA, c.AreaB))
            .ThenBy(c => Math.Max(c.AreaA, c.AreaB));

        foreach (var c in seamless)
        {
            var a = graph[c.AreaA];
            var b = graph[c.AreaB];
            var (edgeA, edgeB) = ChooseEdges(a, b);
            var vertical = edgeA is EdgeSide.Left or EdgeSide.Right;

            var (loA, hiA) = ValidRange(a, vertical);
            var (loB, hiB) = ValidRange(b, vertical);
            var lo = Math.Max(loA, loB);
            var hi = Math.Min(hiA, hiB);

            var slot = Math.Max(
                byArea[a.Id].Count(o => o.Edge == edgeA),
                byArea[b.Id].Count(o => o.Edge == edgeB));
            var offset = Spread(lo, hi, slot);

            byArea[a.Id].Add(new AreaOpening { NeighborAreaId = b.Id, Edge = edgeA, Offset = offset });
            byArea[b.Id].Add(new AreaOpening { NeighborAreaId = a.Id, Edge = edgeB, Offset = offset });
        }

        return new OpeningPlan(byArea);
    }

    /// <summary>Consecutive spine areas pair Right↔Left (the horizontal chain); anything else pairs
    /// Bottom↔Top, the lower-id area (always the earlier-built anchor) taking Bottom.</summary>
    private static (EdgeSide EdgeA, EdgeSide EdgeB) ChooseEdges(Area a, Area b)
    {
        if (a.OnCriticalPath && b.OnCriticalPath && Math.Abs(a.PathIndex - b.PathIndex) == 1)
            return a.PathIndex < b.PathIndex ? (EdgeSide.Right, EdgeSide.Left) : (EdgeSide.Left, EdgeSide.Right);

        return a.Id < b.Id ? (EdgeSide.Bottom, EdgeSide.Top) : (EdgeSide.Top, EdgeSide.Bottom);
    }

    /// <summary>The interior tile range an opening's offset may land in. Horizontal edges (Top/Bottom)
    /// reserve the column a right-edge gate would wall off, so a branch opening never lands under a gate's
    /// barrier (ADR-0002; see <see cref="GateCarver.BarrierInsetFromRightEdge"/>).</summary>
    private static (int Lo, int Hi) ValidRange(Area area, bool vertical)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        return vertical ? (1, h - 2) : (1, w - GateCarver.BarrierInsetFromRightEdge - 1);
    }

    /// <summary>Deterministically spreads sibling openings on the same edge apart from a shared midpoint,
    /// so two branches hanging off the same anchor land on distinct tiles.</summary>
    private static int Spread(int lo, int hi, int slot)
    {
        if (hi <= lo) return lo;
        var step = Math.Max(1, (hi - lo) / 6);
        var sign = slot % 2 == 0 ? 1 : -1;
        var magnitude = (slot + 1) / 2 * step;
        return Math.Clamp((lo + hi) / 2 + sign * magnitude, lo, hi);
    }
}

/// <summary>Per-area lookup of the openings its Seamless connections require.</summary>
public sealed class OpeningPlan
{
    private readonly Dictionary<int, List<AreaOpening>> _byArea;

    internal OpeningPlan(Dictionary<int, List<AreaOpening>> byArea) => _byArea = byArea;

    public IReadOnlyList<AreaOpening> OpeningsOf(int areaId)
        => _byArea.TryGetValue(areaId, out var list) ? list : [];
}
