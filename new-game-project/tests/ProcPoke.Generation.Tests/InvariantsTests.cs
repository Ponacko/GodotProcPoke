using ProcPoke.Generation;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>Smoke coverage for the shared Phase 2 exit checker used by MapGen's packet sweep.</summary>
public sealed class InvariantsTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void SharedCheckerPassesTheCorpus(int badges)
    {
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var region = RegionGenerator.Generate(
                new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            Assert.Empty(Invariants.CheckAll(region, TestData.Data));
        }
    }
}
