using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Ticket 5a: forests carve as organic tree clumps, not full-height stripe barriers. Spine walkability,
/// opening alignment, and determinism are covered by CarvingTests/OpeningAlignmentTests; here we assert the
/// de-stripe signature (no all-tree column; ≥3 discrete interior clumps).
/// </summary>
public class ForestCarverTests
{
    private static IEnumerable<CarvedArea> Forests(ulong seed, int badges)
    {
        var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
        foreach (var area in region.Graph.Areas.Where(a => a.Archetype == AreaArchetype.Forest))
            yield return region.Carved[area.Id];
    }

    /// <summary>4-neighbour connected components over interior (non-border) Tree tiles.</summary>
    private static int InteriorTreeClumps(TileGrid g)
    {
        var seen = new bool[g.Width, g.Height];
        var clumps = 0;
        int[] dx = [1, -1, 0, 0], dy = [0, 0, 1, -1];
        bool Interior(int x, int y) => x >= 1 && x <= g.Width - 2 && y >= 1 && y <= g.Height - 2;

        for (var y = 1; y <= g.Height - 2; y++)
            for (var x = 1; x <= g.Width - 2; x++)
            {
                if (seen[x, y] || g[x, y] != LogicalTile.Tree) continue;
                clumps++;
                var q = new Queue<(int X, int Y)>();
                q.Enqueue((x, y));
                seen[x, y] = true;
                while (q.Count > 0)
                {
                    var (cx, cy) = q.Dequeue();
                    for (var d = 0; d < 4; d++)
                    {
                        int nx = cx + dx[d], ny = cy + dy[d];
                        if (Interior(nx, ny) && !seen[nx, ny] && g[nx, ny] == LogicalTile.Tree)
                        {
                            seen[nx, ny] = true;
                            q.Enqueue((nx, ny));
                        }
                    }
                }
            }
        return clumps;
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void ForestsAreClumpedNotStriped(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 300; seed++)
            foreach (var forest in Forests(seed, badges))
            {
                var g = forest.Grid;

                // No interior column is entirely Tree (the old stripe barriers spanned full height).
                for (var x = 1; x <= g.Width - 2; x++)
                {
                    var allTree = true;
                    for (var y = 1; y <= g.Height - 2; y++)
                        if (g[x, y] != LogicalTile.Tree) { allTree = false; break; }
                    Assert.False(allTree, $"seed {seed}/{badges} area {forest.AreaId}: interior column {x} is all Tree");
                }

                Assert.True(InteriorTreeClumps(g) >= 3,
                    $"seed {seed}/{badges} area {forest.AreaId}: fewer than 3 discrete tree clumps");
                checkedCount++;
            }
        Assert.True(checkedCount > 0, "no forests in the corpus — nothing was checked");
    }
}
