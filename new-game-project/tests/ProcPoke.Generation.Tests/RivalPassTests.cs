using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Encounters;
using ProcPoke.Generation.Rivals;
using ProcPoke.Generation.Roster;
using Xunit;

namespace ProcPoke.Generation.Tests;

public class RivalPassTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void RivalVariantsFollowPinnedBeatsAndCounterTriangle(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var region = Generate(seed, badges);
            var data = TestData.Data;
            var dexIds = region.Dex.Entries.Select(entry => entry.SpeciesId).ToHashSet();
            var familyOf = FamilyMap(data, region.Settings.RosterCap);
            var gymCount = region.Identity.GymTypes.Count;
            var variants = region.Rivals.Variants;

            Assert.Equal(3, variants.Count);
            for (var playerIndex = 0; playerIndex < region.Starters.Corners.Count; playerIndex++)
            {
                var variant = variants[playerIndex];
                var expectedRivalIndex = (playerIndex + 2) % 3;
                Assert.Equal(playerIndex, variant.PlayerCornerIndex);
                Assert.Equal(expectedRivalIndex, variant.RivalCornerIndex);

                var playerType = region.Starters.Corners[playerIndex].Type;
                var rivalType = region.Starters.Corners[expectedRivalIndex].Type;
                Assert.True(data.TypeChart.Effectiveness(rivalType, playerType) >
                    data.TypeChart.Effectiveness(playerType, rivalType));

                var expectedAces = new[]
                {
                    5,
                    LevelCurve.GymAce(0, gymCount) + 2,
                    LevelCurve.GymAce(gymCount / 2, gymCount),
                    52,
                };
                var expectedSizes = new[] { 1, 2, 4, 5 };
                Assert.Equal(4, variant.Beats.Count);
                for (var beatIndex = 0; beatIndex < variant.Beats.Count; beatIndex++)
                {
                    var team = variant.Beats[beatIndex];
                    Assert.Equal(expectedAces[beatIndex], team.AceLevel);
                    Assert.Equal(expectedSizes[beatIndex], team.Members.Count);
                    Assert.Equal(team.RivalStarterSpeciesId, team.Members[0].SpeciesId);
                    Assert.All(team.Members, member =>
                    {
                        Assert.Contains(member.SpeciesId, dexIds);
                        Assert.Equal(team.AceLevel, member.Level);
                        Assert.False(data.Species[member.SpeciesId].IsLegendary ||
                            data.Species[member.SpeciesId].IsMythical);
                    });
                    Assert.Equal(team.Members.Count,
                        team.Members.Select(member => familyOf[member.SpeciesId]).Distinct().Count());

                    var expectedStage = beatIndex == 0
                        ? region.Starters.Corners[expectedRivalIndex].BaseSpeciesId
                        : HighestPermittedStage(region.Starters.Corners[expectedRivalIndex], team.AceLevel, data);
                    Assert.Equal(expectedStage, team.RivalStarterSpeciesId);
                }

                if (region.Identity.ChampionIsRival)
                {
                    Assert.NotNull(variant.ChampionTeam);
                    Assert.Contains(region.Starters.Corners[expectedRivalIndex].FinalSpeciesId,
                        variant.ChampionTeam!.Members.Select(member => member.SpeciesId));
                }
                else
                    Assert.Null(variant.ChampionTeam);

                checkedCount++;
            }
        }

        Assert.True(checkedCount > 0);
    }

    [Fact]
    public void RivalOutputIsDeterministicAndPrinted()
    {
        foreach (var seed in new ulong[] { 7, 42, 999 })
        {
            var first = Generate(seed);
            var second = Generate(seed);
            Assert.Equal(Signature(first.Rivals), Signature(second.Rivals));
            var text = RegionGraphText.Render(first.Graph, rivals: first.Rivals);
            Assert.Contains("Rival teams:", text);
            Assert.Contains("player corner 1", text);
        }
    }

    private static int HighestPermittedStage(StarterCorner corner, int ace, GameData data)
    {
        var stage = 0;
        for (var i = 1; i < corner.LineSpeciesIds.Count; i++)
        {
            var rule = data.Evolutions.Single(e =>
                e.FromSpeciesId == corner.LineSpeciesIds[i - 1] && e.ToSpeciesId == corner.LineSpeciesIds[i]);
            if (rule.MinLevel is not int minLevel || minLevel > ace)
                break;
            stage = i;
        }
        return corner.LineSpeciesIds[stage];
    }

    private static IReadOnlyDictionary<int, int> FamilyMap(GameData data, int cap)
    {
        var map = new Dictionary<int, int>();
        var index = 0;
        foreach (var family in EvolutionFamilies.Build(data, cap))
        {
            foreach (var speciesId in family.Members) map[speciesId] = index;
            index++;
        }
        return map;
    }

    private static string Signature(RivalPlan plan)
        => string.Join("|", plan.Variants.Select(variant =>
            $"{variant.PlayerCornerIndex}/{variant.RivalCornerIndex}/" +
            string.Join(";", variant.Beats.Select(team =>
                $"{team.Beat}:{team.AceLevel}:{string.Join(',', team.Members.Select(member =>
                    $"{member.SpeciesId}/{member.Level}"))}")) + "/" +
            (variant.ChampionTeam is null ? "npc" : string.Join(',', variant.ChampionTeam.Members.Select(member =>
                $"{member.SpeciesId}/{member.Level}")))));
}
