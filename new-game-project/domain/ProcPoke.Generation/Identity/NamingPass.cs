using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Identity;

/// <summary>
/// The naming pass (GDD §8): draws one motif per region, then a name for every city and dungeon, blended
/// from <see cref="NameParts"/>. Routes keep the mainline <c>Route N</c> numbering in critical-path order.
/// A draw that matches the baked <c>name_blocklist.json</c> or collides with an earlier name in this
/// region is rejected and redrawn.
/// </summary>
public static class NamingPass
{
    private const int MaxAttempts = 500;

    public static RegionNames Generate(RegionGraph graph, IReadOnlyList<string> nameBlocklist, RngStreams streams)
    {
        var rng = streams.Stream("names");
        var motif = rng.Pick(Enum.GetValues<NamingMotif>());
        var blocked = new HashSet<string>(nameBlocklist);
        var used = new HashSet<string>();
        var names = new Dictionary<int, string>();

        var routesByPathOrder = graph.Areas
            .Where(a => a.Archetype == AreaArchetype.Route)
            .OrderBy(a => a.PathIndex)
            .ToList();
        for (var i = 0; i < routesByPathOrder.Count; i++)
        {
            var routeName = $"Route {i + 1}";
            names[routesByPathOrder[i].Id] = routeName;
            used.Add(routeName);
        }

        foreach (var area in graph.Areas.OrderBy(a => a.Id))
        {
            if (names.ContainsKey(area.Id)) continue; // Route, already numbered above
            names[area.Id] = area.IsPlain
                ? DrawCityName(rng, motif, used, blocked)
                : DrawDungeonName(rng, motif, area.Archetype, used, blocked);
        }

        return new RegionNames { Motif = motif, ByArea = names };
    }

    private static string DrawCityName(Pcg32 rng, NamingMotif motif, HashSet<string> used, HashSet<string> blocked)
    {
        var (roots, _) = NameParts.Pools[motif];
        return Draw(motif, used, blocked, () => rng.Pick(roots) + rng.Pick(NameParts.SettlementSuffixes));
    }

    private static string DrawDungeonName(Pcg32 rng, NamingMotif motif, AreaArchetype archetype,
        HashSet<string> used, HashSet<string> blocked)
    {
        var (roots, blends) = NameParts.Pools[motif];
        var form = NameParts.DungeonForm(archetype);
        return Draw(motif, used, blocked, () => $"{rng.Pick(roots)}{rng.Pick(blends)} {form}");
    }

    /// <summary>Draws from <paramref name="candidate"/> until one clears the blocklist and hasn't been
    /// used yet in this region, redrawing on either rejection.</summary>
    private static string Draw(NamingMotif motif, HashSet<string> used, HashSet<string> blocked,
        Func<string> candidate)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var name = candidate();
            if (blocked.Contains(name)) continue;
            if (used.Add(name)) return name;
        }
        throw new InvalidOperationException($"exhausted {MaxAttempts} draws for a free name (motif {motif}).");
    }
}
