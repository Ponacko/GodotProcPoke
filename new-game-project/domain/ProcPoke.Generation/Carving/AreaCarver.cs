using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Dispatches each area to its archetype carver (ADR-0001: one carver per archetype). Archetypes without
/// a bespoke MVP carver yet reuse the nearest fit — mountains/interiors carve as caves, while the League
/// has its dedicated E4 gauntlet layout.
/// </summary>
public static class AreaCarver
{
    public static CarvedArea Carve(
        Area area, Biome biome, Pcg32 rng, OpeningPlan openings, EdgeSide? spineExit = null)
        => area.Archetype switch
    {
        AreaArchetype.Route => RouteCarver.Carve(area, biome, rng, openings.EdgesOf(area.Id), spineExit),
        AreaArchetype.Forest => ForestCarver.Carve(area, biome, rng, openings.EdgesOf(area.Id), spineExit),

        AreaArchetype.StartTown or AreaArchetype.Town
            => TownCarver.Carve(area, biome, rng, openings.EdgesOf(area.Id)),

        AreaArchetype.League
            => LeagueCarver.Carve(area, biome, rng, openings.EdgesOf(area.Id)),

        AreaArchetype.StandardCave or AreaArchetype.MountainPath or AreaArchetype.VictoryRoad
            => CaveCarver.Carve(area, biome, rng, openings.EdgesOf(area.Id), transit: area.OnCriticalPath, spineExit),

        AreaArchetype.VillainHideout
            => HideoutCarver.Carve(area, biome, rng, openings.EdgesOf(area.Id)),

        AreaArchetype.Tower
            => TowerCarver.Carve(area, biome, rng, openings.EdgesOf(area.Id)),

        // Destination dungeons: single-entrance enclosed layouts.
        AreaArchetype.DeepCave
            => CaveCarver.Carve(area, biome, rng, openings.EdgesOf(area.Id), transit: false, spineExit),

        _ => RouteCarver.Carve(area, biome, rng, openings.EdgesOf(area.Id), spineExit),
    };

    /// <summary>
    /// Carves the area, then — if a gate blocks its spine exit — stamps that gate onto the tiles.
    /// <paramref name="exitSide"/> is the border the exit sits on, which depends on where the spine goes
    /// next; a gate with no exit side is left uncarved, as a Warp exit already was.
    /// </summary>
    public static CarvedArea Carve(
        Area area, Biome biome, Pcg32 rng, OpeningPlan openings, Gate? gateOnExit, EdgeSide? exitSide)
    {
        var carved = Carve(area, biome, rng, openings, exitSide);
        return gateOnExit is null || exitSide is null
            ? carved
            : GateCarver.Apply(carved, gateOnExit, exitSide.Value);
    }

    /// <summary>The gate blocking this area's spine exit, or null — off-spine areas never hold a spine gate.</summary>
    public static Gate? GateOnExitOf(Area area, GatingPlan gating)
        => area.OnCriticalPath ? gating.GateBlockingExitOf(area.PathIndex) : null;
}
