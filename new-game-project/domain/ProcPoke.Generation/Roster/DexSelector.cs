using ProcPoke.Data;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Roster;

/// <summary>
/// Regional dex selection &amp; numbering (GDD §5.1/§5.2). Fills exactly <see cref="GenerationSettings.DexSize"/>
/// slots: #1–9 are the three starter lines, #10.. are whole evolution families seated in first-availability
/// order, each area drawing families toward a rising base-stat target with a strong bonus for covering a
/// type not yet in the dex. Two fossil families are reserved for the fossil area. Draws only from the
/// <c>"dex"</c> stream.
///
/// Deliberate deviation from §5.2: towns, the League, and villain hideouts hold no wild encounters in our
/// model, so they seat no dex species — only routes, forests, caves, mountains, and towers do.
/// </summary>
public static class DexSelector
{
    private const int TypeCoverageBonus = 200;
    private const double MinTarget = 250, TargetSpan = 350;

    private static readonly IReadOnlySet<AreaArchetype> NoWild = new HashSet<AreaArchetype>
    {
        AreaArchetype.StartTown, AreaArchetype.Town, AreaArchetype.League, AreaArchetype.VillainHideout,
    };

    public static DexPlan Generate(RegionGraph graph, BiomeMap biomes, StarterPlan starters,
        GameData data, GenerationSettings settings, RngStreams streams)
    {
        var rng = streams.Stream("dex");

        // 1. Candidate families: in-cap families, minus legendaries, minus the three starter families.
        var starterSpecies = starters.Corners.SelectMany(c => c.LineSpeciesIds).ToHashSet();
        var families = EvolutionFamilies.Build(data, settings.RosterCap)
            .Where(f => !f.IsLegendary)
            .Where(f => !f.Members.Any(starterSpecies.Contains))
            .ToList();

        // 2. Wild areas in first-availability order (towns/League/hideouts excluded — see the class remark).
        var availabilityOrder = AvailabilityOrder.Of(graph, GatingGenerator.OffSpineAnchors(graph));
        var wildAreas = availabilityOrder.Where(id => !NoWild.Contains(graph[id].Archetype)).ToList();
        var areaCount = wildAreas.Count;

        // 3. Fossil area, then 4. reserve two distinct fossil families (≥3 exist at every cap).
        var fossilAreaId = ChooseFossilArea(graph, biomes, availabilityOrder);
        var fossilPool = families.Where(f => f.IsFossil).ToList();
        var firstFossil = rng.Pick(fossilPool);
        var secondFossil = rng.Pick(fossilPool.Where(f => f != firstFossil).ToList());
        var reservedFossils = new List<EvolutionFamily> { firstFossil, secondFossil };

        // 5. Budget and fair-share quotas (floor differences, so they sum to B exactly). The budget is
        //    clamped to the candidate pool so an infeasible knob combo (e.g. DexSize 150 at RosterCap 1,
        //    where too few species exist) yields a smaller whole-family dex instead of failing — at every
        //    feasible setting the pool dwarfs the budget, so this is a no-op there.
        var starterCount = starterSpecies.Count; // the three starter lines occupy #1..#starterCount
        var budget = Math.Min(settings.DexSize - starterCount, families.Sum(f => f.Members.Count));
        var quota = new int[areaCount];
        for (var k = 0; k < areaCount; k++)
            quota[k] = (int)((long)budget * (k + 1) / areaCount) - (int)((long)budget * k / areaCount);

        // 6. Assignment. Fossils are pre-placed at the fossil area so the budget can never run out before
        //    they are seated — they always make the dex — while still numbering first in that area's block.
        var byArea = new Dictionary<int, List<EvolutionFamily>>();
        foreach (var id in wildAreas) byArea[id] = [];
        byArea.TryAdd(fossilAreaId, []);

        // Reference identity is fine here: every family instance comes from the single Build call above and
        // is never re-created, so the record's list-reference equality never conflates two distinct families.
        var assigned = new HashSet<EvolutionFamily>();
        var typeCounts = new int[Enum.GetValues<PokeType>().Length];
        var totalAssigned = 0;

        void Assign(int areaId, EvolutionFamily family)
        {
            byArea[areaId].Add(family);
            assigned.Add(family);
            totalAssigned += family.Members.Count;
            foreach (var member in family.Members)
                foreach (var type in data.Species[member].Types)
                    typeCounts[(int)type]++;
        }

        // Fossils fit trivially — two families of ≤2 members (≤4 species) against a budget ≥ DexSize(16)−9,
        // so pre-placing them can never overshoot the budget.
        foreach (var fossil in reservedFossils) Assign(fossilAreaId, fossil);

        for (var k = 0; k < areaCount; k++)
        {
            var areaId = wildAreas[k];
            var target = MinTarget + (areaCount == 1 ? 1.0 : k / (areaCount - 1.0)) * TargetSpan;
            var seated = byArea[areaId].Sum(f => f.Members.Count); // fossils pre-placed here already count

            while (seated < quota[k] && totalAssigned < budget)
            {
                var remaining = budget - totalAssigned;
                var candidates = families
                    .Where(f => !assigned.Contains(f) && f.Members.Count <= remaining)
                    .ToList();
                // With the budget clamped to the pool this cannot happen at any feasible setting; if a
                // degenerate combo leaves no fitting family, stop cleanly (a slightly smaller dex) rather
                // than break a family or crash.
                if (candidates.Count == 0) break;

                double Score(EvolutionFamily f) => Math.Abs(f.FinalBst - target)
                    - (f.Types.Any(t => typeCounts[(int)t] == 0) ? TypeCoverageBonus : 0);

                // Lower score wins (closest to the area's BST target, minus the new-type bonus); ties broken
                // by rng.Pick. Score once per candidate — it's pure over unchanged operands.
                var scored = candidates.Select(f => (Family: f, Score: Score(f))).ToList();
                var best = scored.Min(s => s.Score);
                var chosen = rng.Pick(scored.Where(s => s.Score == best).Select(s => s.Family).ToList());

                Assign(areaId, chosen);
                seated += chosen.Members.Count;
            }
        }

        // 7. Number: #1–9 starter lines (base, mid, final per corner), then families in availability order.
        var entries = new List<DexEntry>();
        var number = 1;
        foreach (var corner in starters.Corners)
            foreach (var speciesId in corner.LineSpeciesIds)
                entries.Add(new DexEntry(number++, speciesId));

        var speciesByArea = new Dictionary<int, IReadOnlyList<int>>
        {
            [graph.Areas.First(a => a.Archetype == AreaArchetype.StartTown).Id] =
                starters.Corners.SelectMany(c => c.LineSpeciesIds).ToList(),
        };
        foreach (var areaId in availabilityOrder.Where(byArea.ContainsKey))
        {
            var here = new List<int>();
            foreach (var member in byArea[areaId].SelectMany(f => f.Members))
            {
                entries.Add(new DexEntry(number++, member));
                here.Add(member);
            }
            if (here.Count > 0) speciesByArea[areaId] = here;
        }

        return new DexPlan
        {
            Entries = entries,
            FossilAreaId = fossilAreaId,
            FossilFamilySpecies = reservedFossils.SelectMany(f => f.Members).ToList(),
            SpeciesByArea = speciesByArea,
        };
    }

    /// <summary>First availability-order area that can host fossils: a deep cave, else a standard cave, else
    /// a cave/mountain-biome area, else any dungeon (assert one exists — every region has a dungeon).</summary>
    private static int ChooseFossilArea(RegionGraph graph, BiomeMap biomes, IReadOnlyList<int> availabilityOrder)
    {
        int? FirstWhere(Func<int, bool> predicate)
        {
            foreach (var id in availabilityOrder)
                if (predicate(id)) return id;
            return null;
        }

        return FirstWhere(id => graph[id].Archetype == AreaArchetype.DeepCave)
            ?? FirstWhere(id => graph[id].Archetype == AreaArchetype.StandardCave)
            ?? FirstWhere(id => biomes.Of(id) is Biome.Cave or Biome.Mountain)
            ?? FirstWhere(id => graph[id].IsDungeon)
            ?? throw new InvalidOperationException(
                "no cave/mountain/dungeon area to host fossils — topology invariant broken (generator bug).");
    }
}
