using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Bosses;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Roster;
using Xunit;

namespace ProcPoke.Generation.Tests;

public class BossPassTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void BossFormulasHoldAcrossCorpus(int badges)
    {
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var region = Generate(seed, badges);
            var data = TestData.Data;
            var dexIds = region.Dex.Entries.Select(e => e.SpeciesId).ToHashSet();
            var familyOf = FamilyMap(data, region.Settings.RosterCap);
            var gyms = region.Identity.GymTypes.OrderBy(kv => region.Graph[kv.Key].PathIndex).ToList();

            Assert.Equal(gyms.Count, region.Bosses.GymLeaders.Count);
            for (var i = 0; i < gyms.Count; i++)
            {
                var (areaId, type) = gyms[i];
                var leader = region.Bosses.GymLeaders[areaId];
                var fraction = gyms.Count <= 1 ? 1 : i / (gyms.Count - 1.0);
                var expectedSize = Math.Min(6, 2 + (int)Math.Round(4 * fraction));
                var ace = 14 + (gyms.Count <= 1 ? 36 : (int)Math.Round(36.0 * i / (gyms.Count - 1)));
                Assert.Equal(expectedSize, leader.Members.Count);
                Assert.Equal(type, leader.AssignedType);
                Assert.Equal(new[] { ace, ace - 2, ace - 3, ace - 4, ace - 4, ace - 5 }[..expectedSize],
                    leader.Members.Select(m => m.Level));
                Assert.All(leader.Members, member => Assert.Contains(member.SpeciesId, dexIds));
                var typedPoolSize = dexIds.Count(id => data.Species[id].Types.Contains(type));
                if (typedPoolSize >= leader.Members.Count)
                    Assert.All(leader.Members, member => Assert.Contains(type, data.Species[member.SpeciesId].Types));
            }

            Assert.Equal(4, region.Bosses.EliteFour.Count);
            for (var i = 0; i < region.Bosses.EliteFour.Count; i++)
            {
                var team = region.Bosses.EliteFour[i];
                var type = region.Identity.EliteFourTypes[i];
                var ace = 55 + i;
                Assert.Equal(type, team.AssignedType);
                Assert.Equal(6, team.Members.Count);
                Assert.Equal(new[] { ace - 2, ace - 2, ace - 1, ace - 1, ace - 1, ace },
                    team.Members.Select(m => m.Level));
                Assert.All(team.Members, member =>
                {
                    Assert.Contains(member.SpeciesId, dexIds);
                    Assert.Contains(type, data.Species[member.SpeciesId].Types);
                });
            }

            var champion = region.Bosses.Champion;
            Assert.Null(champion.AssignedType);
            Assert.Equal(6, champion.Members.Count);
            Assert.Equal(new[] { 57, 57, 58, 58, 58, 60 }, champion.Members.Select(m => m.Level));
            Assert.All(champion.Members, member => Assert.Contains(member.SpeciesId, dexIds));
            Assert.Equal(6, champion.Members.Select(m => familyOf[m.SpeciesId]).Distinct().Count());
            Assert.All(data.Species.Values.SelectMany(s => s.Types).Distinct(), type =>
                Assert.True(champion.Members.Count(m => data.Species[m.SpeciesId].Types.Contains(type)) <= 2));

            var pseudo = new[] { 149, 248, 373, 376, 445, 635 }.Where(dexIds.Contains).ToHashSet();
            if (pseudo.Count > 0)
                Assert.Contains(champion.Members.Select(m => m.SpeciesId), id => pseudo.Contains(id));
            else
                Assert.Contains(champion.Members.Select(m => m.SpeciesId),
                    id => region.Starters.Corners.Any(c => c.FinalSpeciesId == id));
        }
    }

    [Fact]
    public void BossesAreDeterministicAndPrinted()
    {
        foreach (var seed in new ulong[] { 7, 42, 999 })
        {
            var a = Generate(seed);
            var b = Generate(seed);
            Assert.Equal(a.Bosses.GymLeaders.Keys.OrderBy(id => id), b.Bosses.GymLeaders.Keys.OrderBy(id => id));
            foreach (var areaId in a.Bosses.GymLeaders.Keys)
                Assert.Equal(Signature(a.Bosses.GymLeaders[areaId]), Signature(b.Bosses.GymLeaders[areaId]));
            Assert.Equal(a.Bosses.EliteFour.Select(Signature), b.Bosses.EliteFour.Select(Signature));
            Assert.Equal(Signature(a.Bosses.Champion), Signature(b.Bosses.Champion));

            var text = RegionGraphText.Render(a.Graph, bosses: a.Bosses);
            Assert.Contains("Boss teams:", text);
        }
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

    private static string Signature(BossTeam team)
        => $"{team.Name}|{team.AssignedType}|{string.Join(',', team.Members.Select(m => $"{m.SpeciesId}/{m.Level}"))}";
}
