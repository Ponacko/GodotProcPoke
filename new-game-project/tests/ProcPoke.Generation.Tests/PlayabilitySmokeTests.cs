using ProcPoke.Generation;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class PlayabilitySmokeTests
{
    private static GeneratedRegion Generate(ulong seed)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);

    [Theory]
    [InlineData(2ul)]
    [InlineData(42ul)]
    [InlineData(777ul)]
    public void CriticalPathReachesLeagueWithRequiredInteractions(ulong seed)
    {
        var region = Generate(seed);

        var result = PlayabilitySmoke.Run(region);

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
        Assert.Equal(region.Graph.CriticalPath.Count, result.AreasVisited);
        Assert.True(result.SeamlessConnections > 0);
        Assert.True(result.WarpConnections > 0);
        Assert.True(result.ExercisedNpc);
        Assert.True(result.ExercisedItem);
        Assert.True(result.ExercisedGate);
    }

    [Fact]
    public void SmokeDoesNotMutateGeneratedMaps()
    {
        var region = Generate(42);
        var beforeWorld = region.World.Grid[0, 0];
        var firstGate = region.Gating.Gates[0];
        var gateArea = region.Graph.CriticalPath.Single(area => area.PathIndex == firstGate.BlockPathIndex);
        var gatePosition = region.Carved[gateArea.Id].GateTiles[0];
        var beforeGate = region.Carved[gateArea.Id].Grid[gatePosition.X, gatePosition.Y];

        _ = PlayabilitySmoke.Run(region);

        Assert.Equal(beforeWorld, region.World.Grid[0, 0]);
        Assert.Equal(beforeGate, region.Carved[gateArea.Id].Grid[gatePosition.X, gatePosition.Y]);
    }
}
