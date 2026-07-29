using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Decides, for every Seamless connection, the shared edge/offset/width both endpoints carve — run before
/// any area is carved so neighbours never disagree on where the doorway is (ADR-0001 §4.2). Warp
/// connections need no alignment and are skipped entirely.
/// <para>
/// Which edges a connection uses is read from the embedding: the topology pass only joins areas on
/// bordering cells, so the shared border *is* the pair of edges facing each other. This used to be inferred
/// from path indices instead — consecutive spine areas Right↔Left, everything else Bottom↔Top — which was
/// only correct while the spine ran in a straight line eastward.
/// </para>
/// </summary>
public static class OpeningAligner
{
    public static OpeningPlan Plan(RegionGraph graph)
    {
        var seamlessByArea = graph.Areas.ToDictionary(a => a.Id, _ => new List<AreaOpening>());
        var allByArea = graph.Areas.ToDictionary(a => a.Id, _ => new List<AreaOpening>());

        // Every connection gets a border, Warps included. A carver has to know which of its four borders
        // face a neighbour at all: it may only open those, or it punches a walkable tile onto a border with
        // nothing behind it. Warps are placed on the same aligned offset as Seamless ones, which costs
        // nothing and puts a cave mouth opposite the doorway it leads to.
        var ordered = graph.Connections
            .OrderBy(c => Math.Min(c.AreaA, c.AreaB))
            .ThenBy(c => Math.Max(c.AreaA, c.AreaB));

        foreach (var c in ordered)
        {
            var a = graph[c.AreaA];
            var b = graph[c.AreaB];
            var (edgeA, edgeB) = ChooseEdges(a, b);
            var vertical = edgeA is EdgeSide.Left or EdgeSide.Right;

            var (loA, hiA) = ValidRange(a, vertical);
            var (loB, hiB) = ValidRange(b, vertical);
            var lo = Math.Max(loA, loB);
            var hi = Math.Min(hiA, hiB);

            // A left/right offset becomes the row an area's trunk corridor runs along, and the decoration
            // rules need a flank above and below it — so those stay near the middle, as they always were.
            // Top/bottom offsets are columns with no such duty, and spreading them is what stops an area's
            // two vertical openings landing on one column and carving a dead-straight shaft through it.
            var offset = vertical
                ? (lo + hi) / 2
                : ChooseOffset(a, b, edgeA, edgeB, lo, hi, allByArea);

            var openingA = new AreaOpening { NeighborAreaId = b.Id, Edge = edgeA, Offset = offset };
            var openingB = new AreaOpening { NeighborAreaId = a.Id, Edge = edgeB, Offset = offset };

            allByArea[a.Id].Add(openingA);
            allByArea[b.Id].Add(openingB);
            if (c.Kind != ConnectionKind.Seamless) continue;
            seamlessByArea[a.Id].Add(openingA);
            seamlessByArea[b.Id].Add(openingB);
        }

        return new OpeningPlan(seamlessByArea, allByArea);
    }

    /// <summary>The two facing edges of the border the areas share, taken from their lattice cells.</summary>
    private static (EdgeSide EdgeA, EdgeSide EdgeB) ChooseEdges(Area a, Area b)
    {
        var heading = a.Cell.HeadingTo(b.Cell);
        return (heading.ToEdge(), heading.Opposite().ToEdge());
    }

    /// <summary>
    /// The interior tile range an opening's offset may land in. Both ends of the axis reserve the line a
    /// gate barrier would occupy, so a branch opening never lands behind one (ADR-0002; see
    /// <see cref="GateCarver.BarrierInsetFromRightEdge"/>). Reserving both ends rather than just the one the
    /// area's own gate uses keeps this independent of the gating pass, and costs nothing: the tightest grid
    /// is 24×12, so the range stays wide.
    /// </summary>
    private static (int Lo, int Hi) ValidRange(Area area, bool vertical)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        // A left/right offset becomes the row a route's trunk corridor runs along, so keep it off the top and
        // bottom flanks — decoration needs a band either side of the corridor to sit in.
        var inset = vertical ? TrunkRowInset : GateCarver.BarrierInsetFromRightEdge;
        var length = vertical ? h : w;
        return (inset, length - inset - 1);
    }

    /// <summary>Rows reserved above and below a left/right opening. Two clears the gate barrier line; three
    /// also leaves a flank a tree clump or a ledge can occupy.</summary>
    private const int TrunkRowInset = 3;

    /// <summary>
    /// Picks the offset both endpoints will carve. Derived from the pair of area ids, so the two sides agree
    /// without coordinating, and spread across the legal range rather than parked on its midpoint.
    /// <para>
    /// The midpoint is what the old <c>Spread</c> returned whenever an edge held a single opening — which is
    /// now always, since an area has at most one neighbour per border. That put an area's two spine openings
    /// on the same offset, and two openings facing each other on the same offset carve a dead-straight
    /// corridor from one side of the area to the other. So a candidate that collides with an opening already
    /// placed on the opposite border of either endpoint is stepped along until it does not.
    /// </para>
    /// </summary>
    private static int ChooseOffset(
        Area a, Area b, EdgeSide edgeA, EdgeSide edgeB, int lo, int hi,
        Dictionary<int, List<AreaOpening>> placed)
    {
        if (hi <= lo) return lo;
        var span = hi - lo + 1;

        bool Collides(int offset)
            => placed[a.Id].Any(o => o.Edge == edgeA.Opposite() && o.Offset == offset)
            || placed[b.Id].Any(o => o.Edge == edgeB.Opposite() && o.Offset == offset);

        var start = lo + (int)(Mix(Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id)) % (ulong)span);
        for (var i = 0; i < span; i++)
        {
            var candidate = lo + (start - lo + i) % span;
            if (!Collides(candidate)) return candidate;
        }
        return start; // every offset on this axis is taken; the corridor shape is the lesser problem
    }

    /// <summary>Order-independent mix of the two area ids — the offset must not depend on which side asks.</summary>
    private static ulong Mix(int low, int high)
    {
        var mixed = ((ulong)low * 0x9E3779B97F4A7C15UL) ^ ((ulong)high * 0xBF58476D1CE4E5B9UL);
        mixed ^= mixed >> 31;
        mixed *= 0x94D049BB133111EBUL;
        return mixed ^ (mixed >> 29);
    }
}

/// <summary>Per-area lookup of the openings an area's connections require.</summary>
public sealed class OpeningPlan
{
    private readonly Dictionary<int, List<AreaOpening>> _seamless;
    private readonly Dictionary<int, List<AreaOpening>> _all;

    internal OpeningPlan(
        Dictionary<int, List<AreaOpening>> seamless, Dictionary<int, List<AreaOpening>> all)
    {
        _seamless = seamless;
        _all = all;
    }

    /// <summary>The openings this area's Seamless connections require — the borders walked straight across,
    /// where both sides must agree on the offset.</summary>
    public IReadOnlyList<AreaOpening> OpeningsOf(int areaId)
        => _seamless.TryGetValue(areaId, out var list) ? list : [];

    /// <summary>
    /// Every border of this area that faces a neighbour, Warp connections included — exactly the set a
    /// carver may open. Opening anything else leaves a hole in the map, since the spine can leave an area on
    /// any of its four borders.
    /// </summary>
    public IReadOnlyList<AreaOpening> EdgesOf(int areaId)
        => _all.TryGetValue(areaId, out var list) ? list : [];
}
