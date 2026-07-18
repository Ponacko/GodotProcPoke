using ProcPoke.Data;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Roster;

/// <summary>
/// Fossils &amp; legendaries (GDD §5.4/§5.5). The two fossil families are already seated in the dex (4a-2);
/// this pass emits their revival pickups plus four legendary static encounters in destination dungeons.
/// Legendaries are optional content and never touch gating or solvability. Draws from the
/// <c>"special-species"</c> stream.
/// </summary>
public static class SpecialSpeciesPass
{
    private static readonly int[] LegendaryLevels = [50, 55, 65, 70];

    public static SpecialSpecies Generate(RegionGraph graph, DexPlan dex, GameData data,
        GenerationSettings settings, RngStreams streams)
    {
        var rng = streams.Stream("special-species");
        var availabilityOrder = AvailabilityOrder.Of(graph, GatingGenerator.OffSpineAnchors(graph));

        // Fossils: the two reserved families (4a-2), each emitted as its root species at the fossil area.
        var fossilFamilies = EvolutionFamilies.Build(data, settings.RosterCap)
            .Where(f => f.Members.Any(dex.FossilFamilySpecies.Contains))
            .ToList();
        var fossils = fossilFamilies
            .Select(f => new FossilPlacement(f.Members[0], dex.FossilAreaId))
            .ToList();

        // Legendaries: four distinct non-mythical species within cap (mythicals are event-only).
        var pool = data.Species.Values
            .Where(s => s.IsLegendary && !s.IsMythical && SpeciesGeneration.Of(s.Id) <= settings.RosterCap)
            .OrderBy(s => s.Id)
            .ToList();
        rng.Shuffle(pool);
        var chosen = pool.Take(4).OrderBy(StarterSelector.Bst).ToList(); // weakest first → earliest dungeon

        var dungeons = EligibleDungeons(graph, dex, availabilityOrder);
        var legendaries = new List<LegendaryPlacement>();
        for (var i = 0; i < chosen.Count; i++)
            legendaries.Add(new LegendaryPlacement(
                SpeciesId: chosen[i].Id,
                AreaId: dungeons[i % dungeons.Count], // round-robin when fewer than four dungeons exist
                Level: LegendaryLevels[i],
                DexNumber: settings.DexSize + 1 + i));

        return new SpecialSpecies { Fossils = fossils, Legendaries = legendaries };
    }

    /// <summary>Destination dungeons that can host a legendary, in availability order: the §5.5 set is
    /// off-spine Tower/DeepCave excluding the fossil area. Broadened progressively only when a small region
    /// lacks them (any off-spine dungeon, then any dungeon at all — VictoryRoad is always one), so every
    /// legendary lands in a real dungeon. The final <c>[FossilAreaId]</c> is reached only in the degenerate
    /// case where the region's *sole* dungeon is the fossil area itself; co-locating there is then the only
    /// option, and harmless since legendaries never gate progress (§5.5).</summary>
    private static List<int> EligibleDungeons(RegionGraph graph, DexPlan dex, IReadOnlyList<int> order)
    {
        List<int> Where(Func<Area, bool> predicate) => order.Where(id => predicate(graph[id])).ToList();

        var eligible = Where(a => !a.OnCriticalPath && a.Archetype is AreaArchetype.Tower or AreaArchetype.DeepCave
                                  && a.Id != dex.FossilAreaId);
        if (eligible.Count > 0) return eligible;

        eligible = Where(a => !a.OnCriticalPath && a.IsDungeon && a.Id != dex.FossilAreaId);
        if (eligible.Count > 0) return eligible;

        eligible = Where(a => a.IsDungeon && a.Id != dex.FossilAreaId);
        return eligible.Count > 0 ? eligible : [dex.FossilAreaId];
    }
}
