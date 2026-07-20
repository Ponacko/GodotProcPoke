using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Encounters;

/// <summary>
/// The §7.1 progression curve, expressed against progression fraction so any badge count works. Shared by
/// wild-encounter levels (§6.3), trainer levels (7a), and boss levels (7b) so they all ride one curve.
/// </summary>
public static class LevelCurve
{
    /// <summary>Base wild level for an area past the last gym: the run then leads to the League, so it sits
    /// at the E4-tier baseline (54) minus the standard 5 (ticket §6a).</summary>
    private const int PostLastGymWild = 49;

    /// <summary>Gym leader ace level: ~14 at the first badge, ~50 at the last (GDD §7.1).
    /// <paramref name="i"/> is the 0-based gym index, <paramref name="gymCount"/> the total.</summary>
    public static int GymAce(int i, int gymCount)
        => gymCount <= 1 ? 50 : (int)Math.Round(14 + 36.0 * i / (gymCount - 1));

    /// <summary>Base wild level for an area = (next gym's ace − 5), with +2 for destination dungeons
    /// (DeepCave/Tower/VillainHideout). The ±2 per-slot jitter is applied in 6b, not here.
    /// <paramref name="graph"/> and <paramref name="biome"/> are part of the shared LevelCurve signature
    /// (7a/7b call this) though this rule needs neither.</summary>
    /// <param name="gymPathIndices">The critical-path PathIndex of each gym city, ascending (the keys of
    /// RegionIdentity.GymTypes mapped through graph[id].PathIndex, sorted).</param>
    /// <param name="offSpineAnchors">areaId → anchor PathIndex (GatingGenerator.OffSpineAnchors).</param>
    public static int WildLevel(
        Area area, RegionGraph graph, Biome biome,
        IReadOnlyList<int> gymPathIndices, IReadOnlyDictionary<int, int> offSpineAnchors)
    {
        var pos = area.OnCriticalPath ? area.PathIndex : offSpineAnchors[area.Id];

        // The first gym strictly after this area's progression position sets the difficulty target.
        var nextGym = -1;
        for (var j = 0; j < gymPathIndices.Count; j++)
            if (gymPathIndices[j] > pos) { nextGym = j; break; }

        // Ticket §6a: gym ace − 5 up to the last gym; past it, the League-tier 49. (The blueprint skeleton
        // suggested GymAce(last) − 5; the ticket prose overrides that, keeping Victory-Road wilds above the
        // final gym's routes.)
        var level = nextGym >= 0 ? GymAce(nextGym, gymPathIndices.Count) - 5 : PostLastGymWild;

        if (area.Archetype is AreaArchetype.DeepCave or AreaArchetype.Tower or AreaArchetype.VillainHideout)
            level += 2;

        return Math.Max(2, level);
    }
}
