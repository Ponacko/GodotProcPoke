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

    public static EncounterPlan Generate(
        RegionGraph graph, BiomeMap biomes, GatingPlan gating, DexPlan dex, RegionIdentity identity,
        RngStreams streams)
    {
        // streams is threaded through for 6b's slot-filling draws ("encounters"); 6a draws nothing.
        _ = dex;
        _ = streams;

        var anchors = GatingGenerator.OffSpineAnchors(graph);
        var gymPathIndices = identity.GymTypes.Keys.Select(id => graph[id].PathIndex).OrderBy(p => p).ToList();
        var waterGated = gating.BiomeRequirements.Where(r => WaterTags.Contains(r.Tag))
            .Select(r => r.AreaId).ToHashSet();

        var byArea = new Dictionary<int, AreaEncounters>();
        foreach (var area in graph.Areas)
        {
            if (NoWild.Contains(area.Archetype)) continue; // towns/League/hideouts hold no wild encounters

            var baseLevel = LevelCurve.WildLevel(area, graph, biomes.Of(area.Id), gymPathIndices, anchors);
            var tables = new List<EncounterTable>();

            if (LandArchetypes.Contains(area.Archetype))
                tables.Add(new EncounterTable(EncounterMethod.Land, []));

            if (biomes.Of(area.Id) == Biome.Water || waterGated.Contains(area.Id))
            {
                tables.Add(new EncounterTable(EncounterMethod.Surf, []));
                tables.Add(new EncounterTable(EncounterMethod.Fishing, []));
            }

            byArea[area.Id] = new AreaEncounters(area.Id, tables, Overlay: null, BaseLevel: baseLevel);
        }

        return new EncounterPlan { ByArea = byArea };
    }
}
