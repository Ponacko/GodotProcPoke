namespace ProcPoke.Generation.Carving;

/// <summary>
/// The gameplay-semantic tile vocabulary carvers emit (CONTEXT.md "Logical Tile"). Carries meaning only,
/// never art — the Phase 3 Tile Realizer turns these plus a biome into rendered tiles. This enum is the
/// contract shared by carvers, invariant tests, the debug renderer, and (later) overworld collision, so
/// it changes expensively.
/// </summary>
public enum LogicalTile
{
    Ground,        // plain walkable
    Wall,          // impassable filler / cliff face
    Tree,          // border/decorative tree clump (impassable)
    TallGrass,     // walkable, wild-encounter tile
    Ledge,         // one-way hop (walkable for connectivity)
    Water,         // surfable water (impassable on foot)
    Sand,          // desert floor (walkable)
    Boulder,       // Strength obstacle
    CutTree,       // Cut obstacle
    GateObstacle,  // a placed gate's blocking object on a chokepoint
    Warp,          // connection point (edge opening / door)
    TrainerPost,   // a trainer stands here
    ItemBall,      // a collectible item
}

public static class LogicalTileExtensions
{
    /// <summary>Whether a Pokémon trainer can stand on / walk through this tile (used for connectivity checks).</summary>
    public static bool IsWalkable(this LogicalTile t) => t switch
    {
        LogicalTile.Ground or LogicalTile.TallGrass or LogicalTile.Ledge or LogicalTile.Sand
            or LogicalTile.Warp or LogicalTile.TrainerPost or LogicalTile.ItemBall => true,
        _ => false,
    };
}
