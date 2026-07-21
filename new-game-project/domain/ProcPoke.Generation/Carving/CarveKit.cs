using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>Shared primitives used by the archetype carvers — dimensions, borders, corridors, rectangles.</summary>
internal static class CarveKit
{
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
