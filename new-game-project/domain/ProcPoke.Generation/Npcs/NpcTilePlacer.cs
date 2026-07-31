using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Rng;

namespace ProcPoke.Generation.Npcs;

/// <summary>
/// Realizes the graph-level NPC plan on carved maps. Placement is deliberately a separate pass from
/// <see cref="NpcPass"/>: dialogue belongs to the graph, while a tile is a property of the finished map.
/// </summary>
public static class NpcTilePlacer
{
    /// <summary>
    /// Stamps one <see cref="LogicalTile.NpcPost"/> per planned post. The input grids are cloned only for
    /// areas that have posts, keeping the raw carver output available to independent carving checks.
    /// </summary>
    public static IReadOnlyDictionary<int, CarvedArea> Place(
        NpcPlan plan, IReadOnlyDictionary<int, CarvedArea> carved, RngStreams streams)
    {
        var rng = streams.Stream("npc-tiles");
        var result = new Dictionary<int, CarvedArea>(carved.Count);

        foreach (var (areaId, original) in carved.OrderBy(pair => pair.Key))
        {
            var posts = plan.Of(areaId);
            if (posts.Count == 0)
            {
                result[areaId] = original;
                continue;
            }

            var area = original with { Grid = original.Grid.Clone() };
            for (var postIndex = 0; postIndex < posts.Count; postIndex++)
                PlaceOne(area, rng, postIndex);

            result[areaId] = area;
        }

        return result;
    }

    private static void PlaceOne(CarvedArea area, Pcg32 rng, int postIndex)
    {
        var preferred = Candidates(area, strict: true).ToList();
        var candidates = preferred.Count > 0 ? preferred : Candidates(area, strict: false).ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"area {area.AreaId} has no walkable tile for NPC post {postIndex + 1}");

        var (x, y) = rng.Pick(candidates);
        area.Grid[x, y] = LogicalTile.NpcPost;
    }

    private static IEnumerable<(int X, int Y)> Candidates(CarvedArea area, bool strict)
    {
        var grid = area.Grid;
        var openings = area.Openings.ToHashSet();
        var gateTiles = area.GateTiles.ToHashSet();

        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                var position = (x, y);
                if (openings.Contains(position) || gateTiles.Contains(position))
                    continue;

                if (strict)
                {
                    if (grid[x, y] != LogicalTile.Ground ||
                        (area.TrunkRow >= 0 && y == area.TrunkRow) ||
                        IsAdjacentTo(position, openings) || IsAdjacentTo(position, gateTiles))
                        continue;
                }
                else if (grid[x, y] is not (LogicalTile.Ground or LogicalTile.TallGrass or LogicalTile.Ledge
                    or LogicalTile.Sand))
                {
                    // Do not overwrite a trainer, item, or already placed NPC marker if a tight map needs
                    // the fallback. Those are all walkable, but replacing them would orphan their plans.
                    continue;
                }

                yield return position;
            }
        }
    }

    private static bool IsAdjacentTo((int X, int Y) position, IReadOnlySet<(int X, int Y)> blocked)
        => blocked.Contains((position.X + 1, position.Y))
            || blocked.Contains((position.X - 1, position.Y))
            || blocked.Contains((position.X, position.Y + 1))
            || blocked.Contains((position.X, position.Y - 1));

}
