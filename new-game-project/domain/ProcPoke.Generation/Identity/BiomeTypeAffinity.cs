using ProcPoke.Data;
using ProcPoke.Generation.Biomes;

namespace ProcPoke.Generation.Identity;

/// <summary>
/// The §6.2 biome → type bias table, read in reverse for gym typing (§7.1): a mountain-ringed gym city
/// leans Rock/Ground/Fighting, a port leans Water. <see cref="Biome.Urban"/> — the meta-biome towns and
/// buildings carry — has no natural affinity of its own; gym typing falls through to neighbouring biomes.
/// </summary>
public static class BiomeTypeAffinity
{
    public static readonly IReadOnlyDictionary<Biome, IReadOnlyList<PokeType>> Table =
        new Dictionary<Biome, IReadOnlyList<PokeType>>
        {
            [Biome.Grassland] = [PokeType.Normal, PokeType.Grass, PokeType.Bug, PokeType.Flying],
            [Biome.Forest] = [PokeType.Bug, PokeType.Grass, PokeType.Poison, PokeType.Normal],
            [Biome.Cave] = [PokeType.Rock, PokeType.Ground, PokeType.Poison, PokeType.Dark],
            [Biome.Mountain] = [PokeType.Rock, PokeType.Ground, PokeType.Fighting, PokeType.Flying],
            [Biome.Water] = [PokeType.Water, PokeType.Ice],
            [Biome.Desert] = [PokeType.Ground, PokeType.Rock, PokeType.Dark, PokeType.Fire],
            [Biome.Urban] = [],
        };
}
