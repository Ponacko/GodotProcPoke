using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Edge-alignment invariants (2a): for every Seamless connection, the shared descriptor
/// <see cref="OpeningAligner"/> hands to both endpoints agrees, and each carver actually stamps that
/// descriptor onto its tiles. Warp connections need no alignment and are asserted to have none.
/// </summary>
public class OpeningAlignmentTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void SeamlessConnectionsAgreeOnOffsetAndWidth(int badges)
    {
        var planned = 0;
        for (ulong seed = 1; seed <= 500; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges });
            foreach (var c in region.Graph.Connections.Where(c => c.Kind == ConnectionKind.Seamless))
            {
                var fromA = region.Openings.OpeningsOf(c.AreaA).Single(o => o.NeighborAreaId == c.AreaB);
                var fromB = region.Openings.OpeningsOf(c.AreaB).Single(o => o.NeighborAreaId == c.AreaA);
                planned++;

                Assert.Equal(fromA.Offset, fromB.Offset);
                Assert.Equal(fromA.Width, fromB.Width);
                Assert.True(
                    (fromA.Edge is EdgeSide.Left or EdgeSide.Right && fromB.Edge is EdgeSide.Left or EdgeSide.Right)
                    || (fromA.Edge is EdgeSide.Top or EdgeSide.Bottom && fromB.Edge is EdgeSide.Top or EdgeSide.Bottom),
                    $"seed {seed}/{badges} connection {c.AreaA}-{c.AreaB}: edges {fromA.Edge}/{fromB.Edge} " +
                    "don't share an axis, so the offset isn't the same coordinate on both sides");
            }
        }
        Assert.True(planned > 0, "no Seamless connections were exercised across the corpus — nothing was tested");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void WarpConnectionsHaveNoAlignedOpening(int badges)
    {
        var warped = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges });
            foreach (var c in region.Graph.Connections.Where(c => c.Kind == ConnectionKind.Warp))
            {
                warped++;
                Assert.DoesNotContain(region.Openings.OpeningsOf(c.AreaA), o => o.NeighborAreaId == c.AreaB);
                Assert.DoesNotContain(region.Openings.OpeningsOf(c.AreaB), o => o.NeighborAreaId == c.AreaA);
            }
        }
        Assert.True(warped > 0, "no Warp connections were exercised across the corpus — nothing was tested");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void CarvedTilesLandOnThePlannedOpening(int badges)
    {
        var branchOpenings = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges });
            var streams = new RngStreams(seed);
            // Seamless connections join any two non-dungeon areas (Builder.Connect) — StartTown/Town/Route
            // *and* League, which is neither Plain nor Dungeon (a loop-back fallback can reach it directly).
            var carved = region.Graph.Areas.Where(a => !a.IsDungeon).ToDictionary(a => a.Id,
                a => AreaCarver.Carve(a, region.Biomes.Of(a.Id), streams.Stream("carve", a.Id),
                    region.Openings, AreaCarver.GateOnExitOf(a, region.Gating)));

            foreach (var (areaId, area) in carved)
                foreach (var o in region.Openings.OpeningsOf(areaId))
                {
                    var tile = o.TileOn(area.Grid.Width, area.Grid.Height);
                    Assert.Equal(LogicalTile.Warp, area.Grid[tile.X, tile.Y]);
                    Assert.Contains(tile, area.Openings);
                    if (o.Edge is EdgeSide.Top or EdgeSide.Bottom) branchOpenings++;
                }

            // The real test of "edge-aligned": carve BOTH sides of every Seamless connection and check the
            // actual tiles line up on the shared axis, not just the plan records that produced them. Both
            // sides are always in `carved` — an unaligned edge here would be a design-invariant break, not
            // routine input, so it's fine to let a missing key throw rather than silently skip.
            foreach (var c in region.Graph.Connections.Where(c => c.Kind == ConnectionKind.Seamless))
            {
                var (a, b) = (carved[c.AreaA], carved[c.AreaB]);
                var oa = region.Openings.OpeningsOf(c.AreaA).Single(o => o.NeighborAreaId == c.AreaB);
                var ob = region.Openings.OpeningsOf(c.AreaB).Single(o => o.NeighborAreaId == c.AreaA);
                var (ax, ay) = oa.TileOn(a.Grid.Width, a.Grid.Height);
                var (bx, by) = ob.TileOn(b.Grid.Width, b.Grid.Height);

                var sharedCoordinate = oa.Edge is EdgeSide.Left or EdgeSide.Right ? (ay, by) : (ax, bx);
                Assert.Equal(sharedCoordinate.Item1, sharedCoordinate.Item2);
            }
        }
        Assert.True(branchOpenings > 0, "no top/bottom (branch or loop-back) opening was exercised — nothing was tested");
    }
}
