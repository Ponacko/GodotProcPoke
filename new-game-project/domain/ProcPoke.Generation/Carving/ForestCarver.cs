using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a forest transit area: a walkable field framed by trees and dotted with organic tree clumps (not
/// the old full-height stripe barriers). A protected three-tile travel band follows the entry-to-exit axis
/// and turns with the region lattice; branch openings reach it through protected stubs, so all openings are
/// mutually reachable. Tall grass straddles the path for wild encounters.
/// </summary>
public static class ForestCarver
{
    public static CarvedArea Carve(
        Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned,
        EdgeSide? spineExit, EdgeSide? spineEntry = null)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Ground);
        CarveKit.Border(grid, LogicalTile.Tree);

        // TrunkRow remains the world-space decoration anchor; the actual guarded corridor below follows the
        // entry/exit borders and can turn vertically.
        var spineY = CarveKit.TrunkRow(planned, spineExit, h);

        var guarded = new HashSet<(int X, int Y)>();
        // Only borders that face a neighbour are opened. The three-tile band is written in the
        // exit-oriented CarveFrame, so a turn is an L rather than a horizontal stripe.
        var openings = TravelCorridor.Carve(
            grid, planned, spineEntry, spineExit, LogicalTile.Ground, guarded, halfWidth: 1);

        // Keep decoration off the line a gate barrier would wall over, or the item placed there is lost.
        foreach (var tile in CarveKit.BarrierLine(spineExit, w, h)) guarded.Add(tile);

        // Tiles touching an opening stay clear so a clump can never wall off an entrance.
        var nearOpening = new HashSet<(int X, int Y)>();
        foreach (var (ox, oy) in openings)
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    nearOpening.Add((ox + dx, oy + dy));

        // Organic tree clumps: filled ellipses at random interior centres, kept ≥6 apart (and off the spine
        // band / openings) so each stays a discrete component — the whole point of replacing the stripes.
        var clumpCount = 6 + rng.NextInt(5);
        var centres = new List<(int X, int Y)>();
        for (var c = 0; c < clumpCount; c++)
        {
            var placed = false;
            int cx = 0, cy = 0;
            for (var t = 0; t < 20 && !placed; t++)
            {
                cx = rng.NextInt(2, w - 2);
                cy = rng.NextInt(2, h - 2);
                if (guarded.Contains((cx, cy)) || nearOpening.Contains((cx, cy))) continue;
                if (centres.Any(p => Math.Max(Math.Abs(p.X - cx), Math.Abs(p.Y - cy)) < 6)) continue;
                placed = true;
            }
            if (!placed) continue; // grid too crowded for another discrete clump — stop adding here

            centres.Add((cx, cy));
            var halfW = 1 + rng.NextInt(2);
            var halfH = 1 + rng.NextInt(2);
            StampClump(grid, cx, cy, halfW, halfH, guarded, nearOpening);
        }

        // Tall grass straddling the path (walkable).
        for (var i = 0; i < 4; i++)
        {
            var cx = rng.NextInt(3, w - 3);
            var cy = spineY + rng.NextInt(-2, 3);
            CarveKit.FillRect(grid, cx - 1, cy - 1, cx + 1, cy + 1, LogicalTile.TallGrass);
        }

        DecorationKit.PlaceItemNook(grid, LogicalTile.Ground, LogicalTile.Tree, spineY, rng, guarded, nearOpening);
        DecorationKit.PlaceTrainerPosts(grid, LogicalTile.Ground, LogicalTile.Tree, spineY,
            1 + rng.NextInt(2), rng, guarded, nearOpening);

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = openings, TrunkRow = spineY };
    }

    /// <summary>Stamps a filled ellipse of <see cref="LogicalTile.Tree"/>, skipping protected and
    /// opening-adjacent tiles.</summary>
    private static void StampClump(
        TileGrid g, int cx, int cy, int halfW, int halfH, HashSet<(int X, int Y)> guarded, HashSet<(int X, int Y)> nearOpening)
    {
        for (var dy = -halfH; dy <= halfH; dy++)
            for (var dx = -halfW; dx <= halfW; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 1 || x > g.Width - 2 || y < 1 || y > g.Height - 2) continue;
                if (dx * dx / (double)(halfW * halfW) + dy * dy / (double)(halfH * halfH) > 1.0) continue;
                if (guarded.Contains((x, y)) || nearOpening.Contains((x, y))) continue;
                g[x, y] = LogicalTile.Tree;
            }
    }
}
