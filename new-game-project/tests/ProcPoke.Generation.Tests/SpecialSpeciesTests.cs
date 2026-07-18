using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Ticket 4c: fossils &amp; legendaries. Fossil roots are re-derived from the evolution data and the eligible
/// dungeon set is recomputed independently, rather than trusting the pass. Legendaries are optional content
/// (§5.5) and must never affect solvability.
/// </summary>
public class SpecialSpeciesTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8, int cap = 5)
        => RegionGenerator.Generate(
            new GenerationSettings { Seed = seed, BadgeCount = badges, RosterCap = cap }, TestData.Data);

    private static int Bst(PokemonSpecies s) => s.BaseStats.Hp + s.BaseStats.Attack + s.BaseStats.Defense
        + s.BaseStats.SpecialAttack + s.BaseStats.SpecialDefense + s.BaseStats.Speed;

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void SpecialSpeciesAreWellFormedAcrossTheCorpus(int badges)
    {
        var data = TestData.Data;
        var checkedCount = 0;
        var oneDungeonEachSeen = 0;

        for (ulong seed = 1; seed <= 150; seed++)
        {
            var region = Generate(seed, badges);
            var special = region.Special;
            var dex = region.Dex;

            // --- Fossils: exactly two, both at the fossil area, each the root of a reserved fossil family.
            Assert.Equal(2, special.Fossils.Count);
            Assert.All(special.Fossils, f => Assert.Equal(dex.FossilAreaId, f.AreaId));

            var families = EvolutionFamilies.Build(data, region.Settings.RosterCap);
            var expectedRoots = families
                .Where(f => f.Members.Any(dex.FossilFamilySpecies.Contains))
                .Select(f => f.Members[0])
                .ToHashSet();
            Assert.Equal(expectedRoots, special.Fossils.Select(f => f.SpeciesId).ToHashSet());

            // --- Legendaries: exactly four, distinct, in cap, legendary-not-mythical.
            var legendaries = special.Legendaries;
            Assert.Equal(4, legendaries.Count);
            Assert.Equal(4, legendaries.Select(l => l.SpeciesId).Distinct().Count());
            foreach (var l in legendaries)
            {
                var s = data.Species[l.SpeciesId];
                Assert.True(s.IsLegendary && !s.IsMythical, $"seed {seed}: #{l.SpeciesId} not a non-mythical legendary");
                Assert.True(SpeciesGeneration.Of(l.SpeciesId) <= region.Settings.RosterCap, $"seed {seed}: legendary out of cap");
            }

            // Levels and appended dex numbers by placement index; weakest BST earliest.
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal(new[] { 50, 55, 65, 70 }[i], legendaries[i].Level);
                Assert.Equal(region.Settings.DexSize + 1 + i, legendaries[i].DexNumber);
            }
            for (var i = 1; i < 4; i++)
                Assert.True(Bst(data.Species[legendaries[i].SpeciesId]) >= Bst(data.Species[legendaries[i - 1].SpeciesId]),
                    $"seed {seed}: legendaries not weakest-BST-first");

            // Every legendary lands in a real area; when ≥4 primary-eligible dungeons exist, one per dungeon.
            var order = AvailabilityOrder.Of(region.Graph, GatingGenerator.OffSpineAnchors(region.Graph));
            var eligible = order.Where(id => !region.Graph[id].OnCriticalPath
                && region.Graph[id].Archetype is AreaArchetype.Tower or AreaArchetype.DeepCave
                && id != dex.FossilAreaId).ToList();
            Assert.All(legendaries, l => Assert.Contains(l.AreaId, region.Graph.Areas.Select(a => a.Id)));
            if (eligible.Count >= 4)
            {
                Assert.Equal(4, legendaries.Select(l => l.AreaId).Distinct().Count());
                Assert.All(legendaries, l => Assert.Contains(l.AreaId, eligible));
                oneDungeonEachSeen++;
            }

            // Legendaries are optional — solvability is untouched.
            Assert.True(GatingValidator.IsSolvable(region.Graph, region.Gating),
                $"seed {seed}/{badges}: special-species pass disturbed solvability");

            checkedCount++;
        }

        Assert.True(checkedCount > 0, "empty corpus — nothing was checked");
        // Small regions (badges 4/8) rarely hold ≥4 destination dungeons once the fossil DeepCave is spent,
        // so round-robin is their normal path; only large regions exercise the one-per-dungeon branch.
        if (badges == 12)
            Assert.True(oneDungeonEachSeen > 0, "no large-region seed had ≥4 eligible dungeons — one-per-dungeon untested");
    }

    [Fact]
    public void LegendariesRespectRosterCapOne()
    {
        // At cap 1 the only non-mythical legendaries are the Gen-1 four (Mew is mythical, excluded).
        var region = Generate(1, cap: 1);
        var ids = region.Special.Legendaries.Select(l => l.SpeciesId).ToHashSet();
        Assert.Equal(new HashSet<int> { 144, 145, 146, 150 }, ids);
    }

    [Fact]
    public void SpecialSpeciesIsDeterministic()
    {
        foreach (var seed in new ulong[] { 7, 42, 999, 4242 })
        {
            var a = Generate(seed).Special;
            var b = Generate(seed).Special;
            Assert.Equal(a.Fossils, b.Fossils);
            Assert.Equal(a.Legendaries, b.Legendaries);
        }
    }
}
