using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves an enclosed rocky area from rooms and winding passages. Transit caves have aligned left/right
/// openings (plus any planned branch openings); destination caves have one entrance and a reward in the
/// room farthest from it. Boulder placement is accepted only when it preserves reachability.
/// </summary>
public static class CaveCarver
{
    private readonly record struct Room(int X0, int Y0, int X1, int Y1)
    {
        public int CenterX => (X0 + X1) / 2;
        public int CenterY => (Y0 + Y1) / 2;

        public IEnumerable<(int X, int Y)> Cells
        {
            get
            {
                for (var y = Y0; y <= Y1; y++)
                    for (var x = X0; x <= X1; x++)
                        yield return (x, y);
            }
        }
    }

    /// <summary>Compatibility overload for callers that do not have an opening plan (for example, a
    /// standalone preview); generated regions use the planned-opening overload below.</summary>
    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, bool transit)
        => Carve(area, biome, rng, [], transit);

    public static CarvedArea Carve(
        Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned, bool transit)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Wall);
        var guarded = new HashSet<(int X, int Y)>();
        var corridorCenterline = new HashSet<(int X, int Y)>();

        CarveKit.Border(grid, LogicalTile.Wall);
        var rooms = PickRooms(grid, rng);
        if (rooms.Count < 2)
            throw new InvalidOperationException($"could not place two rooms in cave area {area.Id}");

        foreach (var room in rooms)
            CarveKit.FillRect(grid, room.X0, room.Y0, room.X1, room.Y1, LogicalTile.Ground);

        rooms.Sort((a, b) => a.CenterX.CompareTo(b.CenterX));
        for (var i = 1; i < rooms.Count; i++)
            ConnectRooms(grid, rooms[i - 1], rooms[i], rng, corridorCenterline);

        var openings = new List<(int X, int Y)>();
        if (transit)
        {
            var spineY = planned.OffsetOr(EdgeSide.Right, h / 2);
            AddOpening(grid, guarded, openings, EdgeSide.Left, planned.OffsetOr(EdgeSide.Left, h / 2), spineY);
            AddOpening(grid, guarded, openings, EdgeSide.Right, spineY, spineY);

            ConnectToRoom(grid, openings[0], rooms[0], corridorCenterline);
            ConnectToRoom(grid, openings[1], rooms[^1], corridorCenterline);

            foreach (var opening in planned.Where(o => o.Edge is EdgeSide.Top or EdgeSide.Bottom))
            {
                AddOpening(grid, guarded, openings, opening.Edge, opening.Offset, spineY);
                ConnectToNearestRoom(grid, openings[^1], rooms, corridorCenterline);
            }
        }
        else
        {
            var plannedEntrance = planned.FirstOrDefault();
            var edge = plannedEntrance is null ? EdgeSide.Bottom : plannedEntrance.Edge;
            var offset = plannedEntrance is null ? w / 2 : plannedEntrance.Offset;
            var spineY = rooms[0].CenterY;
            AddOpening(grid, guarded, openings, edge, offset, spineY);
            ConnectToNearestRoom(grid, openings[0], rooms, corridorCenterline);
        }

        var entrance = openings[0];
        var itemRoom = rooms
            .OrderByDescending(room => Distance(grid, entrance, (room.CenterX, room.CenterY)))
            .First();

        var itemCell = itemRoom.Cells
            .Where(cell => grid[cell.X, cell.Y] == LogicalTile.Ground && !corridorCenterline.Contains(cell))
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .FirstOrDefault();
        if (itemCell == default || grid[itemCell.X, itemCell.Y] != LogicalTile.Ground)
            throw new InvalidOperationException($"room in cave area {area.Id} has no item cell");
        grid[itemCell.X, itemCell.Y] = LogicalTile.ItemBall;

        PlaceBoulders(grid, openings, itemCell, rooms, rng);
        if (transit) EnsureBend(grid, openings, itemCell, rooms);

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = openings };
    }

    private static List<Room> PickRooms(TileGrid grid, Pcg32 rng)
    {
        var rooms = new List<Room>();
        var target = 2 + rng.NextInt(2);

        for (var i = 0; i < target; i++)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var width = rng.NextIntInclusive(5, 8);
                var height = rng.NextIntInclusive(4, 6);
                var x = rng.NextInt(2, grid.Width - width - 1);
                var y = rng.NextInt(2, grid.Height - height - 1);
                var candidate = new Room(x, y, x + width - 1, y + height - 1);
                if (rooms.All(existing => !Touches(candidate, existing)))
                {
                    rooms.Add(candidate);
                    break;
                }
            }
        }

        // Medium and large grids normally satisfy rejection sampling immediately. This deterministic
        // fallback keeps the two-room acceptance invariant true on the small footprint too.
        for (var i = rooms.Count; i < 2; i++)
        {
            var width = Math.Min(6, grid.Width - 4);
            var height = Math.Min(4, grid.Height - 4);
            var found = false;
            for (var y = 2; y <= grid.Height - height - 2 && !found; y++)
                for (var x = 2; x <= grid.Width - width - 2; x++)
                {
                    var candidate = new Room(x, y, x + width - 1, y + height - 1);
                    if (rooms.All(existing => !Touches(candidate, existing)))
                    {
                        rooms.Add(candidate);
                        found = true;
                        break;
                    }
                }
        }

        return rooms;
    }

    private static bool Touches(Room a, Room b)
        => a.X0 <= b.X1 + 1 && a.X1 + 1 >= b.X0 && a.Y0 <= b.Y1 + 1 && a.Y1 + 1 >= b.Y0;

    private static void ConnectRooms(
        TileGrid grid, Room from, Room to, Pcg32 rng, HashSet<(int X, int Y)> centerline)
    {
        var midX = from.CenterX + 1 >= to.CenterX
            ? (from.CenterX + to.CenterX) / 2
            : rng.NextInt(from.CenterX + 1, to.CenterX);

        CarveSegment(grid, centerline, (from.CenterX, from.CenterY), (midX, from.CenterY));
        if (from.CenterY == to.CenterY)
        {
            var offset = rng.Chance(0.5) ? -2 : 2;
            var bentY = Math.Clamp(from.CenterY + offset, 1, grid.Height - 2);
            CarveSegment(grid, centerline, (midX, from.CenterY), (midX, bentY));
            CarveSegment(grid, centerline, (midX, bentY), (to.CenterX, bentY));
            CarveSegment(grid, centerline, (to.CenterX, bentY), (to.CenterX, to.CenterY));
        }
        else
        {
            CarveSegment(grid, centerline, (midX, from.CenterY), (midX, to.CenterY));
            CarveSegment(grid, centerline, (midX, to.CenterY), (to.CenterX, to.CenterY));
        }
    }

    private static void ConnectToNearestRoom(
        TileGrid grid, (int X, int Y) opening, IReadOnlyList<Room> rooms,
        HashSet<(int X, int Y)> centerline)
    {
        var room = rooms.OrderBy(r => Manhattan(opening, (r.CenterX, r.CenterY))).First();
        ConnectToRoom(grid, opening, room, centerline);
    }

    private static void ConnectToRoom(
        TileGrid grid, (int X, int Y) opening, Room room, HashSet<(int X, int Y)> centerline)
    {
        var inside = (
            X: Math.Clamp(opening.X, 1, grid.Width - 2),
            Y: Math.Clamp(opening.Y, 1, grid.Height - 2));

        // An L, not a single leg: CarveSegment draws one straight run, so aiming straight at the room centre
        // only connects when the room happens to straddle the opening's row — otherwise the corridor
        // dead-ends beside the room and strands the opening.
        //
        // Turn first, then run. Running along the opening's row and turning at the end would connect too,
        // but both entrance legs would lie on the spine row and — meeting in the middle through the rooms —
        // fuse into one dead-straight highway across the cave. Leaving the row immediately keeps the
        // passage winding (ticket 5b).
        if (room.CenterY == inside.Y)
        {
            // The room's centre already sits on the opening's row, so the L collapses to one straight run.
            // Detour through an offset row instead — the same trick ConnectRooms uses for two rooms sharing
            // a centre row — so a transit cave never degenerates into a dead-straight corridor.
            var jogY = inside.Y + 2 <= grid.Height - 2 ? inside.Y + 2 : inside.Y - 2;
            CarveSegment(grid, centerline, inside, (inside.X, jogY));
            CarveSegment(grid, centerline, (inside.X, jogY), (room.CenterX, jogY));
            CarveSegment(grid, centerline, (room.CenterX, jogY), (room.CenterX, room.CenterY));
            return;
        }

        var corner = (X: inside.X, Y: room.CenterY);
        CarveSegment(grid, centerline, inside, corner);
        CarveSegment(grid, centerline, corner, (room.CenterX, room.CenterY));
    }

    private static void AddOpening(
        TileGrid grid, HashSet<(int X, int Y)> guarded, List<(int X, int Y)> openings,
        EdgeSide edge, int offset, int spineY)
    {
        var (w, h) = (grid.Width, grid.Height);
        var safeOffset = edge is EdgeSide.Left or EdgeSide.Right
            ? Math.Clamp(offset, 1, h - 2)
            : Math.Clamp(offset, 1, w - 2);
        var opening = CarveKit.OpenSpineEdge(grid, guarded, edge, safeOffset, spineY, LogicalTile.Ground);
        if (!openings.Contains(opening)) openings.Add(opening);
    }

    private static void CarveSegment(
        TileGrid grid, HashSet<(int X, int Y)> centerline, (int X, int Y) from, (int X, int Y) to)
    {
        if (from.X == to.X)
        {
            foreach (var y in Range(from.Y, to.Y))
            {
                if (grid.InBounds(from.X, y) && from.X > 0 && from.X < grid.Width - 1 && y > 0 && y < grid.Height - 1)
                {
                    grid[from.X, y] = LogicalTile.Ground;
                    centerline.Add((from.X, y));
                }
            }
        }
        else
        {
            foreach (var x in Range(from.X, to.X))
            {
                if (grid.InBounds(x, from.Y) && x > 0 && x < grid.Width - 1 && from.Y > 0 && from.Y < grid.Height - 1)
                {
                    grid[x, from.Y] = LogicalTile.Ground;
                    centerline.Add((x, from.Y));
                }
            }
        }
    }

    private static void PlaceBoulders(
        TileGrid grid, IReadOnlyList<(int X, int Y)> openings, (int X, int Y) item,
        IReadOnlyList<Room> rooms, Pcg32 rng)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var candidates = new List<(int X, int Y)>();
            for (var y = 1; y < grid.Height - 1; y++)
                for (var x = 1; x < grid.Width - 1; x++)
                    if (grid[x, y] == LogicalTile.Ground && !IsAdjacentToOpening(x, y, openings))
                        candidates.Add((x, y));
            if (candidates.Count == 0) break;

            var cell = rng.Pick(candidates);
            grid[cell.X, cell.Y] = LogicalTile.Boulder;
            if (!AllReachable(grid, openings, item) || !RoomsRemainOpen(grid, rooms)
                || !ItemStandsInOpenRoom(grid, item))
                grid[cell.X, cell.Y] = LogicalTile.Ground;
        }

        // Eight random attempts are the normal path. If they all land on the few connectivity-critical
        // cells, finish from a stable candidate order so the visible cave still meets the minimum field
        // dressing contract rather than silently producing an under-populated room layout.
        foreach (var cell in GroundCandidates(grid, openings))
        {
            if (grid.Count(LogicalTile.Boulder) >= 3) break;
            grid[cell.X, cell.Y] = LogicalTile.Boulder;
            if (!AllReachable(grid, openings, item) || !RoomsRemainOpen(grid, rooms)
                || !ItemStandsInOpenRoom(grid, item))
                grid[cell.X, cell.Y] = LogicalTile.Ground;
        }
    }

    /// <summary>
    /// Whether the reward still sits inside a fully open 4×3 pocket. <see cref="RoomsRemainOpen"/> only asks
    /// that each room keep <em>some</em> open pocket, which a boulder dropped beside the item satisfies while
    /// still leaving the item wedged in rubble — the reward has to read as sitting in a room.
    /// </summary>
    private static bool ItemStandsInOpenRoom(TileGrid grid, (int X, int Y) item)
    {
        for (var y = Math.Max(1, item.Y - 2); y <= item.Y && y < grid.Height - 3; y++)
            for (var x = Math.Max(1, item.X - 3); x <= item.X && x < grid.Width - 4; x++)
            {
                var open = true;
                for (var dy = 0; dy < 3 && open; dy++)
                    for (var dx = 0; dx < 4 && open; dx++)
                        open = grid[x + dx, y + dy].IsWalkable();
                if (open) return true;
            }
        return false;
    }

    /// <summary>
    /// A transit cave has to read as a passage, not a hallway: the walk between its two openings turns at
    /// least once (ticket 5b). Corridors and room fills can still line up into one straight row whatever the
    /// routing does, so this is enforced as a post-condition rather than chased through the geometry — drop a
    /// boulder on the straight run wherever a detour already exists, under the same accept-only-if-still-
    /// reachable rule <see cref="PlaceBoulders"/> uses. A cave with no such cell is left as it is.
    /// </summary>
    private static void EnsureBend(
        TileGrid grid, IReadOnlyList<(int X, int Y)> openings, (int X, int Y) item, IReadOnlyList<Room> rooms)
    {
        var entry = openings.FirstOrDefault(o => o.X == 0);
        var exit = openings.FirstOrDefault(o => o.X == grid.Width - 1);
        if (entry == default || exit == default || PathBends(grid, entry, exit)) return;

        foreach (var cell in ShortestPath(grid, entry, exit))
        {
            if (cell == item || grid[cell.X, cell.Y] != LogicalTile.Ground) continue;
            if (IsAdjacentToOpening(cell.X, cell.Y, openings)) continue;

            grid[cell.X, cell.Y] = LogicalTile.Boulder;
            if (AllReachable(grid, openings, item) && RoomsRemainOpen(grid, rooms)
                && ItemStandsInOpenRoom(grid, item) && PathBends(grid, entry, exit))
                return;
            grid[cell.X, cell.Y] = LogicalTile.Ground;
        }
    }

    private static bool PathBends(TileGrid grid, (int X, int Y) from, (int X, int Y) to)
    {
        var path = ShortestPath(grid, from, to);
        return path.Count > 0 && path.Zip(path.Skip(1)).Any(step => step.First.Y != step.Second.Y);
    }

    private static IReadOnlyList<(int X, int Y)> ShortestPath(
        TileGrid grid, (int X, int Y) from, (int X, int Y) to)
    {
        var parents = new Dictionary<(int X, int Y), (int X, int Y)> { [from] = from };
        var queue = new Queue<(int X, int Y)>([from]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == to) break;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (X: current.X + dx, Y: current.Y + dy);
                if (!grid.InBounds(next.X, next.Y) || parents.ContainsKey(next)) continue;
                if (!grid[next.X, next.Y].IsWalkable()) continue;
                parents[next] = current;
                queue.Enqueue(next);
            }
        }

        if (!parents.ContainsKey(to)) return [];
        var path = new List<(int X, int Y)>();
        for (var current = to; ; current = parents[current])
        {
            path.Add(current);
            if (current == from) break;
        }
        path.Reverse();
        return path;
    }

    private static bool RoomsRemainOpen(TileGrid grid, IReadOnlyList<Room> rooms)
        => rooms.All(room =>
            Enumerable.Range(room.X0, room.X1 - room.X0 - 2)
                .Any(x => Enumerable.Range(room.Y0, room.Y1 - room.Y0 - 1)
                    .Any(y => Enumerable.Range(x, 4).All(px => Enumerable.Range(y, 3)
                        .All(py => grid[px, py].IsWalkable())))));

    private static IEnumerable<(int X, int Y)> GroundCandidates(
        TileGrid grid, IReadOnlyList<(int X, int Y)> openings)
    {
        for (var y = 1; y < grid.Height - 1; y++)
            for (var x = 1; x < grid.Width - 1; x++)
                if (grid[x, y] == LogicalTile.Ground && !IsAdjacentToOpening(x, y, openings))
                    yield return (x, y);
    }

    private static bool IsAdjacentToOpening(int x, int y, IReadOnlyList<(int X, int Y)> openings)
        => openings.Any(o => Math.Abs(o.X - x) <= 1 && Math.Abs(o.Y - y) <= 1);

    private static bool AllReachable(
        TileGrid grid, IReadOnlyList<(int X, int Y)> openings, (int X, int Y) item)
    {
        if (openings.Count == 0) return false;
        var seen = Reachable(grid, openings[0]);
        return openings.Skip(1).All(seen.Contains) && seen.Contains(item);
    }

    private static HashSet<(int X, int Y)> Reachable(TileGrid grid, (int X, int Y) start)
    {
        var seen = new HashSet<(int X, int Y)> { start };
        var queue = new Queue<(int X, int Y)>([start]);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (x + dx, y + dy);
                if (grid.InBounds(next.Item1, next.Item2) && grid[next.Item1, next.Item2].IsWalkable() && seen.Add(next))
                    queue.Enqueue(next);
            }
        }
        return seen;
    }

    private static int Distance(TileGrid grid, (int X, int Y) from, (int X, int Y) to)
    {
        var distances = new Dictionary<(int X, int Y), int> { [from] = 0 };
        var queue = new Queue<(int X, int Y)>([from]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == to) return distances[current];
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (X: current.X + dx, Y: current.Y + dy);
                if (grid.InBounds(next.X, next.Y) && grid[next.X, next.Y].IsWalkable() && !distances.ContainsKey(next))
                {
                    distances[next] = distances[current] + 1;
                    queue.Enqueue(next);
                }
            }
        }
        return -1;
    }

    private static int Manhattan((int X, int Y) a, (int X, int Y) b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static IEnumerable<int> Range(int a, int b)
    {
        if (a <= b) for (var i = a; i <= b; i++) yield return i;
        else for (var i = a; i >= b; i--) yield return i;
    }
}
