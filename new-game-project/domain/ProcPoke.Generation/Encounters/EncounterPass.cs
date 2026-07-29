using ProcPoke.Data;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Identity;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Encounters;

/// <summary>
/// Per-area encounter tables (GDD §6.3), Gen 5 slot model. Ticket 6a builds the table SHAPES — correct
/// method per area and correct base level — with empty slots; ticket 6b fills the slots from the dex and
/// adds Special Encounter Overlays, drawing from the <c>"encounters"</c> stream.
/// </summary>
public static class EncounterPass
{
    // Land tables on these archetypes (StartTown/Town/League/VillainHideout get no wild tables).
    private static readonly HashSet<AreaArchetype> LandArchetypes =
    [
        AreaArchetype.Route, AreaArchetype.Forest, AreaArchetype.StandardCave,
        AreaArchetype.MountainPath, AreaArchetype.DeepCave, AreaArchetype.VictoryRoad, AreaArchetype.Tower,
    ];

    private static readonly HashSet<AreaArchetype> NoWild =
        [AreaArchetype.StartTown, AreaArchetype.Town, AreaArchetype.League, AreaArchetype.VillainHideout];

    /// <summary>Terrain tags that make an area a water body (they map to <see cref="Biome.Water"/>).</summary>
    private static readonly HashSet<string> WaterTags = ["water", "deep-water", "sea"];

    private sealed record Candidate(int SpeciesId, int FinalBst);

    private sealed record RarityTiers(
        IReadOnlyList<Candidate> Common,
        IReadOnlyList<Candidate> Uncommon,
        IReadOnlyList<Candidate> Rare,
        IReadOnlyList<Candidate> VeryRare)
    {
        public IReadOnlyList<Candidate> CommonAndUncommon => Common.Concat(Uncommon).ToList();
        public IReadOnlyList<Candidate> RareAndVeryRare => Rare.Concat(VeryRare).ToList();
    }

    public static EncounterPlan Generate(
        RegionGraph graph, BiomeMap biomes, GatingPlan gating, DexPlan dex, RegionIdentity identity,
        GenerationSettings settings, GameData data, RngStreams streams)
    {
        var rng = streams.Stream("encounters");

        var anchors = GatingGenerator.OffSpineAnchors(graph);
        var gymPathIndices = identity.GymTypes.Keys.Select(id => graph[id].PathIndex).OrderBy(p => p).ToList();
        var waterGated = gating.BiomeRequirements.Where(r => WaterTags.Contains(r.Tag))
            .Select(r => r.AreaId).ToHashSet();
        var availabilityOrder = AvailabilityOrder.Of(graph, anchors);
        var availabilityIndex = availabilityOrder
            .Select((id, index) => (id, index))
            .ToDictionary(x => x.id, x => x.index);
        var familyFinalBst = EvolutionFamilies.Build(data, settings.RosterCap)
            .SelectMany(f => f.Members.Select(id => (id, f.FinalBst)))
            .ToDictionary(x => x.id, x => x.FinalBst);
        var wildDex = dex.Entries
            .Where(e => data.Species.ContainsKey(e.SpeciesId))
            .Where(e => !data.Species[e.SpeciesId].IsLegendary && !data.Species[e.SpeciesId].IsMythical)
            .Where(e => !dex.FossilFamilySpecies.Contains(e.SpeciesId))
            .Select(e => new Candidate(e.SpeciesId,
                familyFinalBst.TryGetValue(e.SpeciesId, out var bst) ? bst : StarterSelector.Bst(data.Species[e.SpeciesId])))
            .ToList();

        var byArea = new Dictionary<int, AreaEncounters>();
        foreach (var areaId in availabilityOrder)
        {
            var area = graph[areaId];
            if (NoWild.Contains(area.Archetype)) continue; // towns/League/hideouts hold no wild encounters

            var baseLevel = LevelCurve.WildLevel(area, graph, biomes.Of(area.Id), gymPathIndices, anchors);
            var tables = new List<EncounterTable>();
            RarityTiers? landTiers = null;

            if (LandArchetypes.Contains(area.Archetype))
            {
                landTiers = BuildTiers(PoolFor(area, biomes, graph, wildDex, data, EncounterMethod.Land));
                tables.Add(FillTable(EncounterMethod.Land, SlotLayouts.Land, landTiers, baseLevel, 0, 4, rng));
            }

            if (biomes.Of(area.Id) == Biome.Water || waterGated.Contains(area.Id))
            {
                var waterTiers = BuildTiers(PoolFor(area, biomes, graph, wildDex, data, EncounterMethod.Surf));
                tables.Add(FillTable(EncounterMethod.Surf, SlotLayouts.Surf, waterTiers, baseLevel, 0, 3, rng));
                tables.Add(FillTable(EncounterMethod.Fishing, SlotLayouts.Fishing, waterTiers, baseLevel, 0, 3, rng));
            }

            EncounterTable? overlay = null;
            if (landTiers is not null && area.Archetype is AreaArchetype.Route or AreaArchetype.Forest
                && AvailabilityFraction(availabilityIndex[area.Id], availabilityOrder.Count) >= 0.5)
            {
                overlay = FillTable(EncounterMethod.Land, SlotLayouts.Land, landTiers, baseLevel, 5, 0, rng,
                    landTiers.RareAndVeryRare);
                overlay = overlay with { HiddenAbilityChance = 0.5 };
            }

            byArea[area.Id] = new AreaEncounters(area.Id, tables, overlay, baseLevel);
        }

        return new EncounterPlan { ByArea = byArea };
    }

    private static IReadOnlyList<Candidate> PoolFor(Area area, BiomeMap biomes, RegionGraph graph,
        IReadOnlyList<Candidate> wildDex, GameData data, EncounterMethod method)
    {
        IReadOnlySet<PokeType> affinity = method is EncounterMethod.Surf or EncounterMethod.Fishing
            ? new HashSet<PokeType> { PokeType.Water }
            : AffinityFor(area, biomes, graph);

        var pool = wildDex.Where(c => data.Species[c.SpeciesId].Types.Any(affinity.Contains)).ToList();
        if (pool.Count >= 4) return pool;

        var normal = wildDex.Where(c => data.Species[c.SpeciesId].Types.Contains(PokeType.Normal)).ToList();
        if (normal.Count >= 4) return normal;
        if (wildDex.Count >= 4) return wildDex;
        throw new InvalidOperationException($"encounter pool for area {area.Id} has fewer than four species");

    }

    private static IReadOnlySet<PokeType> AffinityFor(Area area, BiomeMap biomes, RegionGraph graph)
    {
        if (area.Archetype == AreaArchetype.Tower)
            return new HashSet<PokeType> { PokeType.Ghost, PokeType.Psychic, PokeType.Normal };

        var biome = biomes.Of(area.Id);
        if (biome != Biome.Urban)
            return BiomeTypeAffinity.Table[biome].ToHashSet();

        var neighbours = graph.Neighbors(area.Id)
            .Select(n => biomes.Of(n.Id))
            .Where(b => b != Biome.Urban)
            .SelectMany(b => BiomeTypeAffinity.Table[b])
            .ToHashSet();
        return neighbours.Count > 0
            ? neighbours
            : BiomeTypeAffinity.Table[Biome.Grassland].ToHashSet();
    }

    private static RarityTiers BuildTiers(IReadOnlyList<Candidate> pool)
    {
        var ordered = pool.OrderBy(c => c.FinalBst).ThenBy(c => c.SpeciesId).ToList();
        var weights = new[] { 0.4, 0.3, 0.2, 0.1 };
        var counts = weights.Select(w => Math.Max(1, (int)Math.Floor(ordered.Count * w))).ToArray();
        while (counts.Sum() < ordered.Count)
        {
            var next = Enumerable.Range(0, counts.Length)
                .OrderByDescending(i => ordered.Count * weights[i] - counts[i])
                .ThenBy(i => i)
                .First();
            counts[next]++;
        }

        // The caller guarantees a pool of at least four. If rounding the mandatory one-per-tier minimum
        // overshoots, trim from the largest tiers first while retaining that minimum.
        while (counts.Sum() > ordered.Count)
        {
            var trim = Enumerable.Range(0, counts.Length)
                .Where(i => counts[i] > 1)
                .OrderByDescending(i => counts[i] - ordered.Count * weights[i])
                .ThenByDescending(i => i)
                .First();
            counts[trim]--;
        }

        var offset = 0;
        IReadOnlyList<Candidate> Take(int count)
        {
            var result = ordered.Skip(offset).Take(count).ToList();
            offset += count;
            return result;
        }
        return new RarityTiers(Take(counts[0]), Take(counts[1]), Take(counts[2]), Take(counts[3]));
    }

    private static EncounterTable FillTable(EncounterMethod method, IReadOnlyList<int> layout,
        RarityTiers tiers, int wildLevel, int levelOffset, int minimumDistinct, Pcg32 rng,
        IReadOnlyList<Candidate>? overridePool = null)
    {
        var slots = layout.Select((percent, index) =>
        {
            var source = overridePool ?? SourceFor(method, index, tiers);
            var candidate = rng.Pick(source);
            return new EncounterSlot(percent, candidate.SpeciesId,
                Math.Max(2, wildLevel - 2 + levelOffset), wildLevel + 2 + levelOffset);
        }).ToList();

        while (slots.Select(s => s.SpeciesId).Distinct().Count() < minimumDistinct)
        {
            var replaced = false;
            for (var index = slots.Count - 1; index >= 0 && !replaced; index--)
            {
                var source = overridePool ?? SourceFor(method, index, tiers);
                var otherSpecies = slots.Where((_, i) => i != index).Select(s => s.SpeciesId).ToHashSet();
                var alternatives = source.Where(c => !otherSpecies.Contains(c.SpeciesId)).ToList();
                if (alternatives.Count == 0) continue;
                var candidate = rng.Pick(alternatives);
                slots[index] = slots[index] with { SpeciesId = candidate.SpeciesId };
                replaced = true;
            }
            if (!replaced)
                throw new InvalidOperationException($"could not satisfy encounter distinctness for {method}");
        }

        return new EncounterTable(method, slots);
    }

    private static IReadOnlyList<Candidate> SourceFor(EncounterMethod method, int index, RarityTiers tiers)
        => index < (method == EncounterMethod.Land ? 6 : 2)
            ? tiers.CommonAndUncommon
            : index < (method == EncounterMethod.Land ? 10 : 4)
                ? tiers.Rare
                : tiers.VeryRare;

    private static double AvailabilityFraction(int index, int count) => count <= 1 ? 1 : index / (count - 1.0);
}
