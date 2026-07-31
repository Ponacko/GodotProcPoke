using ProcPoke.Generation.Gating;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Stamps a placed <see cref="Gate"/> onto its area as physical tiles (ADR-0001, ADR-0002). The area's
/// spine exit is necked so the tile grid itself enforces the lock — the exit is unreachable on foot until
/// the gate is cleared. Two shapes, per ADR-0004:
/// <list type="bullet">
/// <item>Portable obstacles (Cut, Strength, …) become a walled neck with the obstacle in a single gap.</item>
/// <item>Terrain-bound obstacles (the water HMs) become a full-width water span — a river you must Surf —
/// so the gate <em>is</em> its terrain and stays consistent with a water Biome Requirement.</item>
/// </list>
/// <para>
/// The exit can be on any of the four borders, since the spine wanders (<c>SpineEmbedder</c>). Rather than
/// spelling the neck out four times, the whole routine runs in the exit's <see cref="CarveFrame"/> — the
/// grid rotated so the exit border is always at <c>u == U - 1</c> — and writes tiles back through it.
/// </para>
/// </summary>
public static class GateCarver
{
    /// <summary>How far the barrier line sits from the exit border — <see cref="OpeningAligner"/> reserves
    /// this same line on every area's edges so a branch opening never lands behind it.</summary>
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
    /// Returns a copy of <paramref name="carved"/> with the gate carved onto the exit on
    /// <paramref name="exitSide"/>. The grid is mutated in place (carvers hand us a fresh grid per area) and
    /// the new record carries the gate tiles.
    /// </summary>
    public static CarvedArea Apply(CarvedArea carved, Gate gate, EdgeSide exitSide)
    {
        var g = carved.Grid;
        var f = new CarveFrame(exitSide, g.Width, g.Height);

        // The area exposes exactly one opening on its exit border (the way onward) — the gate blocks it.
        var exit = FindExitOpening(carved, f);
        if (exit is null) return carved; // defensive: nothing to gate (a Warp exit carves no edge opening)
        var midV = exit.Value;

        var barrierU = f.U - BarrierInsetFromRightEdge;
        var obstacle = TileFor(gate.Obstacle);

        // Guarantee the entry reaches the neck, whatever decoration the carver left — then the neck is the
        // only way through. Carving the whole spine row unconditionally would do it, but it also flattens a
        // winding cave (ticket 5b) into one dead-straight corridor. So grow the approach inward from the
        // neck one tile at a time and stop the moment it meets ground the carver already laid: routes, whose
        // protected spine row arrives on its own, are untouched, and a cave keeps its bends. If nothing ever
        // connects the loop still carves the full row, which is the old unconditional behaviour as a floor.
        if (!ApproachReachesNeck(carved, g, f, barrierU, midV))
            GrowApproach(carved, g, f, barrierU, midV);

        // Town walls may intersect an edge-aligned exit row. Restore those walls after the generic
        // approach straightening pass; TownCarver excludes its actual doorway coordinates from this set.
        foreach (var (x, y) in carved.BuildingWallTiles)
            if (g.InBounds(x, y) && g[x, y] == LogicalTile.Ground) g[x, y] = LogicalTile.Wall;

        var gateTiles = new List<(int X, int Y)>();
        if (obstacle == LogicalTile.Water)
        {
            // A river across the path: the whole neck is water, crossed as one span once Surf is in hand.
            for (var v = 1; v <= f.V - 2; v++)
            {
                var (x, y) = f.Map(barrierU, v);
                g[x, y] = LogicalTile.Water;
                gateTiles.Add((x, y));
            }
        }
        else
        {
            // A walled neck with the portable obstacle in the single gap (the area's border tile as walls).
            var wall = g[0, 0].IsWalkable() ? LogicalTile.Wall : g[0, 0];
            for (var v = 1; v <= f.V - 2; v++)
            {
                var (wx, wy) = f.Map(barrierU, v);
                g[wx, wy] = wall;
            }
            var gap = f.Map(barrierU, midV);
            g[gap.X, gap.Y] = obstacle;
            gateTiles.Add(gap);
        }

        ForceApproach(carved, g, f, barrierU, midV);
        if (carved.TrunkRow >= 0) RestoreItemNook(carved, gateTiles.ToHashSet());
        return carved with { GateTiles = gateTiles };
    }

    /// <summary>
    /// Last-resort repair, run once the barrier is actually stamped: any opening that still cannot walk to the
    /// neck gets a corridor cut to it — in to the exit row, then along it.
    /// <para>
    /// <see cref="GrowApproach"/> alone is not enough because the carvers decide what blocks what *before* the
    /// gate exists. A cave picks its boulder field by checking reachability on the pre-gate grid (ADR-0004
    /// order), so a boulder can sit on a chokepoint of the layout the gate then creates: wall the barrier line,
    /// re-route the approach along the exit row, and that boulder is suddenly the only thing between the entry
    /// and the neck. The area is then sealed even after the player clears the gate.
    /// </para>
    /// <para>
    /// This only ever carves on the near side of the barrier, so the lock still holds: the exit stays
    /// unreachable until the obstacle in the gap is cleared. Building walls are left alone — a town's street
    /// runs along the border column the repair uses, so it has a way through without knocking a house open.
    /// </para>
    /// </summary>
    private static void ForceApproach(CarvedArea carved, TileGrid g, CarveFrame f, int barrierU, int midV)
    {
        var neck = (U: barrierU - 1, V: midV);
        var walls = carved.BuildingWallTiles;

        void Open(int u, int v)
        {
            var (x, y) = f.Map(u, v);
            if (!g.InBounds(x, y) || g[x, y].IsWalkable() || walls.Contains((x, y))) return;
            g[x, y] = LogicalTile.Ground;
        }

        foreach (var opening in carved.Openings)
        {
            var from = f.Unmap(opening.X, opening.Y);
            if (from.U == f.U - 1) continue; // the exit itself, reached through the gap
            if (Reaches(g, f, from, neck, blockedU: barrierU)) continue;

            // Stay off the barrier line and off the outer frame; OpeningAligner keeps every opening's offset
            // clear of the barrier, so this lane is always available.
            var lane = Math.Clamp(from.U, 1, barrierU - 1);
            for (var v = Math.Min(from.V, midV); v <= Math.Max(from.V, midV); v++)
                if (v >= 1 && v <= f.V - 2) Open(lane, v);
            for (var u = Math.Min(lane, neck.U); u <= Math.Max(lane, neck.U); u++)
                Open(u, midV);
        }
    }

    /// <summary>
    /// Grows the approach inward from the neck, one tile at a time, stopping the moment it meets ground the
    /// carver already laid — so the repair is as short as the layout allows. Reaching u = 1 without connecting
    /// leaves the whole row carved, which is the old unconditional behaviour as a floor.
    /// </summary>
    private static void GrowApproach(CarvedArea carved, TileGrid g, CarveFrame f, int barrierU, int midV)
    {
        for (var u = barrierU - 1; u >= 1; u--)
        {
            var (x, y) = f.Map(u, midV);
            if (!g[x, y].IsWalkable()) g[x, y] = LogicalTile.Ground;
            if (ApproachReachesNeck(carved, g, f, barrierU, midV)) return;
        }
    }

    /// <summary>
    /// Whether every opening except the gated exit already walks to the tile just inside the neck. The
    /// barrier line is treated as blocked, since it is about to become one — so a true answer means the
    /// lock still works without touching the carver's layout.
    /// </summary>
    private static bool ApproachReachesNeck(CarvedArea carved, TileGrid g, CarveFrame f, int barrierU, int midV)
    {
        var neck = (U: barrierU - 1, V: midV);
        var (nx, ny) = f.Map(neck.U, neck.V);
        if (!g.InBounds(nx, ny) || !g[nx, ny].IsWalkable()) return false;

        foreach (var opening in carved.Openings)
        {
            var from = f.Unmap(opening.X, opening.Y);
            // The exit sits beyond the barrier; it is reached through the gap once the gate is cleared.
            if (from.U == f.U - 1) continue;
            if (!Reaches(g, f, from, neck, blockedU: barrierU)) return false;
        }
        return true;
    }

    private static bool Reaches(
        TileGrid g, CarveFrame f, (int U, int V) from, (int U, int V) to, int blockedU)
    {
        var (fx, fy) = f.Map(from.U, from.V);
        if (!g[fx, fy].IsWalkable()) return false;

        var seen = new HashSet<(int U, int V)> { from };
        var queue = new Queue<(int U, int V)>([from]);
        while (queue.Count > 0)
        {
            var (u, v) = queue.Dequeue();
            if ((u, v) == to) return true;
            foreach (var (du, dv) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (U: u + du, V: v + dv);
                if (next.U < 0 || next.U >= f.U || next.V < 0 || next.V >= f.V) continue;
                if (next.U == blockedU) continue;
                var (x, y) = f.Map(next.U, next.V);
                if (!g[x, y].IsWalkable() || !seen.Add(next)) continue;
                queue.Enqueue(next);
            }
        }
        return false;
    }

    /// <summary>The across-axis position of the opening on the exit border, or null if there is none.</summary>
    private static int? FindExitOpening(CarvedArea carved, CarveFrame f)
    {
        foreach (var o in carved.Openings)
        {
            var (u, v) = f.Unmap(o.X, o.Y);
            if (u == f.U - 1) return v;
        }
        return null;
    }

    /// <summary>
    /// Gate approach repair can legitimately reopen a decorative blocker. Restore the item-nook invariant
    /// without compromising the gate: a replacement blocker is accepted only when every opening still
    /// reaches every other opening with the gate tiles treated as cleared.
    /// </summary>
    private static void RestoreItemNook(CarvedArea carved, IReadOnlySet<(int X, int Y)> gateTiles)
    {
        var g = carved.Grid;
        var wall = g[0, 0] is LogicalTile.Wall or LogicalTile.Tree ? g[0, 0] : LogicalTile.Wall;
        var openings = carved.Openings;
        foreach (var item in Interior(g).Where(p => g[p.X, p.Y] == LogicalTile.ItemBall))
        {
            if (NonWalkableNeighbours(g, item) >= 2) continue;
            foreach (var candidate in Neighbours(item))
            {
                if (!g.InBounds(candidate.X, candidate.Y) || openings.Contains(candidate)
                    || gateTiles.Contains(candidate) || g[candidate.X, candidate.Y] == LogicalTile.Ledge
                    || WouldStrandLedge(g, candidate) || !g[candidate.X, candidate.Y].IsWalkable()) continue;

                var old = g[candidate.X, candidate.Y];
                g[candidate.X, candidate.Y] = wall;
                if (NonWalkableNeighbours(g, item) >= 2 && OpeningsReachEachOther(g, openings, gateTiles)) break;
                g[candidate.X, candidate.Y] = old;
            }
        }
    }

    private static bool OpeningsReachEachOther(
        TileGrid grid, IReadOnlyList<(int X, int Y)> openings, IReadOnlySet<(int X, int Y)> cleared)
    {
        if (openings.Count < 2) return true;
        var seen = new HashSet<(int X, int Y)> { openings[0] };
        var queue = new Queue<(int X, int Y)>([openings[0]]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in Neighbours(current))
            {
                if (!grid.InBounds(next.X, next.Y) || !seen.Add(next)) continue;
                if (!grid[next.X, next.Y].IsWalkable() && !cleared.Contains(next)) continue;
                queue.Enqueue(next);
            }
        }
        return openings.All(seen.Contains);
    }

    private static int NonWalkableNeighbours(TileGrid grid, (int X, int Y) cell)
        => Neighbours(cell).Count(p => !grid.InBounds(p.X, p.Y) || !grid[p.X, p.Y].IsWalkable());

    private static bool WouldStrandLedge(TileGrid grid, (int X, int Y) cell)
        => IsLedge(grid, cell.X, cell.Y - 1) || IsLedge(grid, cell.X, cell.Y + 1);

    private static bool IsLedge(TileGrid grid, int x, int y)
        => grid.InBounds(x, y) && grid[x, y] == LogicalTile.Ledge;

    private static IEnumerable<(int X, int Y)> Neighbours((int X, int Y) cell)
    {
        yield return (cell.X + 1, cell.Y);
        yield return (cell.X - 1, cell.Y);
        yield return (cell.X, cell.Y + 1);
        yield return (cell.X, cell.Y - 1);
    }

    private static IEnumerable<(int X, int Y)> Interior(TileGrid grid)
    {
        for (var y = 1; y < grid.Height - 1; y++)
            for (var x = 1; x < grid.Width - 1; x++) yield return (x, y);
    }
}
