using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a Route as a horizontal area, spine-first (ADR-0001): a guaranteed walkable corridor from the
/// left opening to the right opening is laid first and never violated, then decoration is applied
/// outward under placement rules that read hand-crafted — tall grass straddles the path, tree clumps and
/// a ledge break up the flanks, a trainer watches the path, and an item hides in a pocket. Biome tints
/// the fill (forest = denser trees/grass, desert = sand, mountain = rock walls).
/// </summary>
public static class RouteCarver
{
    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng)
    {
        var (w, h) = Dimensions(area.Size);
        var border = biome is Biome.Mountain or Biome.Cave ? LogicalTile.Wall : LogicalTile.Tree;
        var floor = biome == Biome.Desert ? LogicalTile.Sand : LogicalTile.Ground;

        var grid = new TileGrid(w, h, floor);
        var midY = h / 2;

        // Border ring.
        for (var x = 0; x < w; x++) { grid[x, 0] = border; grid[x, h - 1] = border; }
        for (var y = 0; y < h; y++) { grid[0, y] = border; grid[w - 1, y] = border; }

        // Openings (3 tall) on the left and right edges, warp at each centre. The three corridor rows
        // midY-1..midY+1 stay walkable for the whole width — the spine, guaranteed by construction.
        for (var dy = -1; dy <= 1; dy++) { grid[0, midY + dy] = floor; grid[w - 1, midY + dy] = floor; }
        grid[0, midY] = LogicalTile.Warp;
        grid[w - 1, midY] = LogicalTile.Warp;

        var grassPatches = biome == Biome.Forest ? 4 : 3;
        var treeClumps = biome == Biome.Forest ? 5 : biome == Biome.Desert ? 1 : 3;

        // Tall grass straddling the path.
        for (var i = 0; i < grassPatches; i++)
        {
            var cx = rng.NextInt(4, w - 4);
            var cy = midY + rng.NextInt(-2, 3);
            Stamp(grid, cx - 2, cy - 1, cx + 2, cy + 1, LogicalTile.TallGrass, midY, allowCorridor: true);
        }

        // Tree/rock clumps on the flanks only (never on the corridor rows).
        var clumpTile = biome is Biome.Mountain or Biome.Cave ? LogicalTile.Wall : LogicalTile.Tree;
        for (var i = 0; i < treeClumps; i++)
        {
            var upper = rng.Chance(0.5);
            var cy = upper ? rng.NextInt(2, midY - 1) : rng.NextInt(midY + 2, h - 2);
            var cx = rng.NextInt(3, w - 3);
            Stamp(grid, cx - 1, cy - 1, cx + 1, cy, clumpTile, midY, allowCorridor: false);
        }

        // One ledge run in the lower flank (a shortcut you drop down toward the entrance).
        var ledgeY = Math.Min(h - 2, midY + 2);
        var ledgeX0 = rng.NextInt(3, w / 2);
        for (var x = ledgeX0; x < ledgeX0 + rng.NextInt(4, 8) && x < w - 1; x++)
            if (grid[x, ledgeY].IsWalkable()) grid[x, ledgeY] = LogicalTile.Ledge;

        // A trainer watching the path.
        PlaceOnFloor(grid, rng.NextInt(6, w - 6), midY + (rng.Chance(0.5) ? -1 : 1), LogicalTile.TrainerPost, floor);

        // An item tucked into a flank pocket.
        PlaceOnFloor(grid, rng.NextInt(3, w - 3), rng.Chance(0.5) ? 2 : h - 3, LogicalTile.ItemBall, floor);

        return new CarvedArea
        {
            AreaId = area.Id,
            Grid = grid,
            Openings = [(0, midY), (w - 1, midY)],
        };
    }

    private static (int W, int H) Dimensions(SizeClass size) => size switch
    {
        SizeClass.Small => (24, 12),
        SizeClass.Large => (40, 20),
        _ => (32, 16),
    };

    /// <summary>Stamps a rectangle of a tile, staying inside the border and (optionally) off the spine corridor.</summary>
    private static void Stamp(TileGrid g, int x0, int y0, int x1, int y1, LogicalTile tile, int midY, bool allowCorridor)
    {
        for (var y = Math.Max(1, y0); y <= Math.Min(g.Height - 2, y1); y++)
        {
            if (!allowCorridor && y >= midY - 1 && y <= midY + 1) continue;
            for (var x = Math.Max(1, x0); x <= Math.Min(g.Width - 2, x1); x++)
                g[x, y] = tile;
        }
    }

    private static void PlaceOnFloor(TileGrid g, int x, int y, LogicalTile tile, LogicalTile floor)
    {
        if (g.InBounds(x, y) && g[x, y] == floor) g[x, y] = tile;
    }
}
