using ProcPoke.Generation.Gating;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Stamps a placed <see cref="Gate"/> onto its area as physical tiles (ADR-0001, ADR-0002). Every spine
/// area exits on its right edge; this necks that exit so the tile grid itself enforces the lock — the exit
/// is unreachable on foot until the gate is cleared. Two shapes, per ADR-0004:
/// <list type="bullet">
/// <item>Portable obstacles (Cut, Strength, …) become a walled neck with the obstacle in a single gap.</item>
/// <item>Terrain-bound obstacles (the water HMs) become a full-width water span — a river you must Surf —
/// so the gate <em>is</em> its terrain and stays consistent with a water Biome Requirement.</item>
/// </list>
/// </summary>
public static class GateCarver
{
    /// <summary>How far the barrier column sits from the right edge — <see cref="OpeningAligner"/> reserves
    /// this same column on every area's horizontal edges so a branch opening never lands under it.</summary>
    public const int BarrierInsetFromRightEdge = 2;

    public static LogicalTile TileFor(ObstacleClass obstacle) => obstacle switch
    {
        ObstacleClass.CutTree => LogicalTile.CutTree,
        ObstacleClass.Boulder => LogicalTile.Boulder,
        ObstacleClass.SurfWater or ObstacleClass.DeepWater or ObstacleClass.Waterfall
            or ObstacleClass.Whirlpool or ObstacleClass.SeaCrossing => LogicalTile.Water,
        _ => LogicalTile.GateObstacle,
    };

    /// <summary>
    /// Returns a copy of <paramref name="carved"/> with the gate carved onto its right-edge exit. The grid
    /// is mutated in place (carvers hand us a fresh grid per area) and the new record carries the gate tiles.
    /// </summary>
    public static CarvedArea Apply(CarvedArea carved, Gate gate)
    {
        var g = carved.Grid;
        var w = g.Width;
        var h = g.Height;

        // Spine areas expose exactly one right-edge opening (the exit toward the League) — the gate blocks it.
        var exit = FindRightEdgeOpening(carved, w);
        if (exit is null) return carved; // defensive: nothing to gate
        var midY = exit.Value.Y;

        var barrierX = w - BarrierInsetFromRightEdge;
        var obstacle = TileFor(gate.Obstacle);

        // Guarantee the entry reaches the neck, whatever decoration the carver left — then the neck is the
        // only way through. Carving the whole spine row unconditionally would do it, but it also flattens a
        // winding cave (ticket 5b) into one dead-straight corridor. So grow the approach leftward from the
        // neck one tile at a time and stop the moment it meets ground the carver already laid: routes, whose
        // protected spine row arrives on its own, are untouched, and a cave keeps its bends. If nothing ever
        // connects the loop still carves the full row, which is the old unconditional behaviour as a floor.
        if (!ApproachReachesNeck(carved, g, barrierX, midY))
            GrowApproach(carved, g, barrierX, midY);

        // Town walls may intersect an edge-aligned exit row. Restore those walls after the generic
        // approach straightening pass; TownCarver excludes its actual doorway coordinates from this set.
        foreach (var (x, y) in carved.BuildingWallTiles)
            if (g.InBounds(x, y) && g[x, y] == LogicalTile.Ground) g[x, y] = LogicalTile.Wall;

        var gateTiles = new List<(int X, int Y)>();
        if (obstacle == LogicalTile.Water)
        {
            // A river across the path: the whole neck is water, crossed as one span once Surf is in hand.
            for (var y = 1; y <= h - 2; y++) { g[barrierX, y] = LogicalTile.Water; gateTiles.Add((barrierX, y)); }
        }
        else
        {
            // A walled neck with the portable obstacle in the single gap (the area's border tile as walls).
            var wall = g[0, 0].IsWalkable() ? LogicalTile.Wall : g[0, 0];
            for (var y = 1; y <= h - 2; y++) g[barrierX, y] = wall;
            g[barrierX, midY] = obstacle;
            gateTiles.Add((barrierX, midY));
        }

        return carved with { GateTiles = gateTiles };
    }

    /// <summary>
    /// Grows the approach leftward from the neck, one tile at a time, stopping the moment it meets ground the
    /// carver already laid — so the repair is as short as the layout allows. Reaching x = 1 without connecting
    /// leaves the whole row carved, which is the old unconditional behaviour as a floor.
    /// </summary>
    private static void GrowApproach(CarvedArea carved, TileGrid g, int barrierX, int midY)
    {
        for (var x = barrierX - 1; x >= 1; x--)
        {
            if (!g[x, midY].IsWalkable()) g[x, midY] = LogicalTile.Ground;
            if (ApproachReachesNeck(carved, g, barrierX, midY)) return;
        }
    }

    /// <summary>
    /// Whether every opening except the gated exit already walks to the tile just inside the neck. The
    /// barrier column is treated as blocked, since it is about to become one — so a true answer means the
    /// lock still works without touching the carver's layout.
    /// </summary>
    private static bool ApproachReachesNeck(CarvedArea carved, TileGrid g, int barrierX, int midY)
    {
        var neck = (X: barrierX - 1, Y: midY);
        if (!g.InBounds(neck.X, neck.Y) || !g[neck.X, neck.Y].IsWalkable()) return false;

        foreach (var opening in carved.Openings)
        {
            // The exit sits beyond the barrier; it is reached through the gap once the gate is cleared.
            if (opening.X == g.Width - 1) continue;
            if (!Reaches(g, opening, neck, blockedX: barrierX)) return false;
        }
        return true;
    }

    private static bool Reaches(TileGrid g, (int X, int Y) from, (int X, int Y) to, int blockedX)
    {
        if (!g[from.X, from.Y].IsWalkable()) return false;

        var seen = new HashSet<(int X, int Y)> { from };
        var queue = new Queue<(int X, int Y)>([from]);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if ((x, y) == to) return true;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (X: x + dx, Y: y + dy);
                if (!g.InBounds(next.X, next.Y) || next.X == blockedX) continue;
                if (!g[next.X, next.Y].IsWalkable() || !seen.Add(next)) continue;
                queue.Enqueue(next);
            }
        }
        return false;
    }

    private static (int X, int Y)? FindRightEdgeOpening(CarvedArea carved, int width)
    {
        foreach (var o in carved.Openings)
            if (o.X == width - 1) return o;
        return null;
    }
}
