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
}
