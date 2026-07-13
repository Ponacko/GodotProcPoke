using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Gating;

/// <summary>
/// The gates &amp; keys pass (§4.3, §4.5, ADR-0002). Computes a Gate Budget from region size, fills it from
/// the obstacle table (guaranteed classics first, then weighted draws), places gates on critical-path
/// chokepoints in timing order, and — the constructive heart — places each key in an area reachable
/// *before* its gate with a badge earnable before it. Circular locks are unrepresentable by construction.
/// </summary>
public static class GatingGenerator
{
    public static GatingPlan Generate(RegionGraph graph, GenerationSettings settings, RngStreams streams)
    {
        var rng = streams.Stream("gates");
        var path = graph.CriticalPath;
        var m = path.Count;

        // Valid gate edges: after the opening, before the Victory Road → League check.
        var validStart = 1;
        var validEnd = m - 3;
        var availableEdges = Math.Max(0, validEnd - validStart + 1);

        var spineRoutes = path.Count(a => a.Archetype == AreaArchetype.Route);
        var budget = Math.Clamp((int)Math.Round(spineRoutes * 1.1), 3, ObstacleTable.All.Count);
        budget = Math.Min(budget, availableEdges);

        var selected = Select(rng, budget);
        var anchors = OffSpineAnchors(graph);
        var usedKeyAreas = new HashSet<int>();
        var usedTerrainAreas = new HashSet<int>(); // ADR-0004: at most one terrain tag per area

        int TownsUpTo(int edge) => path.Take(edge + 1).Count(a => a.Archetype == AreaArchetype.Town);
        int FirstEdgeWithTowns(int badges)
        {
            for (var e = validStart; e <= validEnd; e++)
                if (TownsUpTo(e) >= badges) return e;
            return validEnd + 1;
        }

        var gates = new List<Gate>();
        var lastEdge = validStart - 1;

        foreach (var def in selected.OrderBy(d => d.Timing))
        {
            // Where does the timing want this gate, and how early can its badge prerequisite be met?
            var desired = Math.Clamp((int)Math.Round(def.Timing * (m - 1)), validStart, validEnd);
            var prereq = def.IsHm ? Math.Clamp((int)Math.Ceiling(def.Timing * settings.BadgeCount), 1, settings.BadgeCount) : 0;

            var minEdge = validStart;
            if (def.IsHm)
            {
                minEdge = FirstEdgeWithTowns(prereq);
                if (minEdge > validEnd)
                {
                    prereq = TownsUpTo(validEnd);
                    minEdge = prereq >= 1 ? FirstEdgeWithTowns(prereq) : validEnd + 1;
                }
                if (minEdge > validEnd) continue; // no town to gate behind — cannot host this HM
            }

            var pos = Math.Max(Math.Max(lastEdge + 1, minEdge), desired);
            if (pos > validEnd) continue; // ran out of room; obstacle is dropped (budget shrinks)
            lastEdge = pos;

            var finalPrereq = def.IsHm ? Math.Min(prereq, TownsUpTo(pos)) : (int?)null;
            var keyArea = PlaceKey(graph, rng, anchors, usedKeyAreas, pos);
            var hintArea = HintArea(path, pos);
            // Terrain (water/desert) belongs on a Route — the flexible outdoor terrain — not on a
            // transit dungeon whose biome its archetype fixes. Tag the Route nearest the gate.
            int? terrainArea = null;
            if (def.TerrainTag is not null)
            {
                terrainArea = NearestRoute(path, pos, usedTerrainAreas);
                usedTerrainAreas.Add(terrainArea.Value);
            }

            gates.Add(new Gate
            {
                Id = gates.Count,
                Obstacle = def.Class,
                KeyName = def.KeyName,
                KeyKind = def.KeyKind,
                BlockPathIndex = pos,
                TimingFraction = def.Timing,
                KeyAreaId = keyArea,
                HintAreaId = hintArea,
                BadgePrerequisite = finalPrereq,
                TerrainTag = def.TerrainTag,
                TerrainAreaId = terrainArea,
            });
        }

        var fly = PlaceFly(graph, rng, anchors, path, settings.BadgeCount);
        return new GatingPlan { Gates = gates, Fly = fly };
    }

    // ---- obstacle selection -------------------------------------------------

    private static List<ObstacleDef> Select(Pcg32 rng, int budget)
    {
        var chosen = ObstacleTable.All.Where(d => d.Guaranteed).ToList();
        var pool = ObstacleTable.All.Where(d => !d.Guaranteed).ToList();

        while (chosen.Count < budget && pool.Count > 0)
        {
            var total = pool.Sum(d => d.Weight);
            var r = rng.NextDouble() * total;
            var i = 0;
            while (i < pool.Count - 1 && (r -= pool[i].Weight) > 0) i++;
            chosen.Add(pool[i]);
            pool.RemoveAt(i);
        }
        return chosen;
    }

    // ---- key / hint / fly placement ----------------------------------------

    /// <summary>Maps each off-spine area to the critical-path index it hangs off (its anchor).</summary>
    private static Dictionary<int, int> OffSpineAnchors(RegionGraph graph)
    {
        var anchors = new Dictionary<int, int>();
        foreach (var area in graph.OffSpineAreas)
        {
            var onSpine = graph.Neighbors(area.Id).Where(n => n.OnCriticalPath).Select(n => n.PathIndex);
            if (onSpine.Any()) anchors[area.Id] = onSpine.Min();
        }
        return anchors;
    }

    private static int PlaceKey(RegionGraph graph, Pcg32 rng, Dictionary<int, int> anchors, HashSet<int> used, int pos)
    {
        // Prefer a fresh off-spine destination dungeon reachable before the gate (§4.3 rule 2).
        bool Reachable(int areaId) => anchors[areaId] <= pos && !used.Contains(areaId);
        var offSpine = anchors.Keys.Where(Reachable).ToList();

        var dungeons = offSpine.Where(id => graph[id].IsDungeon).ToList();
        var pick = dungeons.Count > 0 ? rng.Pick(dungeons)
            : offSpine.Count > 0 ? rng.Pick(offSpine)
            : (int?)null;

        if (pick is int chosen) { used.Add(chosen); return chosen; }

        // Fallback: an NPC gift in a town reachable before the gate (allow reuse only as last resort).
        var towns = graph.CriticalPath
            .Where(a => a.PathIndex <= pos && a.Archetype is AreaArchetype.Town or AreaArchetype.StartTown)
            .ToList();
        var freeTown = towns.Where(a => !used.Contains(a.Id)).ToList();
        var target = freeTown.Count > 0 ? freeTown[^1] : towns[^1];
        used.Add(target.Id);
        return target.Id;
    }

    /// <summary>
    /// The unused Route area nearest a gate edge (prefer the far side), for hosting a terrain requirement.
    /// Skipping used routes keeps each area to at most one terrain tag, so conflicting tags never coincide.
    /// </summary>
    private static int NearestRoute(IReadOnlyList<Area> path, int pos, HashSet<int> used)
    {
        for (var d = 0; d < path.Count; d++)
        {
            var ahead = pos + 1 + d;
            if (ahead < path.Count && path[ahead].Archetype == AreaArchetype.Route && !used.Contains(path[ahead].Id))
                return path[ahead].Id;
            var behind = pos - d;
            if (behind >= 0 && path[behind].Archetype == AreaArchetype.Route && !used.Contains(path[behind].Id))
                return path[behind].Id;
        }
        // Every route already tagged (many terrain gates): reuse the nearest route rather than fail.
        return path[Math.Min(pos + 1, path.Count - 1)].Id;
    }

    /// <summary>A spine area reachable before the gate to host the hint NPC — the nearest earlier town, else route.</summary>
    private static int HintArea(IReadOnlyList<Area> path, int pos)
    {
        for (var i = pos - 1; i >= 0; i--)
            if (path[i].Archetype is AreaArchetype.Town or AreaArchetype.StartTown) return path[i].Id;
        return path[Math.Max(0, pos - 1)].Id;
    }

    private static FlyPlacement PlaceFly(RegionGraph graph, Pcg32 rng, Dictionary<int, int> anchors, IReadOnlyList<Area> path, int badges)
    {
        var prereq = Math.Clamp((int)Math.Round(ObstacleTable.FlyTiming * badges), 1, badges);

        // Prefer an off-spine spot around mid-game; else a mid-path town.
        var midIndex = (int)Math.Round(ObstacleTable.FlyTiming * (path.Count - 1));
        var offSpine = anchors.Keys.OrderBy(id => Math.Abs(anchors[id] - midIndex)).ToList();
        if (offSpine.Count > 0) return new FlyPlacement(offSpine[0], prereq);

        var town = path.FirstOrDefault(a => a.PathIndex >= midIndex && a.Archetype == AreaArchetype.Town)
                   ?? path[midIndex];
        return new FlyPlacement(town.Id, prereq);
    }
}
