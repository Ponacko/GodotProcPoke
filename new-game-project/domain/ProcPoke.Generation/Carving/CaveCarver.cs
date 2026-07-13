using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves an enclosed rocky area (caves, mountain paths, building interiors, Victory Road). Walls fill
/// the frame; guaranteed floor corridors are carved between the openings via a shared midpoint (so the
/// area is always traversable), with a branch chamber holding an item. Transit areas get two openings on
/// opposite edges; destination dungeons get a single entrance.
/// </summary>
public static class CaveCarver
{
    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, bool transit)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Wall);
        var midY = h / 2;

        List<(int X, int Y)> openings;
        if (transit)
        {
            openings = [(0, midY), (w - 1, midY)];

            // Two corridors meeting at a shared midpoint guarantee a left↔right route.
            var mx = w / 2;
            var my = rng.NextInt(2, h - 2);
            CarveKit.CarveCorridor(grid, 1, midY, mx, my, LogicalTile.Ground, halfWidth: 1);
            CarveKit.CarveCorridor(grid, mx, my, w - 2, midY, LogicalTile.Ground, halfWidth: 1);

            // A side chamber off the midpoint with an item.
            var chamber = CarveChamber(grid, rng, mx, my);
            grid[chamber.X, chamber.Y] = LogicalTile.ItemBall;
        }
        else
        {
            var ex = w / 2;
            openings = [(ex, h - 1)];

            // Wind up from the entrance to a reward chamber.
            var cx = rng.NextInt(3, w - 3);
            var cy = rng.NextInt(2, midY);
            CarveKit.CarveCorridor(grid, ex, h - 2, cx, cy, LogicalTile.Ground, halfWidth: 1);
            var chamber = CarveChamber(grid, rng, cx, cy);
            grid[chamber.X, chamber.Y] = LogicalTile.ItemBall;
        }

        // Punch the openings through the frame and connect them inward.
        foreach (var (ox, oy) in openings)
        {
            grid[ox, oy] = LogicalTile.Warp;
            var (ix, iy) = (Math.Clamp(ox, 1, w - 2), Math.Clamp(oy, 1, h - 2));
            grid[ix, iy] = LogicalTile.Ground;
        }

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = openings };
    }

    private static (int X, int Y) CarveChamber(TileGrid g, Pcg32 rng, int nearX, int nearY)
    {
        var cx = Math.Clamp(nearX + rng.NextInt(-4, 5), 2, g.Width - 3);
        var cy = Math.Clamp(nearY + rng.NextInt(-3, 4), 2, g.Height - 3);
        CarveKit.CarveCorridor(g, nearX, nearY, cx, cy, LogicalTile.Ground, halfWidth: 1);
        CarveKit.FillRect(g, cx - 1, cy - 1, cx + 1, cy + 1, LogicalTile.Ground);
        return (cx, cy);
    }
}
