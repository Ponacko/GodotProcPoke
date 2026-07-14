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

        // Guarantee a straight approach along the spine row so the entry always reaches the neck, whatever
        // decoration the carver left — then the neck is the only way through.
        for (var x = 1; x < barrierX; x++)
            if (!g[x, midY].IsWalkable()) g[x, midY] = LogicalTile.Ground;

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

    private static (int X, int Y)? FindRightEdgeOpening(CarvedArea carved, int width)
    {
        foreach (var o in carved.Openings)
            if (o.X == width - 1) return o;
        return null;
    }
}
