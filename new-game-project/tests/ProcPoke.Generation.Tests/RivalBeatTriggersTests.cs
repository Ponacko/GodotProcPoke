using ProcPoke.Generation;
using ProcPoke.Generation.Rivals;
using ProcPoke.Generation.Topology;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class RivalBeatTriggersTests
{
    private static GeneratedRegion Generate(ulong seed, int badges)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void EveryMapTriggeredBeatLandsOnADistinctCriticalPathAreaInProgressionOrder(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var region = Generate(seed, badges);
            var criticalPathIds = region.Graph.CriticalPath.Select(area => area.Id).ToHashSet();
            var leagueIndex = region.Graph[region.Graph.LeagueAreaId].PathIndex;

            var pathIndexByBeat = RivalBeatTriggers.MapTriggeredBeats.ToDictionary(
                beat => beat,
                beat => region.Graph[RivalBeatTriggers.AreaFor(beat, region.Graph, region.Identity)].PathIndex);

            foreach (var beat in RivalBeatTriggers.MapTriggeredBeats)
            {
                var areaId = RivalBeatTriggers.AreaFor(beat, region.Graph, region.Identity);
                Assert.Contains(areaId, criticalPathIds);
                Assert.NotEqual(region.Graph.StartAreaId, areaId);
                Assert.NotEqual(region.Graph.LeagueAreaId, areaId);
                Assert.True(pathIndexByBeat[beat] < leagueIndex);
            }

            Assert.True(pathIndexByBeat[RivalBeat.EarlyRoute] <= pathIndexByBeat[RivalBeat.Midpoint]);
            Assert.True(pathIndexByBeat[RivalBeat.Midpoint] <= pathIndexByBeat[RivalBeat.PreLeague]);

            // GDD §7.2: EarlyRoute fires "on an early route", not in a gym city. TopologyGenerator's
            // Backbone() never places two Town areas back-to-back (always ≥1 Route between them), so the
            // area right after the first gym is guaranteed non-Town — locking that guarantee in here.
            var earlyRouteArea = region.Graph[RivalBeatTriggers.AreaFor(RivalBeat.EarlyRoute, region.Graph, region.Identity)];
            Assert.NotEqual(AreaArchetype.Town, earlyRouteArea.Archetype);
            // GDD §7.2: Midpoint fires "at the midpoint city" — it must be a gym city.
            var midpointArea = region.Graph[RivalBeatTriggers.AreaFor(RivalBeat.Midpoint, region.Graph, region.Identity)];
            Assert.Equal(AreaArchetype.Town, midpointArea.Archetype);
            Assert.Contains(midpointArea.Id, region.Identity.GymTypes.Keys);

            // Determinism: re-deriving from the same region must agree with itself every time.
            foreach (var beat in RivalBeatTriggers.MapTriggeredBeats)
                Assert.Equal(
                    RivalBeatTriggers.AreaFor(beat, region.Graph, region.Identity),
                    RivalBeatTriggers.AreaFor(beat, region.Graph, region.Identity));

            checkedCount++;
        }

        Assert.True(checkedCount > 0);
    }

    [Fact]
    public void UnrecognizedBeatHasNoMapTrigger()
    {
        var region = Generate(seed: 1, badges: 8);
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => RivalBeatTriggers.AreaFor(RivalBeat.PostStarter, region.Graph, region.Identity));
    }
}
