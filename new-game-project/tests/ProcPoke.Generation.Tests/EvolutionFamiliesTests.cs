using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Ticket 4a-1: the shared evolution-family pool and the canonical availability order. Both are pure
/// functions — the family checks run against the pinned <see cref="TestData.Data"/>; the ordering is fuzzed
/// over the standard corpus. Downstream tickets (4a-2 dex, 4c specials, 6 encounters, 7 trainers/bosses) are
/// written against these shapes, so the ground truth here is re-derived independently, not trusted.
/// </summary>
public class EvolutionFamiliesTests
{
    private static int Bst(PokemonSpecies s) => s.BaseStats.Hp + s.BaseStats.Attack + s.BaseStats.Defense
        + s.BaseStats.SpecialAttack + s.BaseStats.SpecialDefense + s.BaseStats.Speed;

    private static EvolutionFamily FamilyOf(IReadOnlyList<EvolutionFamily> families, int speciesId)
        => Assert.Single(families, f => f.Members.Contains(speciesId));

    [Fact]
    public void EeveeFamilyBranchesAndTruncatesWithTheCap()
    {
        // Eevee + its 7 in-cap evolutions at cap 5; Sylveon (700, Gen 6) is out of the pinned dataset anyway.
        var atCap5 = FamilyOf(EvolutionFamilies.Build(TestData.Data, 5), 133);
        Assert.Equal(new HashSet<int> { 133, 134, 135, 136, 196, 197, 470, 471 }, atCap5.Members.ToHashSet());

        // At cap 1 only the Gen-1 Eeveelutions remain (Espeon/Umbreon are Gen 2, Leafeon/Glaceon Gen 4).
        var atCap1 = FamilyOf(EvolutionFamilies.Build(TestData.Data, 1), 133);
        Assert.Equal(new HashSet<int> { 133, 134, 135, 136 }, atCap1.Members.ToHashSet());

        // Eevee is the root; every evolution is seated right after it, before any is seated before it.
        Assert.Equal(133, atCap5.Members[0]);
    }

    [Fact]
    public void FriendshipEvolutionIsStillAFamilyEdge()
    {
        // Golbat -> Crobat (42 -> 169) is a friendship evolution: Trigger==LevelUp with MinHappiness set.
        // Families are about dex adjacency, not level-only completability, so it must stay in one family.
        var edge = Assert.Single(TestData.Data.Evolutions, e => e.FromSpeciesId == 42 && e.ToSpeciesId == 169);
        Assert.Equal(EvolutionTrigger.LevelUp, edge.Trigger);
        Assert.NotNull(edge.MinHappiness);

        var families = EvolutionFamilies.Build(TestData.Data, 5);
        var family = FamilyOf(families, 42);
        Assert.Contains(41, family.Members);  // Zubat, the base
        Assert.Contains(169, family.Members); // Crobat, reached only via the friendship edge
    }

    [Fact]
    public void ThreeFossilFamiliesAtCapOne()
    {
        // Gen 1 fossils: Omanyte, Kabuto, Aerodactyl lines (the later-gen fossils are out of cap).
        var fossil = EvolutionFamilies.Build(TestData.Data, 1).Where(f => f.IsFossil).ToList();
        Assert.Equal(3, fossil.Count);
        Assert.All(fossil, f => Assert.Contains(f.Members, m => m is 138 or 140 or 142));
    }

    [Fact]
    public void FinalBstMatchesAHandComputedFamily()
    {
        // Dratini -> Dragonair -> Dragonite (147 -> 148 -> 149); Dragonite's BST is 600.
        var family = FamilyOf(EvolutionFamilies.Build(TestData.Data, 5), 147);
        Assert.Equal(new[] { 147, 148, 149 }, family.Members);
        Assert.Equal(600, Bst(TestData.Data.Species[149]));
        Assert.Equal(600, family.FinalBst);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void EveryInCapSpeciesBelongsToExactlyOneFamily(int rosterCap)
    {
        var families = EvolutionFamilies.Build(TestData.Data, rosterCap);
        var inCap = TestData.Data.Species.Keys.Where(id => SpeciesGeneration.Of(id) <= rosterCap).ToHashSet();

        var membership = families.SelectMany(f => f.Members).ToList();
        Assert.Equal(inCap.Count, membership.Count);              // no member counted twice or dropped
        Assert.Equal(inCap.Count, membership.Distinct().Count()); // members globally unique
        Assert.Equal(inCap, membership.ToHashSet());              // exactly the in-cap set

        foreach (var f in families)
        {
            Assert.NotEmpty(f.Members);
            Assert.Equal(f.Members.Count, f.Members.Distinct().Count()); // unique within a family
        }
    }

    [Fact]
    public void TruncatedFamilyIsStillCompleteAtItsCap()
    {
        // Electabuzz (125) evolves into Electivire (466, Gen 4). At cap 3 the family stops at Electabuzz —
        // a complete, valid family at that cap; Electivire only joins once the cap admits Gen 4.
        var atCap3 = FamilyOf(EvolutionFamilies.Build(TestData.Data, 3), 125);
        Assert.DoesNotContain(466, atCap3.Members);

        var atCap5 = FamilyOf(EvolutionFamilies.Build(TestData.Data, 5), 125);
        Assert.Contains(466, atCap5.Members);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AvailabilityOrderIsAFirstAvailabilityPermutation(int seedGroup)
    {
        var badgeCounts = new[] { 4, 8, 12 };
        var checkedCount = 0;
        for (ulong seed = (ulong)((seedGroup - 1) * 200 + 1); seed <= (ulong)(seedGroup * 200); seed++)
            foreach (var badges in badgeCounts)
            {
                var region = RegionGenerator.Generate(
                    new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
                var anchors = GatingGenerator.OffSpineAnchors(region.Graph);
                var order = AvailabilityOrder.Of(region.Graph, anchors);

                var allIds = region.Graph.Areas.Select(a => a.Id).ToHashSet();
                Assert.Equal(allIds.Count, order.Count);              // every area once
                Assert.Equal(allIds, order.ToHashSet());              // no strays, no dupes

                // Critical-path ids appear in PathIndex order.
                var pathOrder = region.Graph.CriticalPath.Select(a => a.Id).ToList();
                var pathInResult = order.Where(id => region.Graph[id].OnCriticalPath).ToList();
                Assert.Equal(pathOrder, pathInResult);

                // Each off-spine id appears after its anchor and before the next critical-path area.
                var pos = order.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i);
                foreach (var (offId, anchorPathIndex) in anchors)
                {
                    var anchorArea = region.Graph.CriticalPath.Single(a => a.PathIndex == anchorPathIndex);
                    Assert.True(pos[offId] > pos[anchorArea.Id],
                        $"seed {seed}/{badges}: off-spine {offId} precedes its anchor {anchorArea.Id}");

                    var nextPathArea = region.Graph.CriticalPath
                        .FirstOrDefault(a => a.PathIndex > anchorPathIndex);
                    if (nextPathArea is not null)
                        Assert.True(pos[offId] < pos[nextPathArea.Id],
                            $"seed {seed}/{badges}: off-spine {offId} appears after the next path area {nextPathArea.Id}");
                }
                checkedCount++;
            }
        Assert.True(checkedCount > 0, "empty corpus — nothing was checked");
    }
}
