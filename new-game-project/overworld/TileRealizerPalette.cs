using Godot;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Carving;

namespace ProcPoke.OverworldView;

/// <summary>Debug presentation only. Final art can replace this atlas without changing TileRealizer's seam.</summary>
public static class TileRealizerPalette
{
    public static Color For(Biome biome, LogicalTile tile)
    {
        var terrain = tile switch
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
            _ => Colors.Magenta,
        };

        if (tile is LogicalTile.Warp or LogicalTile.TrainerPost or LogicalTile.ItemBall or LogicalTile.NpcPost)
            return terrain;

        var tint = biome switch
        {
            Biome.Forest => Color.Color8(36, 118, 60),
            Biome.Cave => Color.Color8(92, 90, 118),
            Biome.Mountain => Color.Color8(136, 126, 114),
            Biome.Water => Color.Color8(58, 128, 190),
            Biome.Desert => Color.Color8(220, 172, 82),
            Biome.Urban => Color.Color8(148, 132, 142),
            _ => Color.Color8(186, 182, 108),
        };
        return terrain.Lerp(tint, 0.16f);
    }

    public static Color Ink(Biome biome) => biome switch
    {
        Biome.Cave => Color.Color8(32, 28, 52),
        Biome.Water => Color.Color8(22, 66, 112),
        Biome.Forest => Color.Color8(14, 60, 28),
        _ => Color.Color8(38, 38, 42),
    };
}
