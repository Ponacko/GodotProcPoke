using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a forest transit area: a walkable field framed by trees and dotted with organic tree clumps (not
/// the old full-height stripe barriers). A protected three-row spine band always stays open between the
/// edge-aligned left/right openings, and every branch (Top/Bottom) opening reaches it through a protected
/// column, so all openings are mutually reachable. Tall grass straddles the path for wild encounters.
/// </summary>
public static class ForestCarver
{
    public static CarvedArea Carve(
        Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned, EdgeSide? spineExit)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Ground);
        var midY = h / 2;

        CarveKit.Border(grid, LogicalTile.Tree);

        // Spine = the row of whichever side-to-side border the area has, edge-aligned with that neighbour;
        // midY when the spine runs top-to-bottom here and there is no horizontal border to align to.
        var spineY = CarveKit.TrunkRow(planned, spineExit, h);

        // Three protected spine rows: always Ground, so left↔right traversal is guaranteed and no interior
        // column is ever entirely Tree. Clumps never overwrite a protected tile.
        var guarded = new HashSet<(int X, int Y)>();
        for (var dy = -1; dy <= 1; dy++)
        {
            var y = spineY + dy;
            if (y < 1 || y > h - 2) continue;
            for (var x = 1; x < w - 1; x++) { grid[x, y] = LogicalTile.Ground; guarded.Add((x, y)); }
        }

        // Only borders that face a neighbour are opened; opening a blank border leaves a hole in the map.
        var openings = planned
            .Select(o => CarveKit.OpenSpineEdge(grid, guarded, o.Edge, o.Offset, spineY, LogicalTile.Ground))
            .ToList();

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
        DecorationKit.PlaceTrainerPosts(grid, LogicalTile.Ground, spineY, 1 + rng.NextInt(2), rng, guarded, nearOpening);

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
