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

    /// <summary>Fewest distinct species a table's pool may be narrowed to before a restriction is relaxed —
    /// the Land layout has to reach four distinct species.</summary>
    private const int MinimumPoolSpecies = 4;

    /// <summary>How many distinct species an area's land line-up holds.</summary>
    private const int RosterSize = 5;

    /// <summary>
    /// How many of those an area keeps from the last area of the same biome. The rest are newly introduced, so
    /// the species met early stay common across the whole region while every new area of a familiar biome
    /// still has something in it worth looking for (GDD §6.3 pacing).
    /// </summary>
    private const int CarriedForward = 3;

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

        var stages = EvolutionStages.Of(data, settings.RosterCap);

        // Walked in availability order, so each biome's line-up can build on the one before it.
        var lastRosterByBiome = new Dictionary<Biome, List<Candidate>>();
        var introducedByBiome = new Dictionary<Biome, HashSet<int>>();

        var byArea = new Dictionary<int, AreaEncounters>();
        foreach (var areaId in availabilityOrder)
        {
            var area = graph[areaId];
            if (NoWild.Contains(area.Archetype)) continue; // towns/League/hideouts hold no wild encounters

            var biome = biomes.Of(area.Id);
            var baseLevel = LevelCurve.WildLevel(area, graph, biome, gymPathIndices, anchors);
            var fraction = AvailabilityFraction(availabilityIndex[area.Id], availabilityOrder.Count);
            var tables = new List<EncounterTable>();
            RarityTiers? landTiers = null;

            if (LandArchetypes.Contains(area.Archetype))
            {
                var pool = EligibleByStage(
                    PoolFor(area, biomes, graph, wildDex, data, EncounterMethod.Land), stages, fraction);
                var roster = NextRoster(biome, pool, lastRosterByBiome, introducedByBiome, rng);
                landTiers = BuildTiers(roster);
                tables.Add(FillTable(EncounterMethod.Land, SlotLayouts.Land, landTiers, baseLevel, 0, 4, rng));
            }

            if (biome == Biome.Water || waterGated.Contains(area.Id))
            {
                // Water tables are not rostered — a region has few water areas, so there is no run of them to
                // introduce species across — but the stage cap applies just the same.
                var waterTiers = BuildTiers(EligibleByStage(
                    PoolFor(area, biomes, graph, wildDex, data, EncounterMethod.Surf), stages, fraction));
                tables.Add(FillTable(EncounterMethod.Surf, SlotLayouts.Surf, waterTiers, baseLevel, 0, 3, rng));
                tables.Add(FillTable(EncounterMethod.Fishing, SlotLayouts.Fishing, waterTiers, baseLevel, 0, 3, rng));
            }

            EncounterTable? overlay = null;
            if (landTiers is not null && area.Archetype is AreaArchetype.Route or AreaArchetype.Forest
                && fraction >= 0.5)
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

    /// <summary>
    /// Drops species too far up their evolution line for this point in the game: base forms only for the
    /// first third, one evolution deep by the second, anything after that. Without this a fully-evolved
    /// Pokémon could hold a slot on route one purely because its family sits early in the dex — a family is
    /// seated as a unit, so its evolutions become "available" the moment its base form does.
    /// <para>
    /// The cap lifts a stage at a time if a narrow biome pool cannot field <see cref="MinimumPoolSpecies"/>
    /// otherwise; a table that cannot be built is worse than one with an early evolution in it.
    /// </para>
    /// </summary>
    private static IReadOnlyList<Candidate> EligibleByStage(
        IReadOnlyList<Candidate> pool, IReadOnlyDictionary<int, int> stages, double fraction)
    {
        for (var cap = MaxStageAt(fraction); cap < 3; cap++)
        {
            var eligible = pool.Where(c => StageOf(stages, c.SpeciesId) <= cap).ToList();
            if (eligible.Count >= MinimumPoolSpecies) return eligible;
        }
        return pool;
    }

    private static int MaxStageAt(double fraction) => fraction < 1.0 / 3 ? 0 : fraction < 2.0 / 3 ? 1 : 2;

    private static int StageOf(IReadOnlyDictionary<int, int> stages, int speciesId)
        => stages.TryGetValue(speciesId, out var stage) ? stage : 0;

    /// <summary>
    /// This area's land line-up: most of it carried over from the previous area of the same biome, the rest
    /// species that biome has not shown before. The player therefore keeps meeting the Pokémon they met early
    /// — those become the region's common wildlife — while each new area of a familiar biome still holds one
    /// or two they have not caught yet.
    /// <para>
    /// A pool no larger than the roster is used whole; there is nothing to stagger.
    /// </para>
    /// </summary>
    private static IReadOnlyList<Candidate> NextRoster(
        Biome biome, IReadOnlyList<Candidate> pool,
        Dictionary<Biome, List<Candidate>> lastRosterByBiome,
        Dictionary<Biome, HashSet<int>> introducedByBiome,
        Pcg32 rng)
    {
        IReadOnlyList<Candidate> roster;
        if (pool.Count <= RosterSize)
        {
            roster = pool;
        }
        else
        {
            var previous = lastRosterByBiome.TryGetValue(biome, out var last) ? last : [];
            var inPool = pool.Select(c => c.SpeciesId).ToHashSet();

            var carried = previous.Where(c => inPool.Contains(c.SpeciesId)).ToList();
            rng.Shuffle(carried);
            carried = carried.Take(CarriedForward).ToList();

            var carriedIds = carried.Select(c => c.SpeciesId).ToHashSet();
            var introduced = introducedByBiome.TryGetValue(biome, out var seen) ? seen : [];
            var remaining = pool.Where(c => !carriedIds.Contains(c.SpeciesId)).ToList();

            // Unseen species first, so the new slots really are new; species this biome has shown before are
            // the backstop once it runs out of them.
            var fresh = remaining.Where(c => !introduced.Contains(c.SpeciesId)).ToList();
            var repeats = remaining.Where(c => introduced.Contains(c.SpeciesId)).ToList();
            rng.Shuffle(fresh);
            rng.Shuffle(repeats);

            roster = carried.Concat(fresh).Concat(repeats).Take(RosterSize).ToList();
        }

        lastRosterByBiome[biome] = roster.ToList();
        if (!introducedByBiome.TryGetValue(biome, out var all))
            introducedByBiome[biome] = all = [];
        all.UnionWith(roster.Select(c => c.SpeciesId));

        return roster;
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
