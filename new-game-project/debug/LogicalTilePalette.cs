using Godot;
using ProcPoke.Generation.Carving;

namespace ProcPoke.DebugView;

/// <summary>
/// Debug colours for the Logical Tile vocabulary, kept identical to <c>ProcPoke.MapGen</c>'s PNG palette so
/// the in-engine view and the go/no-go review packet read the same. This is deliberately *not* the Tile
/// Realizer (CONTEXT.md): it paints gameplay semantics, never art. An unmapped tile comes out magenta, so a
/// newly added <see cref="LogicalTile"/> is impossible to miss on screen.
/// </summary>
public static class LogicalTilePalette
{
    public static readonly Color Background = Color.Color8(24, 24, 28);

    /// <summary>Connector drawn between the paired openings of a Map Connection.</summary>
    public static readonly Color Connector = Color.Color8(245, 245, 235);

    /// <summary>Connector for a loop-back edge (§4.2) — dimmer, so shortcuts read as secondary.</summary>
    public static readonly Color LoopBackConnector = Color.Color8(120, 190, 255);

    public static readonly Color SpineOutline = Color.Color8(232, 226, 196);
    public static readonly Color BranchOutline = Color.Color8(96, 104, 124);
    public static readonly Color Selection = Color.Color8(255, 214, 64);
    public static readonly Color AreaLabel = Color.Color8(226, 226, 220);

    /// <summary>Map-key reading order: terrain, then obstacles, then markers.</summary>
    public static readonly LogicalTile[] LegendOrder =
    [
        LogicalTile.Ground,
        LogicalTile.TallGrass,
        LogicalTile.Sand,
        LogicalTile.Water,
        LogicalTile.Wall,
        LogicalTile.Tree,
        LogicalTile.Ledge,
        LogicalTile.Boulder,
        LogicalTile.CutTree,
        LogicalTile.GateObstacle,
        LogicalTile.Warp,
        LogicalTile.TrainerPost,
        LogicalTile.ItemBall,
        LogicalTile.NpcPost,
    ];

    public static Color Of(LogicalTile tile) => tile switch
    {
        LogicalTile.Ground => Color.Color8(210, 200, 150),
        LogicalTile.TallGrass => Color.Color8(90, 170, 80),
        LogicalTile.Tree => Color.Color8(30, 100, 40),
        LogicalTile.Wall => Color.Color8(110, 100, 95),
        LogicalTile.Water => Color.Color8(70, 130, 200),
        LogicalTile.Sand => Color.Color8(225, 205, 140),
        LogicalTile.Ledge => Color.Color8(170, 140, 90),
        LogicalTile.Boulder => Color.Color8(120, 110, 100),
        LogicalTile.CutTree => Color.Color8(60, 140, 60),
        LogicalTile.GateObstacle => Color.Color8(200, 40, 40),
        LogicalTile.Warp => Color.Color8(240, 220, 60),
        LogicalTile.TrainerPost => Color.Color8(220, 60, 180),
        LogicalTile.ItemBall => Color.Color8(240, 150, 40),
        LogicalTile.NpcPost => Color.Color8(80, 220, 220),
        _ => Color.Color8(255, 0, 255),
    };

    /// <summary>Map-key caption: what the colour means in gameplay terms.</summary>
    public static string Describe(LogicalTile tile) => tile switch
    {
        LogicalTile.Ground => "ground — plain walkable",
        LogicalTile.TallGrass => "tall grass — wild encounters",
        LogicalTile.Sand => "sand — desert floor, walkable",
        LogicalTile.Water => "water — surfable, blocked on foot",
        LogicalTile.Wall => "wall / cliff face",
        LogicalTile.Tree => "tree clump — impassable",
        LogicalTile.Ledge => "ledge — one-way hop south",
        LogicalTile.Boulder => "boulder — Strength obstacle",
        LogicalTile.CutTree => "cut tree — Cut obstacle",
        LogicalTile.GateObstacle => "gate — blocks a chokepoint",
        LogicalTile.Warp => "warp — edge opening or door",
        LogicalTile.TrainerPost => "trainer post",
        LogicalTile.ItemBall => "item ball",
        LogicalTile.NpcPost => "NPC post / sign",
        _ => $"{tile} — no palette entry",
    };
}
