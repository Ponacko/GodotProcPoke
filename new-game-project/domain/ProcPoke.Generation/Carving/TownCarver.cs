using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a settlement: an open plaza with a clear central street (which also carries the spine between
/// the left/right openings), buildings arranged in a band above and below the street, each with a door
/// (Warp) facing it. The open street guarantees every door and every edge opening are mutually reachable.
/// Left/right openings line up with the spine neighbours they were edge-aligned against; any branch or
/// loop-back connection gets its own opening on the top or bottom edge.
/// </summary>
public static class TownCarver
{
    // Building footprints (width); heights are a fixed 3. Gym and Center are the wide ones.
    private static readonly int[] TopBand = [4, 4, 3];   // gym, centre, mart
    private static readonly int[] BottomBand = [3, 3, 3]; // houses

    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Ground);
        var midY = h / 2;

        CarveKit.Border(grid, LogicalTile.Tree);
        // Edge-aligned with the spine neighbours, so — unlike the old fixed midY — this can land inside a
        // building band's row. If this town's exit is also gated, GateCarver's approach-straightening pass
        // could then force a gap through that building's wall (cosmetic only: it only adds floor, so
        // reachability never breaks). Accepted for now; 5d's decoration-rules pass is the place to route
        // bands around a gated exit row if it ever reads badly in the sign-off packet.
        var leftY = planned.OffsetOr(EdgeSide.Left, midY);
        var rightY = planned.OffsetOr(EdgeSide.Right, midY);
        grid[0, leftY] = LogicalTile.Warp;
        grid[w - 1, rightY] = LogicalTile.Warp;

        var openings = new List<(int X, int Y)> { (0, leftY), (w - 1, rightY) };
        foreach (var o in planned.Where(o => o.Edge is EdgeSide.Top or EdgeSide.Bottom))
        {
            var tile = o.TileOn(w, h);
            grid[tile.X, tile.Y] = LogicalTile.Warp;
            openings.Add(tile);
        }

        var topY = 2;                 // building rows 2..4
        var bottomY = midY + 3;       // building rows midY+3..midY+5
        if (bottomY + 2 <= h - 2)
            PlaceBand(grid, rng, BottomBand, bottomY, doorOnBottom: false, openings);
        PlaceBand(grid, rng, TopBand, topY, doorOnBottom: true, openings);

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = openings };
    }

    private static void PlaceBand(TileGrid g, Pcg32 rng, int[] widths, int y0, bool doorOnBottom, List<(int, int)> openings)
    {
        var x = 2 + rng.NextInt(0, 2);
        foreach (var bw in widths)
        {
            if (x + bw > g.Width - 2) break;
            CarveKit.FillRect(g, x, y0, x + bw - 1, y0 + 2, LogicalTile.Wall);

            var doorX = x + bw / 2;
            var doorY = doorOnBottom ? y0 + 2 : y0;
            g[doorX, doorY] = LogicalTile.Warp; // door
            openings.Add((doorX, doorY));

            x += bw + 2;
        }
    }
}
