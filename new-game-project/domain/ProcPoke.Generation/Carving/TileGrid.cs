namespace ProcPoke.Generation.Carving;

/// <summary>A rectangular grid of Logical Tiles — the tile-level output of a carver for one area.</summary>
public sealed class TileGrid
{
    private readonly LogicalTile[] _tiles;

    public int Width { get; }
    public int Height { get; }

    public TileGrid(int width, int height, LogicalTile fill = LogicalTile.Wall)
    {
        Width = width;
        Height = height;
        _tiles = new LogicalTile[width * height];
        if (fill != default) Array.Fill(_tiles, fill); // default(LogicalTile) is Ground, so array is already ground
    }

    public LogicalTile this[int x, int y]
    {
        get => _tiles[y * Width + x];
        set => _tiles[y * Width + x] = value;
    }

    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    public int Count(LogicalTile tile)
    {
        var n = 0;
        foreach (var t in _tiles) if (t == tile) n++;
        return n;
    }
}

/// <summary>A carved area: its id and tile grid, plus the edge openings where it connects to neighbours.</summary>
public sealed record CarvedArea
{
    public required int AreaId { get; init; }
    public required TileGrid Grid { get; init; }

    /// <summary>Warp/opening coordinates on the grid edges (entry, exit, …), for edge-aligned Map Connections.</summary>
    public required IReadOnlyList<(int X, int Y)> Openings { get; init; }

    /// <summary>Town building-wall coordinates that gate approach straightening must not punch through.</summary>
    public IReadOnlySet<(int X, int Y)> BuildingWallTiles { get; init; } = new HashSet<(int X, int Y)>();

    /// <summary>
    /// Coordinates of the gate obstacle tiles carved into this area (empty when the area holds no gate).
    /// The chokepoint guarantee (ADR-0001) is expressed against these: with them treated as walls the exit
    /// is unreachable; with them cleared it is reachable.
    /// </summary>
    public IReadOnlyList<(int X, int Y)> GateTiles { get; init; } = [];

    /// <summary>
    /// The row of the guaranteed walkable trunk corridor, for the archetypes that lay one (routes, forests,
    /// transit caves); -1 for the rest. Recorded rather than re-derived: callers used to recover it by
    /// looking up the right-edge opening's row, which stopped being the trunk the moment the spine could
    /// leave on any border.
    /// </summary>
    public int TrunkRow { get; init; } = -1;

    /// <summary>
    /// The chambers this area was built from, for the archetypes that lay rooms (caves, hideouts); empty for
    /// the rest. Recorded because a room is a fact about the construction, not something to be recovered from
    /// the finished tiles: a sliding-window scan for open blocks reports two chambers joined by a corridor as
    /// one room whenever the corridor happens to fall inside both windows.
    /// </summary>
    public IReadOnlyList<TileRect> Rooms { get; init; } = [];
}

/// <summary>An inclusive rectangle of tiles.</summary>
public readonly record struct TileRect(int X0, int Y0, int X1, int Y1)
{
    public int Width => X1 - X0 + 1;
    public int Height => Y1 - Y0 + 1;
    public bool Contains(int x, int y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;

    /// <summary>True if the two rectangles overlap or sit directly against each other.</summary>
    public bool Touches(TileRect other)
        => X0 <= other.X1 + 1 && X1 + 1 >= other.X0 && Y0 <= other.Y1 + 1 && Y1 + 1 >= other.Y0;
}
