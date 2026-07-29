namespace ProcPoke.Generation.Carving;

/// <summary>
/// A view of an area's tile grid rotated so that one chosen border — the one the spine arrives at or leaves
/// by — is always in the same place. Carvers author their layout once, in this frame, and write tiles back
/// through <see cref="Map"/>.
/// <para>
/// Canonical coordinates are <c>(u, v)</c>: <c>u</c> runs <em>toward</em> the reference border, which sits at
/// <c>u == U - 1</c>, so <c>u == 0</c> is the far side; <c>v</c> runs across it. A layout written as "start at
/// the reference border and work away from it" therefore holds whichever of the four borders is the
/// reference — mirrored for the opposite one, transposed for the perpendicular pair.
/// </para>
/// <para>
/// Every carver used to assume the spine ran west to east: routes laid a horizontal trunk between their left
/// and right borders, towers stacked floors upward from a bottom entrance, hideouts put the boss in the
/// top-right room. Once the spine wanders, those assumptions produce entrances that punch through the very
/// walls that give the layout its shape. This type is how a carver stops caring which way it is entered.
/// </para>
/// </summary>
public readonly struct CarveFrame
{
    private readonly EdgeSide _reference;
    private readonly int _width;
    private readonly int _height;

    /// <summary>Extent along the axis running toward the reference border; the border is at <c>u == U - 1</c>.</summary>
    public int U { get; }

    /// <summary>Extent across the reference border.</summary>
    public int V { get; }

    public CarveFrame(EdgeSide reference, int width, int height)
    {
        _reference = reference;
        _width = width;
        _height = height;
        var horizontal = reference is EdgeSide.Left or EdgeSide.Right;
        U = horizontal ? width : height;
        V = horizontal ? height : width;
    }

    /// <summary>Canonical → grid.</summary>
    public (int X, int Y) Map(int u, int v) => _reference switch
    {
        EdgeSide.Right => (u, v),
        EdgeSide.Left => (_width - 1 - u, v),
        EdgeSide.Bottom => (v, u),
        _ => (v, _height - 1 - u),
    };

    /// <summary>Grid → canonical.</summary>
    public (int U, int V) Unmap(int x, int y) => _reference switch
    {
        EdgeSide.Right => (x, y),
        EdgeSide.Left => (_width - 1 - x, y),
        EdgeSide.Bottom => (y, x),
        _ => (_height - 1 - y, x),
    };

    public bool InBounds(int u, int v) => u >= 0 && u < U && v >= 0 && v < V;

    /// <summary>
    /// An opening's offset along the reference border, as a canonical <c>v</c>. When the opening sits on the
    /// reference border itself its offset already <em>is</em> <c>v</c>; this clamps it into the interior so
    /// callers can use it as a corridor coordinate.
    /// </summary>
    public int InteriorV(int offset) => Math.Clamp(offset, 1, V - 2);

    public LogicalTile Read(TileGrid grid, int u, int v)
    {
        var (x, y) = Map(u, v);
        return grid[x, y];
    }

    public void Write(TileGrid grid, int u, int v, LogicalTile tile)
    {
        var (x, y) = Map(u, v);
        if (grid.InBounds(x, y)) grid[x, y] = tile;
    }

    /// <summary>Fills the inclusive canonical rectangle.</summary>
    public void Fill(TileGrid grid, int u0, int v0, int u1, int v1, LogicalTile tile)
    {
        for (var u = Math.Min(u0, u1); u <= Math.Max(u0, u1); u++)
            for (var v = Math.Min(v0, v1); v <= Math.Max(v0, v1); v++)
                Write(grid, u, v, tile);
    }
}
