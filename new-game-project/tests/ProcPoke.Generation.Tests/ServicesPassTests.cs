using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Services;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// The services pass (GDD §8a). Every constraint is recomputed from the graph and the carved towns rather
/// than read back off the plan, so a pass that placed a service by accident still fails.
/// </summary>
public class ServicesPassTests
{
    private static GeneratedRegion Generate(ulong seed, int badges)
        => RegionGenerator.Generate(
            new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    private static bool IsCity(Area area)
        => area.Archetype is AreaArchetype.StartTown or AreaArchetype.Town;

    /// <summary>Service seats a house interior offers — the house template's NPC-post count, which the pass
    /// mirrors as its own constant.</summary>
    private const int PostsPerHouse = 2;

    private static int HouseCount(GeneratedRegion region, int areaId)
        => region.Carved[areaId].BuildingEntrances.Count(entrance => entrance.Kind == BuildingKind.House);

    private static IReadOnlyList<int> AvailabilityOrderOf(GeneratedRegion region)
        => AvailabilityOrder.Of(region.Graph, GatingGenerator.OffSpineAnchors(region.Graph));

    private static double FractionOf(IReadOnlyList<int> order, int areaId)
        => order.Count <= 1 ? 1 : order.ToList().IndexOf(areaId) / (order.Count - 1.0);

    /// <summary>Independent BFS over the graph, so the "nearest city to the fossil cave" claim is checked
    /// against the topology rather than against the pass's own arithmetic.</summary>
    private static Dictionary<int, int> HopsFrom(RegionGraph graph, int fromAreaId)
    {
        var hops = new Dictionary<int, int> { [fromAreaId] = 0 };
        var queue = new Queue<int>([fromAreaId]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in graph.Neighbors(current))
                if (hops.TryAdd(neighbor.Id, hops[current] + 1)) queue.Enqueue(neighbor.Id);
        }
        return hops;
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void ServicesAreWellPlacedAcrossTheCorpus(int badges)
    {
        var checkedCount = 0;
        var spreadYieldedCount = 0;

        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = Generate(seed, badges);
            var plan = region.Services;
            var order = AvailabilityOrderOf(region);
            var cities = order.Where(areaId => IsCity(region.Graph[areaId])).ToList();

            // --- Every kind present; tutors scale but stay bounded.
            foreach (var kind in Enum.GetValues<ServiceKind>())
                Assert.NotNull(plan.FirstOf(kind));
            var tutors = plan.Placements.Count(placement => placement.Kind == ServiceKind.MoveTutor);
            Assert.InRange(tutors, 1, 3);
            foreach (var kind in Enum.GetValues<ServiceKind>().Where(kind => kind != ServiceKind.MoveTutor))
                Assert.Equal(1, plan.Placements.Count(placement => placement.Kind == kind));

            // --- Every placement sits at a real, distinct house post.
            Assert.All(plan.Placements, placement =>
            {
                Assert.Contains(placement.AreaId, cities);
                Assert.InRange(placement.HouseIndex, 0, HouseCount(region, placement.AreaId) - 1);
                Assert.InRange(placement.PostIndex, 0, PostsPerHouse - 1);
            });
            Assert.Equal(
                plan.Placements.Count,
                plan.Placements
                    .Select(placement => (placement.AreaId, placement.HouseIndex, placement.PostIndex))
                    .Distinct().Count());

            // --- 0–2 per city, unless the region's houses cannot seat the guaranteed set that way.
            var spreadFits = cities.Sum(areaId => Math.Min(2, HouseCount(region, areaId) * PostsPerHouse))
                >= Enum.GetValues<ServiceKind>().Length;
            var busiest = plan.Placements.GroupBy(placement => placement.AreaId).Max(group => group.Count());
            if (spreadFits) Assert.True(busiest <= 2, $"seed {seed} seated {busiest} services in one city");
            else spreadYieldedCount++;

            // --- Fossil revival binds to the nearest city to the fossil cave that has a house to seat it.
            var hops = HopsFrom(region.Graph, region.Dex.FossilAreaId);
            var nearest = cities
                .Where(areaId => HouseCount(region, areaId) > 0)
                .Min(areaId => hops.GetValueOrDefault(areaId, int.MaxValue));
            var revival = plan.FirstOf(ServiceKind.FossilRevival)!;
            Assert.Equal(nearest, hops[revival.AreaId]);

            // --- The Move Deleter and the guaranteed Link Cable vendor both land by mid-game.
            Assert.True(FractionOf(order, plan.FirstOf(ServiceKind.MoveDeleter)!.AreaId) <= 0.5);
            Assert.Contains(plan.LinkCableVendorAreaId, cities);
            Assert.True(FractionOf(order, plan.LinkCableVendorAreaId) <= 0.5);

            checkedCount++;
        }

        Assert.True(checkedCount > 0);
        // Not an assertion about the pass — a guard that the corpus is exercising the normal spread path
        // rather than silently running entirely on the house-poor fallback.
        Assert.True(spreadYieldedCount < checkedCount);
    }

    [Fact]
    public void SameSeedProducesTheSamePlan()
    {
        var first = Generate(4242, 8).Services;
        var second = Generate(4242, 8).Services;
        Assert.Equal(first.Placements, second.Placements);
        Assert.Equal(first.LinkCableVendorAreaId, second.LinkCableVendorAreaId);
    }

    [Fact]
    public void ServicesAreReachableByHouseLookup()
    {
        var region = Generate(777, 8);
        foreach (var placement in region.Services.Placements)
            Assert.Contains(placement, region.Services.InHouse(placement.AreaId, placement.HouseIndex));
        Assert.Empty(region.Services.InHouse(region.Graph.LeagueAreaId, 0));
    }

    /// <summary>
    /// A one-badge region has two cities, sometimes with a single house between them — fewer seats than
    /// there are services. Generation must still succeed (it used to throw), seat every service the region
    /// has room for, and spend those seats in priority order, so what a cramped region loses is a move
    /// tutor rather than fossil revival.
    /// </summary>
    [Fact]
    public void TinyRegionsSeatWhatTheyCanInPriorityOrder()
    {
        var priority = new[]
        {
            ServiceKind.FossilRevival, ServiceKind.MoveDeleter, ServiceKind.NameRater, ServiceKind.MoveTutor,
        };
        var shortfallSeen = 0;

        for (ulong seed = 1; seed <= 60; seed++)
        {
            var region = Generate(seed, 1);
            var cities = region.Graph.Areas
                .Where(area => area.Archetype is AreaArchetype.StartTown or AreaArchetype.Town)
                .ToList();
            var seats = cities.Sum(area => HouseCount(region, area.Id) * PostsPerHouse);
            var placed = region.Services.Placements.Select(placement => placement.Kind).Distinct().ToList();

            Assert.Equal(Math.Min(seats, priority.Length), region.Services.Placements.Count);
            Assert.Equal(priority.Take(placed.Count), placed);
            if (placed.Count < priority.Length) shortfallSeen++;
        }

        // The corpus must actually reach the cramped path, or this test proves nothing.
        Assert.True(shortfallSeen > 0);
    }
}
