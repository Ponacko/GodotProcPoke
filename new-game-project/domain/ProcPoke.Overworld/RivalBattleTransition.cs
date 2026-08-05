using ProcPoke.Battle;
using ProcPoke.Generation.Identity;
using ProcPoke.Generation.Rivals;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Overworld;

/// <summary>
/// Immutable hand-off from the overworld to a scripted rival battle. <see cref="RivalPlan"/> has no map
/// position at all — it is keyed purely by (player starter corner, beat) — so <see cref="RivalBeatTriggers"/>
/// is what actually decides where each beat fires.
/// </summary>
public sealed record RivalBattleTransitionRequest(
    RivalBeat Beat,
    int PlayerCornerIndex,
    PlayerState ReturnState,
    IReadOnlyList<TrainerRosterMember> Roster);

/// <summary>
/// Derives the one critical-path area each scripted rival beat fires in (GDD §7.2: "after the starter
/// choice, on an early route, at the midpoint city, and before the Elite Four"), from the region's own
/// gym-city and League structure. <see cref="RivalBeat.PostStarter"/> is handled as dialogue in the
/// new-game flow, not a map trigger, so it is excluded from <see cref="MapTriggeredBeats"/>.
/// </summary>
public static class RivalBeatTriggers
{
    public static readonly IReadOnlyList<RivalBeat> MapTriggeredBeats =
        [RivalBeat.EarlyRoute, RivalBeat.Midpoint, RivalBeat.PreLeague];

    public static int AreaFor(RivalBeat beat, RegionGraph graph, RegionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(identity);

        var criticalPath = graph.CriticalPath;
        var gymCities = criticalPath
            .Where(area => identity.GymTypes.ContainsKey(area.Id))
            .OrderBy(area => area.PathIndex)
            .ToList();
        if (gymCities.Count == 0)
            throw new InvalidOperationException("region has no gym cities to anchor a rival beat to.");

        return beat switch
        {
            // Just past the first badge — matches RivalPass.AceFor(EarlyRoute) = GymAce(0) + 2.
            RivalBeat.EarlyRoute => AreaAtPathIndex(criticalPath, gymCities[0].PathIndex + 1),
            // "The midpoint city" itself (GDD §7.2), matching RivalPass.AceFor(Midpoint) = GymAce(gymCount/2).
            RivalBeat.Midpoint => gymCities[gymCities.Count / 2].Id,
            // The last critical-path area before the League — "before the Elite Four".
            RivalBeat.PreLeague => AreaAtPathIndex(criticalPath, graph[graph.LeagueAreaId].PathIndex - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(beat), beat, "this rival beat has no map trigger."),
        };
    }

    private static int AreaAtPathIndex(IReadOnlyList<Area> criticalPath, int pathIndex)
        => criticalPath.First(area => area.PathIndex == pathIndex).Id;
}
