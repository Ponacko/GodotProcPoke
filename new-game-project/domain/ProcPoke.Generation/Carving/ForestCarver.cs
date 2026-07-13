using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a forest transit area: a walkable field framed by trees, threaded with vertical tree barriers
/// that each leave gaps — a light maze that makes the player weave — while the three spine rows always
/// stay open, so left↔right traversal is guaranteed. Tall grass clusters for wild encounters.
/// </summary>
public static class ForestCarver
{
    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Ground);
        var midY = h / 2;

        CarveKit.Border(grid, LogicalTile.Tree);
        for (var dy = -1; dy <= 1; dy++) { grid[0, midY + dy] = LogicalTile.Ground; grid[w - 1, midY + dy] = LogicalTile.Ground; }
        grid[0, midY] = LogicalTile.Warp;
        grid[w - 1, midY] = LogicalTile.Warp;

        // Vertical tree barriers with gaps; the spine rows always stay open.
        var barriers = w / 6;
        for (var i = 1; i <= barriers; i++)
        {
            var bx = i * w / (barriers + 1);
            var gapY = rng.NextInt(2, h - 3);
            for (var y = 1; y < h - 1; y++)
            {
                var inSpine = y >= midY - 1 && y <= midY + 1;
                var inGap = y >= gapY && y <= gapY + 1;
                if (!inSpine && !inGap) grid[bx, y] = LogicalTile.Tree;
            }
        }

        // Tall grass clusters straddling the path.
        for (var i = 0; i < 4; i++)
        {
            var cx = rng.NextInt(3, w - 3);
            var cy = midY + rng.NextInt(-2, 3);
            CarveKit.FillRect(grid, cx - 1, cy - 1, cx + 1, cy + 1, LogicalTile.TallGrass);
        }

        // An item off the beaten path.
        var ix = rng.NextInt(2, w - 2);
        var iy = rng.Chance(0.5) ? 2 : h - 3;
        if (grid[ix, iy] == LogicalTile.Ground) grid[ix, iy] = LogicalTile.ItemBall;

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = [(0, midY), (w - 1, midY)] };
    }
}
