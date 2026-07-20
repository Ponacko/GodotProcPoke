using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Identity;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Ticket 3c: villain teams, rival archetype, and the Champion-identity roll. Structural facts are checked
/// per seed; the two distributional invariants (ChampionIsRival rate, and Underdog skew when the rival is
/// the Champion) accumulate across the corpus and assert once at the end.
/// </summary>
public class IdentityTests
{
    // The canon names IdentityPass must never reproduce (mirrored here for an independent check).
    private static readonly HashSet<string> Canon = new(StringComparer.OrdinalIgnoreCase)
        { "Rocket", "Aqua", "Magma", "Galactic", "Plasma", "Flare", "Skull", "Yell", "Star", "Snagem", "Cipher" };

    private static RegionIdentity Identity(ulong seed, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data).Identity;

    private static void AssertWellFormed(RegionIdentity id)
    {
        var gymTypes = id.GymTypes.Values.ToHashSet();

        Assert.InRange(id.VillainTeams.Count, 1, 2);
        var roots = new List<string>();
        foreach (var team in id.VillainTeams)
        {
            Assert.StartsWith("Team ", team.Name);
            var root = team.Name["Team ".Length..];
            Assert.DoesNotContain(root, Canon);       // never a canon organisation name
            roots.Add(root);

            Assert.InRange(team.Motif.Count, 1, 2);
            Assert.Equal(team.Motif.Count, team.Motif.Distinct().Count()); // the two types differ
            // At ≤12 badges the candidate pool never runs dry, so the motif avoids every gym type (§7.1).
            Assert.DoesNotContain(team.Motif, gymTypes.Contains);
        }
        Assert.Equal(roots.Count, roots.Distinct(StringComparer.OrdinalIgnoreCase).Count()); // teams distinct
        Assert.Contains(id.Rival, Enum.GetValues<RivalArchetype>());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void TeamsAreWellFormedAcrossTheCorpus(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            AssertWellFormed(Identity(seed, badges));
            checkedCount++;
        }
        Assert.True(checkedCount > 0, "empty corpus — nothing was checked");
    }

    [Fact]
    public void ChampionRollAndArchetypeDistribution()
    {
        // ChampionIsRival is drawn from the (badge-independent) "identity" stream, so vary seeds, not badges.
        int rivalChampions = 0, underdogGivenRival = 0, underdogGivenNot = 0, total = 0;
        for (ulong seed = 1; seed <= 1500; seed++)
        {
            var id = Identity(seed);
            total++;
            if (id.ChampionIsRival)
            {
                rivalChampions++;
                if (id.Rival == RivalArchetype.Underdog) underdogGivenRival++;
            }
            else if (id.Rival == RivalArchetype.Underdog) underdogGivenNot++;
        }

        Assert.True(total > 0 && rivalChampions > 0, "empty corpus — nothing was checked");
        var rate = rivalChampions / (double)total;
        Assert.InRange(rate, 0.20, 0.30); // ~0.25

        // Rival-Champion seeds skew Underdog (0.55) vs the uniform 0.25 otherwise.
        var pUnderdogGivenRival = underdogGivenRival / (double)rivalChampions;
        var pUnderdogGivenNot = underdogGivenNot / (double)(total - rivalChampions);
        Assert.True(pUnderdogGivenRival > pUnderdogGivenNot,
            $"P(Underdog|rival-champ)={pUnderdogGivenRival:F2} should exceed P(Underdog|not)={pUnderdogGivenNot:F2}");
    }

    [Fact]
    public void IdentityIsDeterministic()
    {
        foreach (var seed in new ulong[] { 7, 42, 999, 4242 })
        {
            var a = Identity(seed);
            var b = Identity(seed);
            Assert.Equal(a.ChampionIsRival, b.ChampionIsRival);
            Assert.Equal(a.Rival, b.Rival);
            Assert.Equal(a.VillainTeams.Count, b.VillainTeams.Count);
            for (var i = 0; i < a.VillainTeams.Count; i++)
            {
                Assert.Equal(a.VillainTeams[i].Name, b.VillainTeams[i].Name);
                Assert.Equal(a.VillainTeams[i].Motif, b.VillainTeams[i].Motif);
            }
        }
    }
}
