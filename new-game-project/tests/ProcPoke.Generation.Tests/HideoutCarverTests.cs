using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

public class HideoutCarverTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void HideoutsHaveFourRoomsBossLootAndReachableGrunts(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            foreach (var area in region.Graph.Areas.Where(a => a.Archetype == AreaArchetype.VillainHideout))
            {
                var carved = region.Carved[area.Id];
                checkedCount++;
                Assert.Single(carved.Openings);
                Assert.True(RoomComponents(carved.Grid) >= 4, $"seed {seed}/{badges} area {area.Id}: fewer than four rooms");
                Assert.True(carved.Grid.Count(LogicalTile.TrainerPost) >= 3);
                Assert.Equal(1, carved.Grid.Count(LogicalTile.ItemBall));

                var entrance = carved.Openings[0];
                var reachable = Reachable(carved.Grid, entrance);
                foreach (var post in Find(carved.Grid, LogicalTile.TrainerPost))
                    Assert.Contains(post, reachable);
                Assert.Contains(Find(carved.Grid, LogicalTile.ItemBall)[0], reachable);

                // The contract is that the loot sits in the room farthest from the door — two doorways away,
                // diagonally opposite it. Asserting fixed coordinates instead only held while every hideout
                // was entered from the bottom; the complex is now laid out relative to its entrance.
                var item = Find(carved.Grid, LogicalTile.ItemBall)[0];
                Assert.True(IsInFarthestRoom(carved.Grid, entrance, item),
                    $"seed {seed}/{badges} area {area.Id}: loot is not in the room farthest from the entrance");
            }
        }
        Assert.True(checkedCount > 0, "no villain hideouts were exercised across the corpus");
    }

    private static bool[,] RoomMask(TileGrid grid)
    {
        var mask = new bool[grid.Width, grid.Height];
        for (var y = 1; y < grid.Height - 3; y++)
            for (var x = 1; x < grid.Width - 4; x++)
            {
                var open = true;
                for (var dy = 0; dy < 3 && open; dy++)
                    for (var dx = 0; dx < 4; dx++) open &= grid[x + dx, y + dy].IsWalkable();
                if (!open) continue;
                for (var dy = 0; dy < 3; dy++)
                    for (var dx = 0; dx < 4; dx++) mask[x + dx, y + dy] = true;
            }
        return mask;
    }

    private static int RoomComponents(TileGrid grid)
    {
        var mask = RoomMask(grid);
        var seen = new bool[grid.Width, grid.Height];
        var count = 0;
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                if (!mask[x, y] || seen[x, y]) continue;
                count++;
                var queue = new Queue<(int X, int Y)>([(x, y)]);
                seen[x, y] = true;
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    foreach (var next in Neighbours(current))
                        if (grid.InBounds(next.X, next.Y) && mask[next.X, next.Y] && !seen[next.X, next.Y])
                        {
                            seen[next.X, next.Y] = true;
                            queue.Enqueue(next);
                        }
                }
            }
        return count;
    }

    private static IReadOnlyList<(int X, int Y)> Find(TileGrid grid, LogicalTile tile)
    {
        var found = new List<(int X, int Y)>();
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid[x, y] == tile) found.Add((x, y));
        return found;
    }

    private static HashSet<(int X, int Y)> Reachable(TileGrid grid, (int X, int Y) start)
    {
        var seen = new HashSet<(int X, int Y)> { start };
        var queue = new Queue<(int X, int Y)>([start]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in Neighbours(current))
                if (grid.InBounds(next.X, next.Y) && grid[next.X, next.Y].IsWalkable() && seen.Add(next)) queue.Enqueue(next);
        }
        return seen;
    }

    private static IEnumerable<(int X, int Y)> Neighbours((int X, int Y) cell)
    {
        yield return (cell.X + 1, cell.Y);
        yield return (cell.X - 1, cell.Y);
        yield return (cell.X, cell.Y + 1);
        yield return (cell.X, cell.Y - 1);
    }

    /// <summary>Whether <paramref name="cell"/> lies in the room component whose closest tile to the entrance
    /// is farther than every other room's.</summary>
    private static bool IsInFarthestRoom(TileGrid grid, (int X, int Y) entrance, (int X, int Y) cell)
    {
        var labels = RoomLabels(grid, out var count);
        if (count == 0 || labels[cell.X, cell.Y] == 0) return false;

        var distance = Distances(grid, entrance);
        var nearest = new Dictionary<int, int>();
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var label = labels[x, y];
                if (label == 0 || distance[x, y] < 0) continue;
                if (!nearest.TryGetValue(label, out var best) || distance[x, y] < best)
                    nearest[label] = distance[x, y];
            }

        if (!nearest.TryGetValue(labels[cell.X, cell.Y], out var mine)) return false;
        return nearest.Values.All(d => d <= mine);
    }

    /// <summary>Room components, labelled from 1; 0 means "not part of any room".</summary>
    private static int[,] RoomLabels(TileGrid grid, out int count)
    {
        var mask = RoomMask(grid);
        var labels = new int[grid.Width, grid.Height];
        count = 0;
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                if (!mask[x, y] || labels[x, y] != 0) continue;
                var label = ++count;
                var queue = new Queue<(int X, int Y)>([(x, y)]);
                labels[x, y] = label;
                while (queue.Count > 0)
                    foreach (var next in Neighbours(queue.Dequeue()))
                        if (grid.InBounds(next.X, next.Y) && mask[next.X, next.Y] && labels[next.X, next.Y] == 0)
                        {
                            labels[next.X, next.Y] = label;
                            queue.Enqueue(next);
                        }
            }
        return labels;
    }

    /// <summary>Walkable step distance from <paramref name="start"/>; -1 where unreachable.</summary>
    private static int[,] Distances(TileGrid grid, (int X, int Y) start)
    {
        var distance = new int[grid.Width, grid.Height];
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++) distance[x, y] = -1;

        distance[start.X, start.Y] = 0;
        var queue = new Queue<(int X, int Y)>([start]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in Neighbours(current))
                if (grid.InBounds(next.X, next.Y) && grid[next.X, next.Y].IsWalkable()
                    && distance[next.X, next.Y] < 0)
                {
                    distance[next.X, next.Y] = distance[current.X, current.Y] + 1;
                    queue.Enqueue(next);
                }
        }
        return distance;
    }
}
