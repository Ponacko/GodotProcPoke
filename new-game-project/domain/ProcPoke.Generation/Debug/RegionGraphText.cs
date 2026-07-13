using System.Text;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Debug;

/// <summary>
/// Renders a region as a readable text tree — the topology/gating review artifact until the tile-level
/// PNG renderer lands in 2b. The critical path runs top to bottom; branches, gates, and loop-backs hang
/// off it.
/// </summary>
public static class RegionGraphText
{
    public static string Render(RegionGraph g, GatingPlan? gating = null, BiomeMap? biomes = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Region  badges={g.BadgeCount}  areas={g.Areas.Count}  connections={g.Connections.Count}  connected={g.IsConnected()}");
        if (gating is not null)
            sb.AppendLine($"Gates={gating.Gates.Count}  Fly@{Label(g, gating.Fly.AreaId)} (badge {gating.Fly.BadgePrerequisite})");
        sb.AppendLine("Critical path (start → League):");

        var gateAtEdge = gating?.Gates.ToDictionary(x => x.BlockPathIndex) ?? [];
        var keySites = gating?.Gates.ToLookup(x => x.KeyAreaId);

        foreach (var area in g.CriticalPath)
        {
            var tag = area.IsDungeon ? "  «transit»" : "";
            var bio = biomes is not null ? $"  {{{biomes.Of(area.Id)}}}" : "";
            sb.AppendLine($"  [{area.PathIndex,2}] {area.Archetype,-12} {area.Size}{bio}{tag}");

            // Keys and hints hosted here.
            if (keySites is not null)
                foreach (var gk in keySites[area.Id])
                    sb.AppendLine($"        · holds key: {gk.KeyName}");

            // Off-spine areas hanging directly off this spine area (non-loop-back edges).
            foreach (var c in g.ConnectionsOf(area.Id))
            {
                if (c.IsLoopBack) continue;
                var other = g[c.Other(area.Id)];
                if (other.OnCriticalPath) continue;
                var holds = keySites is not null && keySites[other.Id].Any()
                    ? $"  → key: {string.Join(", ", keySites[other.Id].Select(k => k.KeyName))}"
                    : "";
                sb.AppendLine($"        └─ {other.Archetype,-14} {other.Size}  ({c.Kind}){holds}");
            }

            // A gate blocks the edge leaving this area.
            if (gateAtEdge.TryGetValue(area.PathIndex, out var gate))
            {
                var badge = gate.BadgePrerequisite is int b ? $", badge {b}" : "";
                var terrain = gate.TerrainTag is not null ? $" [{gate.TerrainTag}]" : "";
                sb.AppendLine($"   ⟫⟫ GATE {gate.Obstacle} → {gate.KeyName} ({gate.KeyKind}{badge}){terrain}  hint@{Label(g, gate.HintAreaId)}");
            }
        }

        var loopBacks = g.Connections.Where(c => c.IsLoopBack).ToList();
        if (loopBacks.Count > 0)
        {
            sb.AppendLine("Loop-backs:");
            foreach (var c in loopBacks)
                sb.AppendLine($"  {Label(g, c.AreaA)}  ↔  {Label(g, c.AreaB)}  ({c.Kind})");
        }

        return sb.ToString();
    }

    private static string Label(RegionGraph g, int areaId)
    {
        var a = g[areaId];
        return a.OnCriticalPath ? $"[{a.PathIndex}]{a.Archetype}" : $"{a.Archetype}";
    }
}
