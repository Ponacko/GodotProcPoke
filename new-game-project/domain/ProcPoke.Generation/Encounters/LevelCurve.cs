using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Encounters;

/// <summary>
/// The §7.1 progression curve, expressed against progression fraction so any badge count works. Shared by
/// wild-encounter levels (§6.3), trainer levels (7a), and boss levels (7b) so they all ride one curve.
/// </summary>
public static class LevelCurve
{
    /// <summary>Wild level on the very first route — a starter's own level, so route one is catchable fodder
    /// rather than a wall.</summary>
    private const int FirstRouteWild = 3;

    /// <summary>Wild level once every badge is in hand, on the run to the League.</summary>
    private const int FinalRouteWild = 46;

    /// <summary>
    /// Curvature of the wild ramp. Above 1 the early game climbs slowly and the back half steepens, which is
    /// how the games pace it: single digits for the first few routes, then bigger jumps once the player has a
    /// full team and evolutions to lean on.
    /// </summary>
    private const double WildRampExponent = 1.35;

    /// <summary>Extra levels for a dungeon you go out of your way to enter, or the final gauntlet.</summary>
    private const int DungeonBump = 2;

    /// <summary>Gym leader ace level: ~14 at the first badge, ~50 at the last (GDD §7.1).
    /// <paramref name="i"/> is the 0-based gym index, <paramref name="gymCount"/> the total.</summary>
    public static int GymAce(int i, int gymCount)
        => gymCount <= 1 ? 50 : (int)Math.Round(14 + 36.0 * i / (gymCount - 1));

    /// <summary>Whether this archetype's wild levels sit <see cref="DungeonBump"/> above the base ramp.
    /// Exposed so callers reason about the same set the curve does rather than restating it.</summary>
    public static bool HasDungeonBump(AreaArchetype archetype)
        => archetype is AreaArchetype.DeepCave or AreaArchetype.Tower
            or AreaArchetype.VillainHideout or AreaArchetype.VictoryRoad;

    /// <summary>
    /// Base wild level for an area, from how many badges the player can hold by the time they reach it. The
    /// ±2 per-slot jitter is applied in 6b, not here.
    /// <para>
    /// This used to be "the next gym's ace − 5", which made the first route level 9 against a level-5 starter
    /// and had every route sitting five levels under a linearly rising target — a ramp far steeper than the
    /// games use early on. The curve is now anchored at both ends and convex in between, so the opening
    /// routes are gentle and the climb tightens as the team fills out.
    /// </para>
    /// <paramref name="graph"/> and <paramref name="biome"/> are part of the shared LevelCurve signature
    /// (7a/7b call this) though this rule needs neither.
    /// </summary>
    /// <param name="gymPathIndices">The critical-path PathIndex of each gym city, ascending (the keys of
    /// RegionIdentity.GymTypes mapped through graph[id].PathIndex, sorted).</param>
    /// <param name="offSpineAnchors">areaId → anchor PathIndex (GatingGenerator.OffSpineAnchors).</param>
    public static int WildLevel(
        Area area, RegionGraph graph, Biome biome,
        IReadOnlyList<int> gymPathIndices, IReadOnlyDictionary<int, int> offSpineAnchors)
    {
        var pos = area.OnCriticalPath ? area.PathIndex : offSpineAnchors[area.Id];

        // Badges the player can hold here: one per gym strictly before this area's progression position.
        var badges = 0;
        while (badges < gymPathIndices.Count && gymPathIndices[badges] <= pos) badges++;

        var level = WildAtBadges(badges, gymPathIndices.Count);
        if (HasDungeonBump(area.Archetype)) level += DungeonBump;
        return Math.Max(2, level);
    }

    /// <summary>The ramp itself: <paramref name="badges"/> of <paramref name="gymCount"/> earned.</summary>
    public static int WildAtBadges(int badges, int gymCount)
    {
        if (gymCount <= 0) return FinalRouteWild;
        var fraction = Math.Clamp((double)badges / gymCount, 0, 1);
        var span = FinalRouteWild - FirstRouteWild;
        return (int)Math.Round(FirstRouteWild + span * Math.Pow(fraction, WildRampExponent));
    }
}
