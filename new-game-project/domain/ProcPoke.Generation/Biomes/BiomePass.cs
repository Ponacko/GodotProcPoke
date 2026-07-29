using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Biomes;

/// <summary>
/// The biome pass (ADR-0004: after gates). Dungeon and settlement archetypes dictate their own biome;
/// terrain-bound gates pin their areas as fixed points (a Surf gate ⇒ that area is Water); every remaining
/// route takes the biome of the region zone it falls in.
/// <para>
/// Zones are seeded on the overview lattice and claimed by nearest seed, so a biome comes out as a
/// contiguous stretch of the map — a run of desert, a coastline, a range of hills — rather than being
/// sprinkled per area. Routes previously defaulted to Grassland and only changed if they happened to border
/// a forest or mountain <em>archetype</em>, which meant a region was grassland everywhere except beside its
/// caves and forests: Desert needed a rare "desert" gate tag to appear at all, and Water needed a Surf gate.
/// </para>
/// </summary>
public static class BiomePass
{
    /// <summary>
    /// Biomes a zone may be, and how often. Grassland stays the most likely so a region still reads as
    /// mostly temperate, with the rest giving it its character.
    /// </summary>
    private static readonly (Biome Biome, double Weight)[] ZoneBiomes =
    [
        (Biome.Grassland, 3.0),
        (Biome.Forest, 2.0),
        (Biome.Mountain, 2.0),
        (Biome.Desert, 1.25),
        (Biome.Water, 1.25),
    ];

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

        var zones = SeedZones(graph, rng);
        foreach (var area in graph.Areas)
        {
            if (area.Archetype != AreaArchetype.Route || pinned.Contains(area.Id)) continue;
            biome[area.Id] = NearestZone(area.Cell, zones);
        }

        return new BiomeMap { ByArea = biome };
    }

    private readonly record struct Zone(GridCell Seed, Biome Biome);

    /// <summary>
    /// Scatters one zone seed per few areas across the lattice's bounding box, each with a weighted biome and
    /// no two zones sharing one. Enough seeds to give a region three or four distinct stretches, few enough
    /// that each is several areas wide.
    /// </summary>
    private static List<Zone> SeedZones(RegionGraph graph, Pcg32 rng)
    {
        var cells = graph.Areas.Select(a => a.Cell).ToList();
        var minCol = cells.Min(c => c.Col);
        var maxCol = cells.Max(c => c.Col);
        var minRow = cells.Min(c => c.Row);
        var maxRow = cells.Max(c => c.Row);

        var count = Math.Clamp(graph.Areas.Count / 6, 3, 6);
        var zones = new List<Zone>();
        var used = new HashSet<GridCell>();
        var chosen = new List<Biome>();

        for (var i = 0; i < count; i++)
        {
            GridCell seed = default;
            for (var attempt = 0; attempt < 24; attempt++)
            {
                seed = new GridCell(
                    rng.NextIntInclusive(minCol, maxCol),
                    rng.NextIntInclusive(minRow, maxRow));
                if (used.Add(seed)) break;
            }

            // Force some variety: once a biome is used, prefer one that is not until each has had a turn.
            var options = ZoneBiomes.Where(z => !chosen.Contains(z.Biome)).ToArray();
            if (options.Length == 0) options = ZoneBiomes;
            var picked = WeightedPick(options, rng);
            chosen.Add(picked);
            zones.Add(new Zone(seed, picked));
        }

        return zones;
    }

    private static Biome WeightedPick((Biome Biome, double Weight)[] options, Pcg32 rng)
    {
        var total = options.Sum(o => o.Weight);
        var roll = rng.NextDouble() * total;
        foreach (var option in options)
        {
            roll -= option.Weight;
            if (roll <= 0) return option.Biome;
        }
        return options[^1].Biome;
    }

    /// <summary>The biome of the closest zone seed; ties break toward the earlier seed so this is total.</summary>
    private static Biome NearestZone(GridCell cell, List<Zone> zones)
    {
        var best = zones[0];
        var bestDistance = cell.ManhattanTo(best.Seed);
        foreach (var zone in zones.Skip(1))
        {
            var distance = cell.ManhattanTo(zone.Seed);
            if (distance >= bestDistance) continue;
            best = zone;
            bestDistance = distance;
        }
        return best.Biome;
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
