using ProcPoke.Data;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Identity;

/// <summary>
/// The gym/Elite Four/Champion typing pass (§7.1): each gym city picks the highest-affinity type from its
/// own and neighbouring biomes (§6.2, read in reverse) that no earlier gym already claimed; ties within
/// equal affinity break deterministically off <c>streams.Stream("gym-types")</c>. The Elite Four then draw
/// 4 more distinct types from whatever remains. The Champion is always typeless.
/// </summary>
public static class GymTypingPass
{
    public static RegionIdentity Generate(RegionGraph graph, BiomeMap biomes, RngStreams streams)
    {
        var rng = streams.Stream("gym-types");
        var allTypes = Enum.GetValues<PokeType>();
        var taken = new HashSet<PokeType>();
        var gymTypes = new Dictionary<int, PokeType>();

        var gymCities = graph.CriticalPath.Where(a => a.Archetype == AreaArchetype.Town).OrderBy(a => a.PathIndex);
        foreach (var city in gymCities)
        {
            var ranked = RankedAffinity(graph, biomes, city.Id, rng);
            var chosen = FirstUntaken(ranked, taken)
                ?? rng.Pick(allTypes.Where(t => !taken.Contains(t)).ToList());
            taken.Add(chosen);
            gymTypes[city.Id] = chosen;
        }

        var remaining = allTypes.Where(t => !taken.Contains(t)).ToList();
        rng.Shuffle(remaining);
        var eliteFourTypes = remaining.Take(4).ToList();

        return new RegionIdentity { GymTypes = gymTypes, EliteFourTypes = eliteFourTypes };
    }

    /// <summary>Candidate types for a gym city, ranked by how many of its own + neighbouring biomes carry
    /// them (own biome is usually Urban, which contributes nothing — the surrounding terrain decides).
    /// Ties within a score tier are shuffled off the pass's own RNG stream, not left to dictionary order.</summary>
    private static List<PokeType> RankedAffinity(RegionGraph graph, BiomeMap biomes, int areaId, Pcg32 rng)
    {
        var score = new Dictionary<PokeType, int>();
        void Tally(Biome biome)
        {
            foreach (var t in BiomeTypeAffinity.Table[biome])
                score[t] = score.GetValueOrDefault(t) + 1;
        }

        Tally(biomes.Of(areaId));
        foreach (var neighbor in graph.Neighbors(areaId))
            Tally(biomes.Of(neighbor.Id));

        var ranked = new List<PokeType>();
        foreach (var tier in score.GroupBy(kv => kv.Value).OrderByDescending(g => g.Key))
        {
            var group = tier.Select(kv => kv.Key).ToList();
            rng.Shuffle(group);
            ranked.AddRange(group);
        }
        return ranked;
    }

    private static PokeType? FirstUntaken(List<PokeType> ranked, HashSet<PokeType> taken)
    {
        foreach (var t in ranked)
            if (!taken.Contains(t)) return t;
        return null;
    }
}
