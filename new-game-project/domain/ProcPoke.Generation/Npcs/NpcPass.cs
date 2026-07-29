using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Identity;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Npcs;

/// <summary>
/// Builds the graph-level NPC plan (§8). Posts are intentionally independent of tile geometry; ticket 8b
/// realizes them on carved maps. The pass owns only the <c>npcs</c> RNG stream so adding or moving a post
/// cannot perturb topology, gating, or the other population passes.
/// </summary>
public static class NpcPass
{
    private static readonly IReadOnlyList<string> HintTemplates =
    [
        "I hear {key} waits somewhere in {area}.",
        "The way forward needs {key}; look for it in {area}.",
        "A traveler told me that {area} holds the {key} you need.",
    ];

    private static readonly IReadOnlyList<string> FlavorTemplates =
    [
        "The view from {area} is best just before dawn.",
        "Travelers say the road beyond {area} changes with the weather.",
        "A local in {area} keeps a sketch of the region's distant paths.",
        "The shops in {area} always have a story about {neighbor}.",
        "Rest easy in {area}; every journey has a memorable first step.",
        "Someone in {area} swears the shortest road to {neighbor} is never the safest.",
        "The bells of {area} carry farther than the maps suggest.",
        "Old friends still meet beneath the lights of {area}.",
    ];

    public static NpcPlan Generate(
        RegionGraph graph, GatingPlan gating, RegionNames names, RegionIdentity identity, RngStreams streams)
    {
        var rng = streams.Stream("npcs");
        var posts = graph.Areas.ToDictionary(a => a.Id, _ => new List<NpcPost>());

        AddHints(gating, names, rng, posts);
        AddGymFurniture(graph, identity, names, posts);
        AddRouteSigns(graph, names, posts);
        AddTownFlavor(graph, names, rng, posts);

        var byArea = posts.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<NpcPost>)pair.Value.ToArray());
        return new NpcPlan { ByArea = byArea };
    }

    private static void AddHints(
        GatingPlan gating, RegionNames names, Pcg32 rng,
        IReadOnlyDictionary<int, List<NpcPost>> posts)
    {
        foreach (var gate in gating.Gates.OrderBy(g => g.Id))
        {
            // GatingGenerator places this area strictly before the gate. Keeping the placement on that
            // already-validated side means the hint remains reachable even when a gate has an HM badge
            // prerequisite or its key was placed on an off-spine branch.
            var text = rng.Pick(HintTemplates)
                .Replace("{key}", gate.KeyName, StringComparison.Ordinal)
                .Replace("{area}", names.Of(gate.KeyAreaId), StringComparison.Ordinal);
            Add(posts, gate.HintAreaId, new NpcPost(NpcKind.Hint, text));
        }
    }

    private static void AddGymFurniture(
        RegionGraph graph, RegionIdentity identity, RegionNames names,
        IReadOnlyDictionary<int, List<NpcPost>> posts)
    {
        foreach (var city in identity.GymTypes.Keys
                     .Select(id => graph[id])
                     .OrderBy(a => a.PathIndex))
        {
            var type = identity.GymTypes[city.Id];
            Add(posts, city.Id, new NpcPost(
                NpcKind.GymGuide,
                $"{names.Of(city.Id)}'s Leader runs a {type} gym!"));

            var hideout = Nearest(graph, city.Id, a => a.Archetype == AreaArchetype.VillainHideout);
            if (hideout is not null)
            {
                Add(posts, city.Id, new NpcPost(
                    NpcKind.CenterGossip,
                    $"I hear the villain hideout near here is called {names.Of(hideout.Id)}."));
            }
        }
    }

    private static void AddRouteSigns(
        RegionGraph graph, RegionNames names, IReadOnlyDictionary<int, List<NpcPost>> posts)
    {
        var anchors = GatingGenerator.OffSpineAnchors(graph);
        var routes = graph.Areas.Where(a => a.Archetype == AreaArchetype.Route)
            .OrderBy(a => a.OnCriticalPath ? a.PathIndex : anchors.GetValueOrDefault(a.Id, int.MaxValue))
            .ThenBy(a => a.Id);
        foreach (var route in routes)
        {
            var position = route.OnCriticalPath
                ? route.PathIndex
                : anchors.GetValueOrDefault(route.Id, -1);
            var nextTown = graph.CriticalPath
                .Where(a => a.PathIndex > position
                            && a.Archetype is AreaArchetype.StartTown or AreaArchetype.Town)
                .OrderBy(a => a.PathIndex)
                .FirstOrDefault();
            var destination = nextTown ?? graph[graph.LeagueAreaId];
            Add(posts, route.Id, new NpcPost(
                NpcKind.Sign,
                $"{names.Of(route.Id)} — onward to {names.Of(destination.Id)}."));
        }
    }

    private static void AddTownFlavor(
        RegionGraph graph, RegionNames names, Pcg32 rng,
        IReadOnlyDictionary<int, List<NpcPost>> posts)
    {
        foreach (var town in graph.Areas.Where(a => a.Archetype is AreaArchetype.StartTown or AreaArchetype.Town)
                     .OrderBy(a => a.OnCriticalPath ? a.PathIndex : int.MaxValue)
                     .ThenBy(a => a.Id))
        {
            var count = rng.Chance(0.5) ? 2 : 1;
            var neighbors = graph.Neighbors(town.Id)
                .OrderBy(a => a.PathIndex < 0 ? int.MaxValue : a.PathIndex)
                .ThenBy(a => a.Id)
                .Select(a => names.Of(a.Id))
                .ToList();
            if (neighbors.Count == 0) neighbors.Add(names.Of(town.Id));

            for (var i = 0; i < count; i++)
            {
                var text = rng.Pick(FlavorTemplates)
                    .Replace("{area}", names.Of(town.Id), StringComparison.Ordinal)
                    .Replace("{neighbor}", neighbors[i % neighbors.Count], StringComparison.Ordinal);
                Add(posts, town.Id, new NpcPost(NpcKind.Flavor, text));
            }
        }
    }

    private static void Add(
        IReadOnlyDictionary<int, List<NpcPost>> posts, int areaId, NpcPost post)
    {
        if (!posts.TryGetValue(areaId, out var areaPosts))
            throw new InvalidOperationException($"NPC post target area {areaId} is not in the region graph.");
        areaPosts.Add(post);
    }

    private static Area? Nearest(RegionGraph graph, int startId, Func<Area, bool> predicate)
    {
        var distance = new Dictionary<int, int> { [startId] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(startId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in graph.Neighbors(current).OrderBy(a => a.Id))
            {
                if (distance.ContainsKey(next.Id)) continue;
                distance[next.Id] = distance[current] + 1;
                queue.Enqueue(next.Id);
            }
        }

        return graph.Areas.Where(predicate)
            .Where(a => distance.ContainsKey(a.Id))
            .OrderBy(a => distance[a.Id])
            .ThenBy(a => a.Id)
            .FirstOrDefault();
    }
}
