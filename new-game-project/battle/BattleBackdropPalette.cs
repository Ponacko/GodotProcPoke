using Godot;
using ProcPoke.Generation.Biomes;

namespace ProcPoke.BattleView;

/// <summary>Placeholder flat-color backdrop per biome family — same idea as the overworld's
/// <c>TileRealizerPalette</c>, until real battle backdrop art exists.</summary>
public static class BattleBackdropPalette
{
    public static Color Of(Biome biome) => biome switch
    {
        Biome.Grassland => new Color(0.45f, 0.68f, 0.32f),
        Biome.Forest => new Color(0.20f, 0.42f, 0.22f),
        Biome.Cave => new Color(0.28f, 0.26f, 0.30f),
        Biome.Mountain => new Color(0.52f, 0.50f, 0.55f),
        Biome.Water => new Color(0.24f, 0.48f, 0.72f),
        Biome.Desert => new Color(0.80f, 0.68f, 0.42f),
        Biome.Urban => new Color(0.55f, 0.55f, 0.60f),
        _ => new Color(0.4f, 0.4f, 0.4f),
    };
}
