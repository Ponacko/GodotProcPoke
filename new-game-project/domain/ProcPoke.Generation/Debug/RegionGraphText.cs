using System.Text;
using ProcPoke.Data;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Bosses;
using ProcPoke.Generation.Encounters;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Identity;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Rivals;
using ProcPoke.Generation.Npcs;
using ProcPoke.Generation.Topology;
using ProcPoke.Generation.Trainers;

namespace ProcPoke.Generation.Debug;

/// <summary>
/// Renders a region as a readable text tree — the topology/gating review artifact until the tile-level
/// PNG renderer lands in 2b. The critical path runs top to bottom; branches, gates, and loop-backs hang
/// off it.
/// </summary>
public static class RegionGraphText
{
    public static string Render(RegionGraph g, GatingPlan? gating = null, BiomeMap? biomes = null,
        RegionNames? names = null, RegionIdentity? identity = null, StarterPlan? starters = null,
        DexPlan? dex = null, GameData? data = null, SpecialSpecies? special = null,
        EncounterPlan? encounters = null, TrainerPlan? trainers = null, BossPlan? bosses = null,
        RivalPlan? rivals = null, NpcPlan? npcs = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Region  badges={g.BadgeCount}  areas={g.Areas.Count}  connections={g.Connections.Count}  connected={g.IsConnected()}");
        if (gating is not null)
            sb.AppendLine($"Gates={gating.Gates.Count}  Fly@{Label(g, gating.Fly.AreaId)} (badge {gating.Fly.BadgePrerequisite})");
        if (names is not null)
            sb.AppendLine($"Naming motif: {names.Motif}");
        if (starters is not null)
        {
            var corners = string.Join("  ", starters.Corners.Select(c => $"{c.Type}: #{c.FinalSpeciesId}"));
            sb.AppendLine($"Starters ({starters.Triangle}): {corners}");
        }
        if (identity is not null)
        {
            sb.AppendLine($"Elite Four: {string.Join(", ", identity.EliteFourTypes)}  Champion: typeless={identity.ChampionIsTypeless}");
            foreach (var team in identity.VillainTeams)
                sb.AppendLine($"Villain: {team.Name} [{string.Join('/', team.Motif)}]");
            sb.AppendLine($"Rival: {identity.Rival}");
            sb.AppendLine(identity.ChampionIsRival ? "Champion: the rival" : "Champion: generated NPC");
        }
        sb.AppendLine("Critical path (start → League):");

        var gateAtEdge = gating?.Gates.ToDictionary(x => x.BlockPathIndex) ?? [];
        var keySites = gating?.Gates.ToLookup(x => x.KeyAreaId);

        foreach (var area in g.CriticalPath)
        {
            var tag = area.IsDungeon ? "  «transit»" : "";
            var bio = biomes is not null ? $"  {{{biomes.Of(area.Id)}}}" : "";
            var name = names is not null ? $"  \"{names.Of(area.Id)}\"" : "";
            var gymType = identity is not null && identity.GymTypes.TryGetValue(area.Id, out var t) ? $"  [{t} gym]" : "";
            sb.AppendLine($"  [{area.PathIndex,2}] {area.Archetype,-12} {area.Size}{bio}{name}{gymType}{tag}");

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

        if (dex is not null && data is not null)
            AppendDex(sb, g, dex, data, names);

        if (special is not null && data is not null)
            AppendSpecial(sb, g, special, data, names);

        if (encounters is not null)
            AppendEncounters(sb, g, encounters, names);

        if (trainers is not null)
            AppendTrainers(sb, g, trainers, names);

        if (bosses is not null)
            AppendBosses(sb, g, bosses, names);

        if (rivals is not null)
            AppendRivals(sb, rivals);

        if (npcs is not null)
            AppendNpcs(sb, g, npcs, names);

        return sb.ToString();
    }

    /// <summary>Per-area encounter methods, filled slot species, overlays, and base wild level.</summary>
    private static void AppendEncounters(StringBuilder sb, RegionGraph g, EncounterPlan encounters, RegionNames? names)
    {
        sb.AppendLine("Encounters (methods, slots @ base wild level):");
        foreach (var (areaId, area) in encounters.ByArea.OrderBy(kv => kv.Key))
        {
            static string TableText(EncounterTable table)
                => $"{table.Method}[{string.Join(',', table.Slots.Select(s => s.SpeciesId))}]";

            var methods = string.Join(" ", area.Tables.Select(TableText));
            var overlay = area.Overlay is null ? "" : $" overlay[{string.Join(',', area.Overlay.Slots.Select(s => s.SpeciesId))}]";
            sb.AppendLine($"    {AreaName(g, names, areaId),-22} {methods} Lv{area.BaseLevel}{overlay}");
        }
    }

    /// <summary>Per-area trainer class/ace summaries and item-ball contents.</summary>
    private static void AppendTrainers(StringBuilder sb, RegionGraph g, TrainerPlan trainers, RegionNames? names)
    {
        sb.AppendLine("Trainers & items:");
        foreach (var areaId in trainers.ByArea.Keys.Union(trainers.ItemsByArea.Keys).OrderBy(id => id))
        {
            var areaTrainers = trainers.TrainersOf(areaId);
            var classes = string.Join(", ", areaTrainers.Select(t => $"{t.DisplayClass} Lv{t.AceLevel}"));
            var items = string.Join(", ", trainers.ItemsOf(areaId).Select(i => $"item#{i.ItemId}"));
            sb.AppendLine($"    {AreaName(g, names, areaId),-22} {classes}  items[{items}]");
        }
    }

    /// <summary>Per-boss team type, size, and levels.</summary>
    private static void AppendBosses(StringBuilder sb, RegionGraph g, BossPlan bosses, RegionNames? names)
    {
        sb.AppendLine("Boss teams:");
        foreach (var (areaId, leader) in bosses.GymLeaders.OrderBy(kv => g[kv.Key].PathIndex))
            sb.AppendLine($"    gym {AreaName(g, names, areaId)} {leader.Name} {leader.AssignedType} [{string.Join(',', leader.Members.Select(m => $"#{m.SpeciesId}/Lv{m.Level}"))}]");
        foreach (var team in bosses.EliteFour)
            sb.AppendLine($"    {team.Name} {team.AssignedType} [{string.Join(',', team.Members.Select(m => $"#{m.SpeciesId}/Lv{m.Level}"))}]");
        sb.AppendLine($"    {bosses.Champion.Name} [{string.Join(',', bosses.Champion.Members.Select(m => $"#{m.SpeciesId}/Lv{m.Level}"))}]");
    }

    private static void AppendRivals(StringBuilder sb, RivalPlan rivals)
    {
        sb.AppendLine("Rival teams:");
        foreach (var variant in rivals.Variants.OrderBy(v => v.PlayerCornerIndex))
        {
            sb.AppendLine($"    player corner {variant.PlayerCornerIndex + 1} → rival corner {variant.RivalCornerIndex + 1}");
            foreach (var team in variant.Beats)
                sb.AppendLine($"      {team.Beat} ace Lv{team.AceLevel} [{string.Join(',', team.Members.Select(m => $"#{m.SpeciesId}/Lv{m.Level}"))}]");
            if (variant.ChampionTeam is not null)
                sb.AppendLine($"      {variant.ChampionTeam.Name} [{string.Join(',', variant.ChampionTeam.Members.Select(m => $"#{m.SpeciesId}/Lv{m.Level}"))}]");
        }
    }

    /// <summary>Per-area NPC post counts and generated dialogue.</summary>
    private static void AppendNpcs(StringBuilder sb, RegionGraph g, NpcPlan npcs, RegionNames? names)
    {
        sb.AppendLine("NPC posts:");
        foreach (var (areaId, posts) in npcs.ByArea.OrderBy(kv => g[kv.Key].PathIndex < 0 ? int.MaxValue : g[kv.Key].PathIndex)
                     .ThenBy(kv => kv.Key))
        {
            if (posts.Count == 0) continue;
            sb.AppendLine($"    {AreaName(g, names, areaId),-22} {posts.Count} posts");
            foreach (var post in posts)
                sb.AppendLine($"      {post.Kind}: {post.Text}");
        }
    }

    /// <summary>Fossils and legendaries: species, area, and (for legendaries) level and appended dex number.</summary>
    private static void AppendSpecial(StringBuilder sb, RegionGraph g, SpecialSpecies special, GameData data, RegionNames? names)
    {
        sb.AppendLine("Fossils & legendaries:");
        foreach (var f in special.Fossils)
            sb.AppendLine($"    fossil  {data.Species[f.SpeciesId].Name,-12} @ {AreaName(g, names, f.AreaId)}");
        foreach (var l in special.Legendaries)
            sb.AppendLine($"    #{l.DexNumber,3} {data.Species[l.SpeciesId].Name,-12} Lv{l.Level} @ {AreaName(g, names, l.AreaId)}");
    }

    /// <summary>An area's generated name in quotes, or its archetype when names aren't available.</summary>
    private static string AreaName(RegionGraph g, RegionNames? names, int areaId)
        => names is not null ? $"\"{names.Of(areaId)}\"" : g[areaId].Archetype.ToString();

    /// <summary>The regional dex: first and last ten entries (number, name, types, BST) and per-area counts.</summary>
    private static void AppendDex(StringBuilder sb, RegionGraph g, DexPlan dex, GameData data, RegionNames? names)
    {
        sb.AppendLine($"Regional dex ({dex.Entries.Count} species):");
        string Row(DexEntry e)
        {
            var s = data.Species[e.SpeciesId];
            return $"    #{e.Number,3} {s.Name,-12} {string.Join("/", s.Types),-14} BST {StarterSelector.Bst(s)}";
        }
        foreach (var e in dex.Entries.Take(10)) sb.AppendLine(Row(e));
        if (dex.Entries.Count > 20) sb.AppendLine("      …");
        foreach (var e in dex.Entries.Skip(Math.Max(10, dex.Entries.Count - 10))) sb.AppendLine(Row(e));

        sb.AppendLine("  Per-area first-availability counts:");
        foreach (var (areaId, species) in dex.SpeciesByArea.OrderBy(kv => kv.Key))
        {
            var fossil = areaId == dex.FossilAreaId ? "  [fossils]" : "";
            sb.AppendLine($"    {AreaName(g, names, areaId),-22} {species.Count} species{fossil}");
        }
    }

    private static string Label(RegionGraph g, int areaId)
    {
        var a = g[areaId];
        return a.OnCriticalPath ? $"[{a.PathIndex}]{a.Archetype}" : $"{a.Archetype}";
    }
}
