using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Biomes;

/// <summary>
/// The biome pass (ADR-0004: after gates). Dungeon archetypes dictate their own biome; terrain-bound
/// gates pin their areas as fixed points (a Surf gate ⇒ that area is Water); the remaining routes take a
/// grassland default that blends toward an adjacent forest or mountain, so terrain transitions read
/// coherently (a grassy route flows into the forest it borders).
/// </summary>
public static class BiomePass
{
    public static BiomeMap Generate(RegionGraph graph, GatingPlan gating, RngStreams streams)
    {
        var rng = streams.Stream("biomes");
        var biome = new Dictionary<int, Biome>();

        foreach (var area in graph.Areas)
            biome[area.Id] = ArchetypeBiome(area.Archetype);

        // Terrain-bound obstacles are fixed points the pass must honor.
        var pinned = new HashSet<int>();
        foreach (var (areaId, tag) in gating.BiomeRequirements)
        {
            biome[areaId] = TagBiome(tag);
            pinned.Add(areaId);
        }

        // Blend plain routes toward a bordering forest/mountain for coherent transitions.
        foreach (var area in graph.Areas)
        {
            if (area.Archetype != AreaArchetype.Route || pinned.Contains(area.Id)) continue;
            if (biome[area.Id] != Biome.Grassland) continue;

            var neighbours = graph.Neighbors(area.Id).Select(n => biome[n.Id]).ToList();
            if (neighbours.Contains(Biome.Forest) && rng.Chance(0.6)) biome[area.Id] = Biome.Forest;
            else if (neighbours.Contains(Biome.Mountain) && rng.Chance(0.6)) biome[area.Id] = Biome.Mountain;
            else if (neighbours.Contains(Biome.Desert) && rng.Chance(0.5)) biome[area.Id] = Biome.Desert;
        }

        return new BiomeMap { ByArea = biome };
    }

    private static Biome ArchetypeBiome(AreaArchetype a) => a switch
    {
        AreaArchetype.Forest => Biome.Forest,
        AreaArchetype.StandardCave or AreaArchetype.DeepCave => Biome.Cave,
        AreaArchetype.MountainPath or AreaArchetype.VictoryRoad => Biome.Mountain,
        AreaArchetype.StartTown or AreaArchetype.Town or AreaArchetype.League
            or AreaArchetype.Tower or AreaArchetype.VillainHideout => Biome.Urban,
        _ => Biome.Grassland, // Route
    };

    private static Biome TagBiome(string tag) => tag switch
    {
        "water" or "deep-water" or "sea" => Biome.Water,
        "desert" => Biome.Desert,
        _ => Biome.Grassland,
    };
}
