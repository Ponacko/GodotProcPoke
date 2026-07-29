using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>Shared primitives used by the archetype carvers — dimensions, borders, corridors, rectangles.</summary>
internal static class CarveKit
{
    /// <summary>
    /// The row an area's guaranteed trunk corridor runs along.
    /// <para>
    /// When the spine leaves through a left or right border, the trunk <em>must</em> arrive at that opening's
    /// own row. A gate necks the exit two tiles in, and on a horizontal exit that barrier is a column — the
    /// very column a stub from the exit opening would run down. Wall it and the stub goes with it, leaving the
    /// exit stranded even after the gate is cleared. Aligning the trunk to the exit row means the exit needs
    /// no stub at all. A vertical exit has no such clash: its stub runs perpendicular to the barrier and
    /// passes straight through the gap, so the trunk can sit wherever the horizontal borders want it.
    /// </para>
    /// </summary>
    public static int TrunkRow(IReadOnlyList<AreaOpening> planned, EdgeSide? spineExit, int height)
        => spineExit is EdgeSide.Left or EdgeSide.Right
            ? planned.OffsetOr(spineExit.Value, height / 2)
            : planned.OffsetOr(EdgeSide.Right, planned.OffsetOr(EdgeSide.Left, height / 2));

    /// <summary>
    /// The line a gate barrier would occupy if this area's spine exit is gated — the full span two tiles in
    /// from the exit border. Carvers add it to their protected set so decoration never lands there: the
    /// barrier overwrites the line wholesale, and an item ball or trainer post on it simply disappears.
    /// <para>
    /// This used to need no expressing. The exit was always the right border, so the barrier was always
    /// column <c>w-2</c>, and the placement rules just avoided that column by construction.
    /// </para>
    /// </summary>
    public static IEnumerable<(int X, int Y)> BarrierLine(EdgeSide? spineExit, int width, int height)
    {
        if (spineExit is null) yield break;
        var inset = GateCarver.BarrierInsetFromRightEdge;
        switch (spineExit.Value)
        {
            case EdgeSide.Right:
                for (var y = 1; y <= height - 2; y++) yield return (width - inset, y);
                break;
            case EdgeSide.Left:
                for (var y = 1; y <= height - 2; y++) yield return (inset - 1, y);
                break;
            case EdgeSide.Bottom:
                for (var x = 1; x <= width - 2; x++) yield return (x, height - inset);
                break;
            default:
                for (var x = 1; x <= width - 2; x++) yield return (x, inset - 1);
                break;
        }
    }

    public static (int W, int H) Dimensions(SizeClass size) => size switch
    {
        SizeClass.Small => (24, 12),
        SizeClass.Large => (40, 22),
        _ => (32, 16),
    };

    public static void Border(TileGrid g, LogicalTile tile)
    {
        for (var x = 0; x < g.Width; x++) { g[x, 0] = tile; g[x, g.Height - 1] = tile; }
        for (var y = 0; y < g.Height; y++) { g[0, y] = tile; g[g.Width - 1, y] = tile; }
    }

    /// <summary>Fills an inside-the-border rectangle with a tile (clamped, never touches the frame).</summary>
    public static void FillRect(TileGrid g, int x0, int y0, int x1, int y1, LogicalTile tile)
    {
        for (var y = Math.Max(1, y0); y <= Math.Min(g.Height - 2, y1); y++)
            for (var x = Math.Max(1, x0); x <= Math.Min(g.Width - 2, x1); x++)
                g[x, y] = tile;
    }

    /// <summary>Carves an L-shaped corridor (horizontal then vertical) of the given half-width as floor.</summary>
    public static void CarveCorridor(TileGrid g, int ax, int ay, int bx, int by, LogicalTile floor, int halfWidth = 0)
    {
        foreach (var x in Range(ax, bx))
            for (var w = -halfWidth; w <= halfWidth; w++)
                Set(g, x, ay + w, floor);
        foreach (var y in Range(ay, by))
            for (var w = -halfWidth; w <= halfWidth; w++)
                Set(g, bx + w, y, floor);
    }

    /// <summary>Punches one edge opening (a <see cref="LogicalTile.Warp"/>) through the border at
    /// <paramref name="offset"/> and links it into the spine row via a protected floor column — the shared
    /// spine-first connectivity idiom (ADR-0001). Every carved tile is recorded in <paramref name="guarded"/>
    /// so decoration never overwrites the connection. Returns the opening tile.</summary>
    public static (int X, int Y) OpenSpineEdge(
        TileGrid g, ISet<(int X, int Y)> guarded, EdgeSide edge, int offset, int spineY, LogicalTile floor)
    {
        var (w, h) = (g.Width, g.Height);
        if (edge is EdgeSide.Left or EdgeSide.Right)
        {
            var x = edge == EdgeSide.Left ? 0 : w - 1;
            for (var dy = -1; dy <= 1; dy++)
                if (g.InBounds(x, offset + dy)) g[x, offset + dy] = floor;
            g[x, offset] = LogicalTile.Warp;
            ConnectSpineColumn(g, guarded, edge == EdgeSide.Left ? 1 : w - 2, offset, spineY, floor);
            return (x, offset);
        }

        var y = edge == EdgeSide.Top ? 0 : h - 1;
        g[offset, y] = LogicalTile.Warp;
        ConnectSpineColumn(g, guarded, offset, edge == EdgeSide.Top ? 1 : h - 2, spineY, floor);
        return (offset, y);
    }

    /// <summary>Carves a protected floor column between <paramref name="fromY"/> and the spine row — an
    /// opening's link to the guaranteed corridor.</summary>
    public static void ConnectSpineColumn(
        TileGrid g, ISet<(int X, int Y)> guarded, int x, int fromY, int spineY, LogicalTile floor)
    {
        var (lo, hi) = fromY <= spineY ? (fromY, spineY) : (spineY, fromY);
        for (var y = lo; y <= hi; y++)
            if (x >= 1 && x <= g.Width - 2 && y >= 1 && y <= g.Height - 2)
            {
                g[x, y] = floor;
                guarded.Add((x, y));
            }
    }

    private static void Set(TileGrid g, int x, int y, LogicalTile tile)
    {
        if (x >= 1 && x <= g.Width - 2 && y >= 1 && y <= g.Height - 2) g[x, y] = tile;
    }

    private static IEnumerable<int> Range(int a, int b)
    {
        if (a <= b) for (var i = a; i <= b; i++) yield return i;
        else for (var i = a; i >= b; i--) yield return i;
    }
}
