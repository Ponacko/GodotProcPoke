using ProcPoke.Data;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Encounters;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Npcs;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation;

/// <summary>
/// The Phase 2 exit checks shared by tests and the headless packet sweep. Checks return human-readable
/// failures instead of throwing so a packet can report every broken seed in one run.
/// </summary>
public static class Invariants
{
    public static IReadOnlyList<string> CheckAll(GeneratedRegion region, GameData data)
    {
        var errors = new List<string>();
        var graph = region.Graph;
        var anchors = GatingGenerator.OffSpineAnchors(graph);
        int Position(Area area) => area.OnCriticalPath
            ? area.PathIndex
            : anchors.TryGetValue(area.Id, out var anchor) ? anchor : int.MaxValue;
        void Fail(string message) => errors.Add(message);

        if (!graph.IsConnected()) Fail("topology is disconnected");
        var path = graph.CriticalPath;
        if (path.Count == 0 || path[0].Id != graph.StartAreaId)
            Fail("critical path does not start at StartTown");
        if (path.Count == 0 || path[^1].Id != graph.LeagueAreaId)
            Fail("critical path does not end at League");
        var cells = graph.Areas.Select(a => a.Cell).ToList();
        if (cells.Count != cells.Distinct().Count()) Fail("two areas share a lattice cell");
        foreach (var connection in graph.Connections)
            if (!graph[connection.AreaA].Cell.IsAdjacentTo(graph[connection.AreaB].Cell))
                Fail($"connection {connection.AreaA}-{connection.AreaB} is not adjacent");

        if (!GatingValidator.IsSolvable(graph, region.Gating)) Fail("gating replay cannot reach the League");
        foreach (var gate in region.Gating.Gates)
        {
            if (!graph.Areas.Any(a => a.Id == gate.KeyAreaId) || Position(graph[gate.KeyAreaId]) > gate.BlockPathIndex)
                Fail($"gate {gate.Id} key is after its block frontier");
            if (!graph.Areas.Any(a => a.Id == gate.HintAreaId) || Position(graph[gate.HintAreaId]) > gate.BlockPathIndex)
                Fail($"gate {gate.Id} hint is after its block frontier");

            var hints = region.Npcs.Of(gate.HintAreaId).Where(p => p.Kind == NpcKind.Hint).ToList();
            if (hints.Count == 0) Fail($"gate {gate.Id} has no hint post");
            if (!hints.Any(p => p.Text.Contains(region.Names.Of(gate.KeyAreaId), StringComparison.Ordinal)))
                Fail($"gate {gate.Id} hint does not name its key area");
        }

        CheckOpenings(region, Fail);
        CheckChokepoints(region, Fail);
        CheckNames(region, data, Fail);
        CheckDex(region, data, anchors, Fail);
        CheckPopulations(region, data, Fail);
        CheckLevels(region, anchors, Fail);
        return errors;
    }

    private static void CheckOpenings(GeneratedRegion region, Action<string> fail)
    {
        foreach (var connection in region.Graph.Connections.Where(c => c.Kind == ConnectionKind.Seamless))
        {
            var a = region.Openings.OpeningsOf(connection.AreaA)
                .FirstOrDefault(o => o.NeighborAreaId == connection.AreaB);
            var b = region.Openings.OpeningsOf(connection.AreaB)
                .FirstOrDefault(o => o.NeighborAreaId == connection.AreaA);
            if (a is null || b is null)
            {
                fail($"seamless connection {connection.AreaA}-{connection.AreaB} lacks a paired opening");
                continue;
            }
            if (a.Offset != b.Offset || a.Width != b.Width)
                fail($"seamless connection {connection.AreaA}-{connection.AreaB} offsets disagree");

            var carvedA = region.Carved[connection.AreaA];
            var carvedB = region.Carved[connection.AreaB];
            var tileA = a.TileOn(carvedA.Grid.Width, carvedA.Grid.Height);
            var tileB = b.TileOn(carvedB.Grid.Width, carvedB.Grid.Height);
            if (carvedA.Grid[tileA.X, tileA.Y] != LogicalTile.Warp || carvedB.Grid[tileB.X, tileB.Y] != LogicalTile.Warp)
                fail($"seamless connection {connection.AreaA}-{connection.AreaB} is not carved at its opening");
            var actualA = a.Edge is EdgeSide.Left or EdgeSide.Right ? tileA.Y : tileA.X;
            var actualB = b.Edge is EdgeSide.Left or EdgeSide.Right ? tileB.Y : tileB.X;
            if (actualA != actualB) fail($"seamless connection {connection.AreaA}-{connection.AreaB} tiles misalign");
        }
    }

    private static void CheckChokepoints(GeneratedRegion region, Action<string> fail)
    {
        foreach (var area in region.Graph.Areas)
        {
            var carved = region.Carved[area.Id];
            if (carved.Openings.Count > 1)
            {
                var cleared = carved.GateTiles.ToHashSet();
                foreach (var opening in carved.Openings.Skip(1))
                    if (!Reachable(carved.Grid, carved.Openings[0], opening, cleared))
                        fail($"area {area.Id} openings are not mutually reachable when cleared");
            }

            if (carved.GateTiles.Count == 0) continue;
            var next = region.Graph.CriticalPath.FirstOrDefault(a => a.PathIndex == area.PathIndex + 1);
            if (next is null) continue;
            var exitOpening = region.Openings.EdgesOf(area.Id)
                .FirstOrDefault(o => o.NeighborAreaId == next.Id);
            if (exitOpening is null) continue;
            var exit = exitOpening.TileOn(carved.Grid.Width, carved.Grid.Height);
            foreach (var entry in carved.Openings.Where(o => o != exit))
            {
                if (Reachable(carved.Grid, entry, exit))
                    fail($"area {area.Id} gate does not block its exit");
                if (!Reachable(carved.Grid, entry, exit, carved.GateTiles.ToHashSet()))
                    fail($"area {area.Id} gate does not clear to a reachable exit");
            }
        }
    }

    private static bool Reachable(TileGrid grid, (int X, int Y) from, (int X, int Y) to,
        IReadOnlySet<(int X, int Y)>? cleared = null)
    {
        bool Passable(int x, int y) => grid[x, y].IsWalkable() || (cleared?.Contains((x, y)) ?? false);
        if (!grid.InBounds(from.X, from.Y) || !grid.InBounds(to.X, to.Y)) return false;
        var seen = new bool[grid.Width, grid.Height];
        var queue = new Queue<(int X, int Y)>([from]);
        seen[from.X, from.Y] = true;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == to) return true;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (current.X + dx, current.Y + dy);
                if (grid.InBounds(next.Item1, next.Item2) && !seen[next.Item1, next.Item2]
                    && Passable(next.Item1, next.Item2))
                {
                    seen[next.Item1, next.Item2] = true;
                    queue.Enqueue(next);
                }
            }
        }
        return false;
    }

    private static void CheckNames(GeneratedRegion region, GameData data, Action<string> fail)
    {
        if (region.Names.ByArea.Count != region.Graph.Areas.Count) fail("not every area has a name");
        var names = region.Names.ByArea.Values.ToList();
        if (names.Count != names.Distinct(StringComparer.OrdinalIgnoreCase).Count()) fail("area names collide");
        foreach (var name in names)
            if (!IsCanonicalRouteName(name) && data.NameBlocklist.Contains(name))
                fail($"name is blocklisted: {name}");

        foreach (var (areaId, posts) in region.Npcs.ByArea)
            foreach (var post in posts)
                if (post.Text.Contains("area#", StringComparison.OrdinalIgnoreCase))
                    fail($"NPC post in area {areaId} exposes an internal area id");
        foreach (var route in region.Graph.Areas.Where(a => a.Archetype == AreaArchetype.Route))
            if (!region.Npcs.Of(route.Id).Where(p => p.Kind == NpcKind.Sign)
                    .Any(p => p.Text.Contains(region.Names.Of(route.Id), StringComparison.Ordinal)))
                fail($"route {route.Id} sign does not contain its generated name");
    }

    private static bool IsCanonicalRouteName(string name)
        => name.StartsWith("Route ", StringComparison.Ordinal)
            && int.TryParse(name[6..], out _);

    private static void CheckDex(GeneratedRegion region, GameData data,
        IReadOnlyDictionary<int, int> anchors, Action<string> fail)
    {
        var dex = region.Dex;
        var ids = dex.Entries.Select(e => e.SpeciesId).ToHashSet();
        if (ids.Count != dex.Entries.Count) fail("regional dex repeats a species");
        if (!dex.Entries.Select(e => e.Number).OrderBy(n => n).SequenceEqual(Enumerable.Range(1, dex.Entries.Count)))
            fail("regional dex numbering has a gap");
        var starterIds = region.Starters.Corners.SelectMany(c => c.LineSpeciesIds).ToList();
        if (dex.Entries.Count >= starterIds.Count
            && !dex.Entries.Take(starterIds.Count).Select(e => e.SpeciesId).SequenceEqual(starterIds))
            fail("starter lines are not first in the regional dex");
        var families = EvolutionFamilies.Build(data, region.Settings.RosterCap);
        foreach (var family in families.Where(f => f.Members.Any(ids.Contains)))
        {
            if (!family.Members.All(ids.Contains)) fail("regional dex splits an evolution family");
            var numbers = family.Members.Select(id => dex.Entries.First(e => e.SpeciesId == id).Number).ToList();
            if (!numbers.SequenceEqual(Enumerable.Range(numbers[0], numbers.Count)))
                fail("regional dex family members are not consecutive");
        }

        var seen = new HashSet<int>();
        var previousBlockMax = 0;
        var order = AvailabilityOrder.Of(region.Graph, anchors);
        foreach (var areaId in order)
            if (dex.SpeciesByArea.TryGetValue(areaId, out var species))
            {
                var numbers = species.Select(id => dex.Entries.First(e => e.SpeciesId == id).Number).ToList();
                if (numbers.Any(n => !seen.Add(n))) fail($"dex availability blocks overlap at area {areaId}");
                if (numbers.Count > 0 && numbers.Min() <= previousBlockMax)
                    fail($"dex availability blocks are out of order at area {areaId}");
                if (numbers.Count > 0) previousBlockMax = numbers.Max();
            }
        if (seen.Count != ids.Count) fail("some dex species have no availability area");
        if (region.Settings.DexSize == 150 && region.Settings.RosterCap == 5)
        {
            var present = ids.SelectMany(id => data.Species[id].Types).ToHashSet();
            foreach (var type in Enum.GetValues<PokeType>())
                if (!present.Contains(type)) fail($"default dex lacks type {type}");
        }
    }

    private static void CheckPopulations(GeneratedRegion region, GameData data, Action<string> fail)
    {
        var dex = region.Dex.Entries.Select(e => e.SpeciesId).ToHashSet();
        foreach (var entry in region.Encounters.ByArea.Values)
        {
            foreach (var table in entry.Tables.Append(entry.Overlay).Where(t => t is not null).Cast<EncounterTable>())
                foreach (var slot in table.Slots)
                    if (!dex.Contains(slot.SpeciesId)) fail($"encounter species #{slot.SpeciesId} is outside the dex");
        }
        foreach (var trainer in region.Trainers.ByArea.Values.SelectMany(x => x))
            foreach (var member in trainer.Roster)
                if (!dex.Contains(member.SpeciesId)) fail($"trainer species #{member.SpeciesId} is outside the dex");
        foreach (var team in region.Bosses.GymLeaders.Values.Append(region.Bosses.Champion)
                     .Concat(region.Bosses.EliteFour))
            foreach (var member in team.Members)
                if (!dex.Contains(member.SpeciesId)) fail($"boss species #{member.SpeciesId} is outside the dex");
        foreach (var variant in region.Rivals.Variants)
            foreach (var member in variant.Beats.SelectMany(team => team.Members)
                         .Concat(variant.ChampionTeam?.Members ?? []))
                if (!dex.Contains(member.SpeciesId)) fail($"rival species #{member.SpeciesId} is outside the dex");
    }

    private static void CheckLevels(GeneratedRegion region, IReadOnlyDictionary<int, int> anchors, Action<string> fail)
    {
        var previousWild = 0;
        foreach (var areaId in AvailabilityOrder.Of(region.Graph, anchors))
        {
            var entry = region.Encounters.Of(areaId);
            if (entry is null) continue;
            var wild = entry.BaseLevel - (LevelCurve.HasDungeonBump(region.Graph[areaId].Archetype) ? 2 : 0);
            if (wild < previousWild) fail($"wild levels fall at area {areaId}");
            previousWild = wild;
        }

        var previousTrainer = 0;
        foreach (var area in region.Graph.CriticalPath)
        {
            var trainers = region.Trainers.TrainersOf(area.Id);
            if (trainers.Count == 0) continue;
            var ace = trainers.Max(t => t.AceLevel);
            if (ace + 4 < previousTrainer) fail($"trainer levels fall at area {area.Id}");
            previousTrainer = Math.Max(previousTrainer, ace);
        }
    }
}
