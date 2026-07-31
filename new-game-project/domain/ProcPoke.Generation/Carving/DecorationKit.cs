using ProcPoke.Generation.Rng;

namespace ProcPoke.Generation.Carving;

/// <summary>Shared placement rules for route and forest decorative interactables.</summary>
internal static class DecorationKit
{
    private static readonly (int X, int Y)[] Directions = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    public static void PlaceItemNook(
        TileGrid grid, LogicalTile floor, LogicalTile blocker, int spineY, Pcg32 rng,
        IReadOnlySet<(int X, int Y)> protectedTiles, IReadOnlySet<(int X, int Y)> nearOpening)
    {
        var candidates = Interior(grid)
            .Where(p => p.X >= 2 && p.X <= grid.Width - 3 && p.Y != spineY && grid[p.X, p.Y] == floor
                && !protectedTiles.Contains(p) && !nearOpening.Contains(p))
            .ToList();
        var shuffled = candidates.OrderBy(_ => rng.NextUInt()).ToList();

        foreach (var cell in shuffled)
        {
            if (NonWalkableNeighbours(grid, cell) < 2)
            {
                foreach (var (dx, dy) in Directions)
                {
                    var neighbour = (X: cell.X + dx, Y: cell.Y + dy);
                    if (!grid.InBounds(neighbour.X, neighbour.Y) || protectedTiles.Contains(neighbour)
                        || nearOpening.Contains(neighbour) || grid[neighbour.X, neighbour.Y] == LogicalTile.Ledge
                        || WouldStrandLedge(grid, neighbour)
                        || !grid[neighbour.X, neighbour.Y].IsWalkable()) continue;
                    grid[neighbour.X, neighbour.Y] = blocker;
                    if (NonWalkableNeighbours(grid, cell) >= 2) break;
                }
            }
            if (NonWalkableNeighbours(grid, cell) >= 2)
            {
                grid[cell.X, cell.Y] = LogicalTile.ItemBall;
                return;
            }
        }

        // A turning corridor can consume the pockets the normal search uses. Build a nook around the first
        // safe fallback instead of placing an exposed item and silently violating the decoration contract.
        var fallbacks = Interior(grid)
            .Where(p => p.X >= 2 && p.X <= grid.Width - 3 && p.Y != spineY
                && grid[p.X, p.Y].IsWalkable() && !protectedTiles.Contains(p))
            .OrderBy(_ => rng.NextUInt());
        foreach (var fallback in fallbacks)
        {
            foreach (var (dx, dy) in Directions)
            {
                if (NonWalkableNeighbours(grid, fallback) >= 2) break;
                var neighbour = (X: fallback.X + dx, Y: fallback.Y + dy);
                if (!grid.InBounds(neighbour.X, neighbour.Y) || protectedTiles.Contains(neighbour)
                    || nearOpening.Contains(neighbour) || !grid[neighbour.X, neighbour.Y].IsWalkable()
                    || grid[neighbour.X, neighbour.Y] == LogicalTile.Ledge
                    || WouldStrandLedge(grid, neighbour)) continue;
                grid[neighbour.X, neighbour.Y] = blocker;
            }
            if (NonWalkableNeighbours(grid, fallback) < 2) continue;
            grid[fallback.X, fallback.Y] = LogicalTile.ItemBall;
            return;
        }

        // This is only reachable for a pathological one-tile interior. Keep the interactable rather than
        // throwing during generation; normal route/forest footprints always have a repairable fallback.
        var lastResort = Interior(grid).First(p => p.Y != spineY && grid[p.X, p.Y].IsWalkable());
        grid[lastResort.X, lastResort.Y] = LogicalTile.ItemBall;
    }

    public static void PlaceTrainerPosts(
        TileGrid grid, LogicalTile floor, LogicalTile blocker, int spineY, int count, Pcg32 rng,
        IReadOnlySet<(int X, int Y)> protectedTiles, IReadOnlySet<(int X, int Y)> nearOpening)
    {
        var candidates = Interior(grid)
            .Where(p => p.X >= 2 && p.X <= grid.Width - 3 && Math.Abs(p.Y - spineY) <= 2
                && grid[p.X, p.Y].IsWalkable()
                && grid[p.X, p.Y] != LogicalTile.ItemBall && grid[p.X, p.Y] != LogicalTile.Ledge
                && !protectedTiles.Contains(p) && !nearOpening.Contains(p)
                && HasSightline(grid, p, spineY))
            .ToList();

        for (var i = 0; i < count; i++)
        {
            if (candidates.Count == 0)
            {
                var fallbackX = Math.Clamp(grid.Width / 2 + i * 2, 2, grid.Width - 3);
                var fallbackY = spineY + (i % 2 == 0 ? -1 : 1);
                fallbackY = Math.Clamp(fallbackY, 1, grid.Height - 2);
                for (var y = Math.Min(fallbackY, spineY); y <= Math.Max(fallbackY, spineY); y++)
                    if (!grid[fallbackX, y].IsWalkable()
                        && !AdjacentToItem(grid, (fallbackX, y))) grid[fallbackX, y] = floor;
                grid[fallbackX, fallbackY] = LogicalTile.TrainerPost;
                continue;
            }

            var post = rng.Pick(candidates);
            candidates.Remove(post);
            grid[post.X, post.Y] = LogicalTile.TrainerPost;
        }

        EnsureItemNook(grid, floor, blocker, spineY, protectedTiles, nearOpening);
    }

    /// <summary>Whether blocking <paramref name="cell"/> would strand a Ledge. A ledge is a one-way hop
    /// south, so it needs a walkable tile both north and south of it; the tile directly above or below one
    /// is therefore never nook material, however good a pocket it would make.</summary>
    private static bool WouldStrandLedge(TileGrid grid, (int X, int Y) cell)
        => IsLedge(grid, cell.X, cell.Y - 1) || IsLedge(grid, cell.X, cell.Y + 1);

    private static bool IsLedge(TileGrid grid, int x, int y)
        => grid.InBounds(x, y) && grid[x, y] == LogicalTile.Ledge;

    public static int NonWalkableNeighbours(TileGrid grid, (int X, int Y) cell)
        => Directions.Count(d =>
        {
            var x = cell.X + d.X;
            var y = cell.Y + d.Y;
            return !grid.InBounds(x, y) || !grid[x, y].IsWalkable();
        });

    public static bool HasSightline(TileGrid grid, (int X, int Y) candidate, int spineY)
    {
        if (candidate.Y == spineY) return true;
        for (var y = Math.Min(candidate.Y, spineY); y <= Math.Max(candidate.Y, spineY); y++)
            if (!grid[candidate.X, y].IsWalkable()) return false;
        return true;
    }

    private static bool AdjacentToItem(TileGrid grid, (int X, int Y) cell)
        => Directions.Any(d => grid.InBounds(cell.X + d.X, cell.Y + d.Y)
            && grid[cell.X + d.X, cell.Y + d.Y] == LogicalTile.ItemBall);

    private static void EnsureItemNook(
        TileGrid grid, LogicalTile floor, LogicalTile blocker, int spineY,
        IReadOnlySet<(int X, int Y)> protectedTiles, IReadOnlySet<(int X, int Y)> nearOpening)
    {
        var item = Interior(grid).FirstOrDefault(p => grid[p.X, p.Y] == LogicalTile.ItemBall);
        if (!grid.InBounds(item.X, item.Y) || NonWalkableNeighbours(grid, item) >= 2) return;

        if (TryBlockAround(grid, item, blocker, protectedTiles, nearOpening)
            && NonWalkableNeighbours(grid, item) >= 2) return;

        foreach (var candidate in Interior(grid).Where(p => p.X >= 2 && p.X <= grid.Width - 3
            && p.Y != spineY && grid[p.X, p.Y].IsWalkable() && grid[p.X, p.Y] != LogicalTile.TrainerPost
            && grid[p.X, p.Y] != LogicalTile.ItemBall && !protectedTiles.Contains(p)))
        {
            if (!TryBlockAround(grid, candidate, blocker, protectedTiles, nearOpening)
                || NonWalkableNeighbours(grid, candidate) < 2) continue;
            grid[item.X, item.Y] = floor;
            grid[candidate.X, candidate.Y] = LogicalTile.ItemBall;
            return;
        }

    }

    private static bool TryBlockAround(
        TileGrid grid, (int X, int Y) cell, LogicalTile blocker,
        IReadOnlySet<(int X, int Y)> protectedTiles, IReadOnlySet<(int X, int Y)> nearOpening)
    {
        foreach (var (dx, dy) in Directions)
        {
            if (NonWalkableNeighbours(grid, cell) >= 2) break;
            var neighbour = (X: cell.X + dx, Y: cell.Y + dy);
            if (!grid.InBounds(neighbour.X, neighbour.Y) || protectedTiles.Contains(neighbour)
                || nearOpening.Contains(neighbour) || grid[neighbour.X, neighbour.Y] == LogicalTile.Ledge
                || WouldStrandLedge(grid, neighbour) || !grid[neighbour.X, neighbour.Y].IsWalkable()
                || grid[neighbour.X, neighbour.Y] == LogicalTile.TrainerPost) continue;
            grid[neighbour.X, neighbour.Y] = blocker;
        }
        return NonWalkableNeighbours(grid, cell) >= 2;
    }

    private static IEnumerable<(int X, int Y)> Interior(TileGrid grid)
    {
        for (var y = 1; y < grid.Height - 1; y++)
            for (var x = 1; x < grid.Width - 1; x++)
                yield return (x, y);
    }
}
