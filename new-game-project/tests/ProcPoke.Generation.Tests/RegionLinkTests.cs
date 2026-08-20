using ProcPoke.Generation;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// The reserved external-connection slot (GDD §11, §15). Phase 7 attaches a second region here, so every
/// region must keep one free lattice cell beside a real area — and the choice must be deterministic, since
/// a save records the region and the link has to land in the same place next session.
/// </summary>
public sealed class RegionLinkTests
{
    private static GeneratedRegion Generate(ulong seed, int badges)
        => RegionGenerator.Generate(
            new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void EveryRegionKeepsAFreeSlotForASecondRegion(int badges)
    {
        var checkedCount = 0;
        var cityCount = 0;

        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = Generate(seed, badges);
            var slot = RegionLink.SlotFor(region.Graph);

            Assert.NotNull(slot);
            var host = region.Graph[slot!.AreaId];
            // The slot's cell is genuinely free, and genuinely beside its host.
            Assert.DoesNotContain(slot.Cell, region.Graph.Areas.Select(area => area.Cell));
            Assert.True(host.Cell.IsAdjacentTo(slot.Cell));
            Assert.Equal(slot.Cell, host.Cell.Step(slot.Heading));

            if (host.Archetype is AreaArchetype.StartTown or AreaArchetype.Town) cityCount++;
            checkedCount++;
        }

        Assert.True(checkedCount > 0);
        // A city is preferred (a port for the ship crossing, or the start of a border pass), and with this
        // many seeds one is always available.
        Assert.Equal(checkedCount, cityCount);
    }

    [Fact]
    public void TheSlotIsTheSameEveryTimeItIsAskedFor()
    {
        var region = Generate(4242, 8);

        Assert.Equal(RegionLink.SlotFor(region.Graph), RegionLink.SlotFor(region.Graph));
        Assert.Equal(
            RegionLink.SlotFor(region.Graph), RegionLink.SlotFor(Generate(4242, 8).Graph));
    }
}
