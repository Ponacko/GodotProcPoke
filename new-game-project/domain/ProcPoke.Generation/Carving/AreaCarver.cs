using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Dispatches each area to its archetype carver (ADR-0001: one carver per archetype). Archetypes without
/// a bespoke MVP carver yet reuse the nearest fit — mountains/interiors carve as caves, the League as a
/// settlement — a deliberate variety cut to be paid back in Phase 6.
/// </summary>
public static class AreaCarver
{
    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, OpeningPlan openings) => area.Archetype switch
    {
        AreaArchetype.Route => RouteCarver.Carve(area, biome, rng, openings.OpeningsOf(area.Id)),
        AreaArchetype.Forest => ForestCarver.Carve(area, biome, rng),

        AreaArchetype.StartTown or AreaArchetype.Town or AreaArchetype.League
            => TownCarver.Carve(area, biome, rng, openings.OpeningsOf(area.Id)),

        AreaArchetype.StandardCave or AreaArchetype.MountainPath or AreaArchetype.VictoryRoad
            => CaveCarver.Carve(area, biome, rng, transit: area.OnCriticalPath),

        // Destination dungeons: single-entrance enclosed layouts.
        AreaArchetype.DeepCave or AreaArchetype.VillainHideout or AreaArchetype.Tower
            => CaveCarver.Carve(area, biome, rng, transit: false),

        _ => RouteCarver.Carve(area, biome, rng, openings.OpeningsOf(area.Id)),
    };

    /// <summary>Carves the area, then — if a gate blocks its spine exit — stamps that gate onto the tiles.</summary>
    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, OpeningPlan openings, Gate? gateOnExit)
    {
        var carved = Carve(area, biome, rng, openings);
        return gateOnExit is null ? carved : GateCarver.Apply(carved, gateOnExit);
    }

    /// <summary>The gate blocking this area's spine exit, or null — off-spine areas never hold a spine gate.</summary>
    public static Gate? GateOnExitOf(Area area, GatingPlan gating)
        => area.OnCriticalPath ? gating.GateBlockingExitOf(area.PathIndex) : null;
}
