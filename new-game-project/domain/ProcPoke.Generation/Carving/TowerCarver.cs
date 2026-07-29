using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a tower as stacked floor bands. Full-span wall lines with one alternating stair gap force a zigzag
/// climb while keeping the whole area in one ordinary tile-grid reachability graph.
/// <para>
/// Laid out in the entrance's <see cref="CarveFrame"/>: the entrance is always at <c>u == U - 1</c> and the
/// floors stack away from it, so the reward band is the one farthest from the door whichever border the door
/// is on. Written against the grid directly this only worked for a bottom entrance — a side entrance's link
/// to the ground floor ran straight through every separator line, punching a hole in each and destroying the
/// climb it exists to create.
/// </para>
/// </summary>
public static class TowerCarver
{
    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Wall);
        CarveKit.Border(grid, LogicalTile.Wall);

        var entrance = planned.FirstOrDefault();
        var f = new CarveFrame(entrance?.Edge ?? EdgeSide.Bottom, w, h);

        // Open the whole interior, then divide it back up with the separator lines.
        f.Fill(grid, 1, 1, f.U - 2, f.V - 2, LogicalTile.Ground);

        var floors = 3 + rng.NextInt(2);
        var bands = Bands(f.U, floors);

        // Separator between each pair of bands, its single gap alternating side to side so the climb zigzags.
        // Counted from the entrance end so the alternation reads the same however many floors there are.
        var separators = new List<(int U, int GapV)>();
        for (var i = 0; i < bands.Count - 1; i++)
        {
            var separatorU = bands[i].U1 + 1;
            var fromEntrance = bands.Count - 2 - i;
            var gapV = fromEntrance % 2 == 0 ? f.V - 3 : 2;
            f.Fill(grid, separatorU, 1, separatorU, f.V - 2, LogicalTile.Wall);
            f.Write(grid, separatorU, gapV, LogicalTile.Ground);
            separators.Add((separatorU, gapV));
        }

        // The entrance sits on the reference border, so its offset already is a canonical v, and the tile just
        // inside it belongs to the ground-floor band — no stub needed, and none crosses a separator.
        var entranceV = f.InteriorV(entrance?.Offset ?? f.V / 2);
        f.Write(grid, f.U - 2, entranceV, LogicalTile.Ground);
        var opening = f.Map(f.U - 1, entranceV);
        grid[opening.X, opening.Y] = LogicalTile.Warp;

        // Reward on the top floor: the band farthest from the entrance, beyond every separator.
        var top = bands[0];
        var rewardU = (top.U0 + top.U1) / 2;
        var postV = Math.Clamp(f.V / 2, 1, f.V - 3);
        f.Write(grid, rewardU, postV, LogicalTile.TrainerPost);
        f.Write(grid, rewardU, postV + 1, LogicalTile.ItemBall);

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = [opening] };
    }

    /// <summary>
    /// Floor bands over the interior <c>u</c> range, one separator line between each pair. Index 0 is the band
    /// farthest from the entrance (the reward floor); the last band is the one the entrance opens into.
    /// Sized from the frame rather than fixed, since the climb axis is the grid's height for a top or bottom
    /// entrance and its width for a side one.
    /// </summary>
    private static List<(int U0, int U1)> Bands(int u, int floors)
    {
        var interior = u - 2;
        var bandTotal = interior - (floors - 1);
        var basis = bandTotal / floors;
        var extra = bandTotal % floors;

        var bands = new List<(int U0, int U1)>();
        var start = 1;
        for (var i = 0; i < floors; i++)
        {
            var depth = basis + (i < extra ? 1 : 0);
            bands.Add((start, start + depth - 1));
            start += depth + 1; // +1 for the separator line that follows
        }
        return bands;
    }
}
