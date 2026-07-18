using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Ticket 4a-2: regional dex selection &amp; numbering. Invariants are re-derived independently — families
/// are rebuilt from raw evolution data, and the availability order is recomputed — rather than trusting the
/// selector's own bookkeeping (the dex is a shared contract for 4c/6/7).
/// </summary>
public class DexSelectorTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8, int dexSize = 150, int cap = 5)
        => RegionGenerator.Generate(
            new GenerationSettings { Seed = seed, BadgeCount = badges, DexSize = dexSize, RosterCap = cap },
            TestData.Data);

    private static Dictionary<int, int> NumberBySpecies(DexPlan dex)
        => dex.Entries.ToDictionary(e => e.SpeciesId, e => e.Number);

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void DexIsWellFormedAcrossTheCorpus(int badges)
    {
        var data = TestData.Data;
        var families = EvolutionFamilies.Build(data, 5); // default cap
        var checkedCount = 0;

        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = Generate(seed, badges);
            var dex = region.Dex;
            var numberOf = NumberBySpecies(dex);
            var dexSpecies = numberOf.Keys.ToHashSet();

            // Exactly DexSize entries numbered 1..DexSize, no gaps or dupes.
            Assert.Equal(region.Settings.DexSize, dex.Entries.Count);
            Assert.Equal(dex.Entries.Count, dexSpecies.Count); // species unique
            Assert.Equal(Enumerable.Range(1, dex.Entries.Count), dex.Entries.Select(e => e.Number).OrderBy(n => n));

            // #1–9 are the three starter lines, base/mid/final per corner in order.
            var starterIds = region.Starters.Corners.SelectMany(c => c.LineSpeciesIds).ToList();
            for (var i = 0; i < 9; i++)
            {
                Assert.Equal(i + 1, dex.Entries[i].Number);
                Assert.Equal(starterIds[i], dex.Entries[i].SpeciesId);
            }

            // No broken families: every dex species' full in-cap family is present, consecutive, in order.
            foreach (var species in dexSpecies)
            {
                var family = Assert.Single(families, f => f.Members.Contains(species));
                Assert.All(family.Members, m => Assert.Contains(m, dexSpecies));
                var first = numberOf[family.Members[0]];
                for (var i = 0; i < family.Members.Count; i++)
                    Assert.Equal(first + i, numberOf[family.Members[i]]); // consecutive AND in family order
            }

            // Numbering equals first-availability order: each area's block starts after every earlier area's.
            var order = AvailabilityOrder.Of(region.Graph, GatingGenerator.OffSpineAnchors(region.Graph));
            var runningMax = 0;
            foreach (var areaId in order.Where(dex.SpeciesByArea.ContainsKey))
            {
                var nums = dex.SpeciesByArea[areaId].Select(sp => numberOf[sp]).ToList();
                Assert.True(nums.Min() > runningMax,
                    $"seed {seed}/{badges}: area {areaId} block overlaps an earlier area");
                runningMax = Math.Max(runningMax, nums.Max());
            }

            // Exactly two fossil families, seated together at the fossil area.
            var fossilFamilies = families.Where(f => f.Members.Any(dex.FossilFamilySpecies.Contains)).ToList();
            Assert.Equal(2, fossilFamilies.Count);
            Assert.All(fossilFamilies, f => Assert.True(f.IsFossil, $"seed {seed}: reserved a non-fossil family"));
            Assert.Equal(fossilFamilies.SelectMany(f => f.Members).ToHashSet(), dex.FossilFamilySpecies.ToHashSet());
            Assert.All(dex.FossilFamilySpecies, sp => Assert.Contains(sp, dex.SpeciesByArea[dex.FossilAreaId]));

            checkedCount++;
        }
        Assert.True(checkedCount > 0, "empty corpus — nothing was checked");
    }

    [Fact]
    public void EveryTypeHasARepresentativeAtDefaultSettings()
    {
        // Default settings only (DexSize 150, cap 5, badges 8): Gen 1–5 spans all 17 types with the dex's
        // type-coverage bonus. (Not asserted at cap 1 — Gen 1 has no Dark-type.)
        var allTypes = Enum.GetValues<PokeType>().ToHashSet();
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 150; seed++)
        {
            var dex = Generate(seed).Dex;
            var present = dex.Entries.SelectMany(e => TestData.Data.Species[e.SpeciesId].Types).ToHashSet();
            Assert.Equal(allTypes, present);
            checkedCount++;
        }
        Assert.True(checkedCount > 0, "empty corpus — nothing was checked");
    }

    [Fact]
    public void SmallerDexStillFillsExactlyAndKeepsFamiliesWhole()
    {
        // A non-default DexSize still lands exactly on target and never splits a family.
        var families = EvolutionFamilies.Build(TestData.Data, 5);
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var region = Generate(seed, dexSize: 60);
            var dex = region.Dex;
            Assert.Equal(60, dex.Entries.Count);
            var dexSpecies = dex.Entries.Select(e => e.SpeciesId).ToHashSet();
            foreach (var species in dexSpecies)
            {
                var family = Assert.Single(families, f => f.Members.Contains(species));
                Assert.All(family.Members, m => Assert.Contains(m, dexSpecies));
            }
        }
    }

    [Fact]
    public void InfeasibleDexSizeClampsToWholeFamiliesWithoutCrashing()
    {
        // DexSize 150 at RosterCap 1 asks for more species than Gen 1 supplies (≈137 non-legendary,
        // non-starter). The dex clamps to the pool rather than crashing, and never splits a family.
        var families = EvolutionFamilies.Build(TestData.Data, 1);
        var dex = Generate(1, cap: 1, dexSize: 150).Dex;

        Assert.InRange(dex.Entries.Count, 10, 149); // clamped below DexSize, still a real dex
        var dexSpecies = dex.Entries.Select(e => e.SpeciesId).ToHashSet();
        Assert.Equal(dex.Entries.Count, dexSpecies.Count);
        Assert.Equal(Enumerable.Range(1, dex.Entries.Count), dex.Entries.Select(e => e.Number).OrderBy(n => n));
        foreach (var species in dexSpecies)
        {
            var family = Assert.Single(families, f => f.Members.Contains(species));
            Assert.All(family.Members, m => Assert.Contains(m, dexSpecies));
        }
    }

    [Fact]
    public void DexIsDeterministic()
    {
        foreach (var seed in new ulong[] { 7, 42, 999, 4242 })
            Assert.Equal(Generate(seed).Dex.Entries, Generate(seed).Dex.Entries);
    }
}
