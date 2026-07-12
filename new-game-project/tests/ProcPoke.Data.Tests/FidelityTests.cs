using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Data.Tests;

/// <summary>
/// Spot-checks the committed <c>data/</c> against independently-known Gen 5 (B2W2) facts. These are the
/// Phase 1 fidelity gate: if a bake drifts from Gen 5, one of these fails.
/// </summary>
[Collection("baked-data")]
public sealed class FidelityTests(BakedDataFixture fx)
{
    private GameData Data => fx.Data;

    private Move MoveByName(string name) => Data.Moves.Values.Single(m => m.Name == name);

    [Fact]
    public void AllGenOneToFiveSpeciesLoad()
    {
        Assert.Equal(649, Data.Species.Count);
        for (var id = 1; id <= 649; id++)
            Assert.True(Data.Species.ContainsKey(id), $"species {id} missing");
    }

    [Fact]
    public void ManifestMatchesThePinInCode()
    {
        // The drift guard: committed data must have been baked from the pin currently in DataPin.
        Assert.Equal(DataManifest.FromPin(Data.Species.Count), Data.Manifest);
        Assert.Equal(DataPin.SourceCommitSha, Data.Manifest.SourceCommitSha);
        Assert.Equal("black-2-white-2", Data.Manifest.VersionGroup);
    }

    [Fact]
    public void EverySpeciesHasOneOrTwoTypes()
    {
        foreach (var s in Data.Species.Values)
            Assert.InRange(s.Types.Count, 1, 2);
    }

    [Fact]
    public void EverySpeciesHasALevelUpLearnset()
    {
        foreach (var s in Data.Species.Values)
        {
            Assert.True(Data.Learnsets.TryGetValue(s.Id, out var entries), $"species {s.Id} has no learnset");
            Assert.Contains(entries!, e => e.Method == LearnMethod.LevelUp);
        }
    }

    [Fact]
    public void BulbasaurIsGenFiveAccurate()
    {
        var b = Data.Species[1];
        Assert.Equal("Bulbasaur", b.Name);
        Assert.Equal(new StatBlock(45, 49, 49, 65, 65, 45), b.BaseStats);
        Assert.Equal([PokeType.Grass, PokeType.Poison], b.Types);
        Assert.Equal(45, b.CatchRate);
        Assert.Equal("medium-slow", b.GrowthRate);
        Assert.Equal("Overgrow", b.Ability1.Name);
        Assert.Equal("Chlorophyll", b.HiddenAbility?.Name);
    }

    [Fact]
    public void ClefairyIsNormalInGenFive()
    {
        // Clefairy was retconned to Fairy in Gen 6; the _past table must rewind it to pure Normal.
        Assert.Equal([PokeType.Normal], Data.Species[35].Types);
    }

    [Fact]
    public void TackleHasItsGenFiveStats()
    {
        var tackle = MoveByName("Tackle");
        Assert.Equal(50, tackle.Power);      // 35 in Gen 1–4, 40 from Gen 7 — 50 is the Gen 5 value
        Assert.Equal(100, tackle.Accuracy);
        Assert.Equal(DamageClass.Physical, tackle.DamageClass);
        Assert.Equal(PokeType.Normal, tackle.Type);
    }

    [Fact]
    public void ThunderboltHasItsGenFivePower()
    {
        // Thunderbolt was 95 power through Gen 5, nerfed to 90 in Gen 6.
        Assert.Equal(95, MoveByName("Thunderbolt").Power);
        Assert.Equal(PokeType.Electric, MoveByName("Thunderbolt").Type);
    }

    [Fact]
    public void StatusMovesHaveNoPowerOrAccuracyWhereExpected()
    {
        var sd = MoveByName("Swords Dance");
        Assert.Equal(DamageClass.Status, sd.DamageClass);
        Assert.Null(sd.Power);
    }

    [Theory]
    [InlineData(PokeType.Electric, PokeType.Ground, 0)]   // immunity
    [InlineData(PokeType.Ghost, PokeType.Steel, 50)]      // Gen 5: Steel resists Ghost (removed Gen 6)
    [InlineData(PokeType.Dark, PokeType.Steel, 50)]       // Gen 5: Steel resists Dark (removed Gen 6)
    [InlineData(PokeType.Ghost, PokeType.Psychic, 200)]
    [InlineData(PokeType.Water, PokeType.Fire, 200)]
    [InlineData(PokeType.Normal, PokeType.Ghost, 0)]
    [InlineData(PokeType.Normal, PokeType.Normal, 100)]
    public void TypeChartMatchesGenFive(PokeType attacker, PokeType defender, int expectedPercent)
        => Assert.Equal(expectedPercent / 100.0, Data.TypeChart.Effectiveness(attacker, defender));

    [Fact]
    public void KadabraEvolvesViaLinkCable()
    {
        var rule = Data.Evolutions.Single(e => e.FromSpeciesId == 64 && e.ToSpeciesId == 65);
        Assert.Equal(EvolutionTrigger.LinkCable, rule.Trigger);
        Assert.True(rule.WasTrade);
    }

    [Fact]
    public void OnixKeepsItsHeldItemThroughTheLinkCableTransform()
    {
        var rule = Data.Evolutions.Single(e => e.FromSpeciesId == 95 && e.ToSpeciesId == 208);
        Assert.Equal(EvolutionTrigger.LinkCable, rule.Trigger);
        Assert.NotNull(rule.HeldItemId); // Metal Coat must survive the transform
    }

    [Fact]
    public void LeafeonAndGlaceonUseTheGenFiveRockMethod()
    {
        // In Gen 5 these are level-up-near-a-rock, not the Gen 8 stones.
        var leafeon = Data.Evolutions.Single(e => e.ToSpeciesId == 470);
        var glaceon = Data.Evolutions.Single(e => e.ToSpeciesId == 471);
        Assert.Equal(EvolutionTrigger.LevelUp, leafeon.Trigger);
        Assert.True(leafeon.NeedsSpecialRock);
        Assert.Equal(EvolutionTrigger.LevelUp, glaceon.Trigger);
        Assert.True(glaceon.NeedsSpecialRock);
    }

    [Fact]
    public void NaturesAreCompleteAndCorrect()
    {
        Assert.Equal(25, Data.Natures.Count);
        var adamant = Data.Natures.Single(n => n.Name == "Adamant");
        Assert.Equal(Stat.Attack, adamant.IncreasedStat);
        Assert.Equal(Stat.SpecialAttack, adamant.DecreasedStat);
        var hardy = Data.Natures.Single(n => n.Name == "Hardy");
        Assert.Null(hardy.IncreasedStat); // neutral
    }

    [Fact]
    public void GrowthCurvesReachTheirKnownTotals()
    {
        // "medium" is the x^3 curve: 100^3 = 1,000,000 total experience at level 100.
        Assert.Equal(1_000_000, Data.GrowthRates["medium"].CumulativeExperience[100]);
        Assert.Equal(0, Data.GrowthRates["medium"].CumulativeExperience[1]);
    }

    [Fact]
    public void NameBlocklistCarriesCanonLocationNames()
    {
        Assert.True(Data.NameBlocklist.Count > 200);
        Assert.Contains("Pallet Town", Data.NameBlocklist);
    }
}
