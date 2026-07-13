using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Gating;

/// <summary>
/// An independent solvability check (ADR-0002's assertion). It does not trust the generator: it replays
/// the region as a player would — obtaining a key only when its area is reachable, earning a badge only
/// on reaching that gym, crossing a gate only with key + badge in hand — and confirms the League is
/// reachable. A circular lock or unreachable key stalls the fixpoint short of the League and fails here.
/// </summary>
public static class GatingValidator
{
    public static bool IsSolvable(RegionGraph graph, GatingPlan gating)
    {
        var path = graph.CriticalPath;
        var m = path.Count;

        var anchors = new Dictionary<int, int>();
        foreach (var area in graph.OffSpineAreas)
        {
            var onSpine = graph.Neighbors(area.Id).Where(n => n.OnCriticalPath).Select(n => n.PathIndex);
            if (onSpine.Any()) anchors[area.Id] = onSpine.Min();
        }

        var gateAtEdge = gating.Gates.ToDictionary(g => g.BlockPathIndex);
        var keys = new HashSet<string>();
        var reached = 0; // furthest contiguous critical-path index reachable

        int TownsUpTo(int r) => path.Take(r + 1).Count(a => a.Archetype == AreaArchetype.Town);
        bool AreaReachable(int areaId)
        {
            var area = graph[areaId];
            if (area.OnCriticalPath) return area.PathIndex <= reached;
            return anchors.TryGetValue(areaId, out var anchor) && anchor <= reached;
        }

        var changed = true;
        while (changed)
        {
            changed = false;

            // Pick up any key whose area is now reachable.
            foreach (var gate in gating.Gates)
                if (!keys.Contains(gate.KeyName) && AreaReachable(gate.KeyAreaId))
                {
                    keys.Add(gate.KeyName);
                    changed = true;
                }

            // Walk as far forward as current keys and badges allow.
            while (reached < m - 1)
            {
                var badges = TownsUpTo(reached);
                if (gateAtEdge.TryGetValue(reached, out var gate))
                {
                    var haveKey = keys.Contains(gate.KeyName);
                    var haveBadge = gate.BadgePrerequisite is null || badges >= gate.BadgePrerequisite;
                    if (!haveKey || !haveBadge) break;
                }
                reached++;
                changed = true;
            }
        }

        return reached >= m - 1;
    }
}
