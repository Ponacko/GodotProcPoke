using ProcPoke.Data;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Roster;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>Starter triangle invariants (GDD §5.3, ticket 4b): the chosen triangle's three corners are
/// distinct, genuinely 3-stage, level-only-completable evolution lines within the Roster Cap, capped at
/// 540 final BST and close together, and only templates with a non-empty pool under the cap are ever
/// chosen (Classic is always the floor).</summary>
public class StarterSelectorTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8, int rosterCap = 5)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges, RosterCap = rosterCap }, TestData.Data);

    private static int Bst(PokemonSpecies s) => s.BaseStats.Hp + s.BaseStats.Attack + s.BaseStats.Defense
        + s.BaseStats.SpecialAttack + s.BaseStats.SpecialDefense + s.BaseStats.Speed;

    /// <summary>Independently verifies (re-deriving from raw evolution rules, not trusting the selector's
    /// internals) that a corner really is a 3-stage, level-only-completable line ending in its type.</summary>
    private static void AssertGenuineStarterLine(StarterCorner corner, int rosterCap)
    {
        var data = TestData.Data;
        var (baseId, midId, finalId) = (corner.BaseSpeciesId, corner.MidSpeciesId, corner.FinalSpeciesId);

        var toMid = Assert.Single(data.Evolutions, e => e.FromSpeciesId == baseId && e.ToSpeciesId == midId);
        var toFinal = Assert.Single(data.Evolutions, e => e.FromSpeciesId == midId && e.ToSpeciesId == finalId);
        Assert.Equal(EvolutionTrigger.LevelUp, toMid.Trigger);
        Assert.Equal(EvolutionTrigger.LevelUp, toFinal.Trigger);
        // Friendship evolutions stay Trigger==LevelUp with MinHappiness set (unlike trade, rewritten to
        // its own LinkCable trigger) — a real level-only line needs neither step gated on friendship.
        Assert.Null(toMid.MinHappiness);
        Assert.Null(toFinal.MinHappiness);
        Assert.DoesNotContain(data.Evolutions, e => e.FromSpeciesId == finalId); // exactly 3 stages
        Assert.DoesNotContain(data.Evolutions, e => e.ToSpeciesId == baseId);    // base really is a base

        var finalSpecies = data.Species[finalId];
        Assert.True(Bst(finalSpecies) <= 540, $"final stage #{finalId} BST exceeds 540");
        Assert.Contains(corner.Type, finalSpecies.Types);

        foreach (var id in corner.LineSpeciesIds)
            Assert.True(SpeciesGeneration.Of(id) <= rosterCap, $"species #{id} exceeds Roster Cap {rosterCap}");
    }

    private static List<(PokeType Type, string Line)> Fingerprint(StarterPlan plan)
        => plan.Corners.Select(c => (c.Type, string.Join(",", c.LineSpeciesIds))).ToList();

    [Fact]
    public void GenerationIsDeterministicWithStarters()
    {
        var a = Generate(2026).Starters;
        var b = Generate(2026).Starters;
        Assert.Equal(a.Triangle, b.Triangle);
        Assert.Equal(Fingerprint(a), Fingerprint(b));
    }

    [Fact]
    public void RegionGraphTextPrintsTheStarters()
    {
        var region = Generate(42);
        var text = RegionGraphText.Render(region.Graph, starters: region.Starters);
        Assert.Contains("Starters", text);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void ThreeDistinctValidStartersWithCloseBstsAreChosen(int badges)
    {
        for (ulong seed = 1; seed <= 2000; seed++)
        {
            var starters = Generate(seed, badges).Starters;
            Assert.Equal(3, starters.Corners.Count);

            var allIds = starters.Corners.SelectMany(c => c.LineSpeciesIds).ToList();
            Assert.Equal(allIds.Count, allIds.Distinct().Count()); // no species shared across corners

            foreach (var corner in starters.Corners)
                AssertGenuineStarterLine(corner, rosterCap: 5);

            var finalBsts = starters.Corners.Select(c => Bst(TestData.Data.Species[c.FinalSpeciesId])).ToList();
            var spread = finalBsts.Max() - finalBsts.Min();
            Assert.True(spread <= StarterSelector.MaxAcceptableBstSpread,
                $"seed {seed}/{badges}: final BST spread {spread} exceeds threshold {StarterSelector.MaxAcceptableBstSpread}");
        }
    }

    /// <summary>Ground truth from the pinned Gen 1–5 data: Elemental's corner pools are non-empty from
    /// Roster Cap 3 on; Mind &amp; Body's Dark corner only clears at Cap 5. Classic clears at every cap.</summary>
    private static readonly IReadOnlyDictionary<int, IReadOnlySet<StarterTriangle>> AvailableAtCap =
        new Dictionary<int, IReadOnlySet<StarterTriangle>>
        {
            [1] = new HashSet<StarterTriangle> { StarterTriangle.Classic },
            [2] = new HashSet<StarterTriangle> { StarterTriangle.Classic },
            [3] = new HashSet<StarterTriangle> { StarterTriangle.Classic, StarterTriangle.Elemental },
            [4] = new HashSet<StarterTriangle> { StarterTriangle.Classic, StarterTriangle.Elemental },
            [5] = new HashSet<StarterTriangle> { StarterTriangle.Classic, StarterTriangle.Elemental, StarterTriangle.MindAndBody },
        };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void OnlyTemplatesAvailableUnderTheCapAreEverChosen(int rosterCap)
    {
        var available = AvailableAtCap[rosterCap];
        for (ulong seed = 1; seed <= 2000; seed++)
        {
            var starters = Generate(seed, badges: 8, rosterCap: rosterCap).Starters;
            Assert.Contains(starters.Triangle, available);
            foreach (var corner in starters.Corners)
                AssertGenuineStarterLine(corner, rosterCap);
        }
    }

    [Fact]
    public void ClassicIsAlwaysAvailableAsTheGuaranteedFloor()
    {
        for (var rosterCap = 1; rosterCap <= 5; rosterCap++)
            Assert.Contains(StarterTriangle.Classic, AvailableAtCap[rosterCap]);
    }
}
