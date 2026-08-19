using ProcPoke.Battle;
using ProcPoke.Generation;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Owning Pokémon: the party/PC overflow rule every acquisition shares, and the dex record it ticks.
/// </summary>
public sealed class PokedexAndStorageTests
{
    private static GeneratedRegion Region(ulong seed = 42)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);

    private static OverworldSession Session(GeneratedRegion region)
        => new(region, NewGameIdentity.Create(CharacterChoice.CharacterA, "Player"));

    private static BattleProgressionState Member(int speciesId, int level = 10)
        => BattleProgressionFactory.AtLevel(TestData.Data, speciesId, level);

    /// <summary>Baked species ids, so a test can gain more Pokémon than a party holds.</summary>
    private static IReadOnlyList<int> SpeciesIds(int count)
        => TestData.Data.Species.Keys.OrderBy(id => id).Take(count).ToArray();

    [Fact]
    public void PartyFillsFirstAndThenTheBoxes()
    {
        var session = Session(Region());
        var species = SpeciesIds(Party.MaxSize + 2);

        for (var i = 0; i < Party.MaxSize; i++)
        {
            var result = session.GainPokemon(TestData.Data, Member(species[i]));
            Assert.Equal(PokemonGainKind.JoinedParty, result.Kind);
            Assert.Null(result.BoxIndex);
        }
        Assert.Equal(Party.MaxSize, session.Party.Members.Count);

        var overflow = session.GainPokemon(TestData.Data, Member(species[Party.MaxSize]));
        Assert.Equal(PokemonGainKind.SentToBox, overflow.Kind);
        Assert.Equal(0, overflow.BoxIndex);
        Assert.Contains("Box 1", overflow.Message);
        Assert.Equal(Party.MaxSize, session.Party.Members.Count);
        Assert.Single(session.Storage.Stored);

        // Either way it counts as caught.
        Assert.All(species.Take(Party.MaxSize + 1), id => Assert.True(session.Pokedex.HasCaught(id)));
        Assert.All(species.Take(Party.MaxSize + 1), id => Assert.True(session.Pokedex.HasSeen(id)));
        Assert.False(session.Pokedex.HasCaught(species[^1]));
    }

    [Fact]
    public void DepositsRollOverIntoLaterBoxesAndStopWhenStorageIsFull()
    {
        var storage = new PcStorage();
        for (var i = 0; i < PcStorage.Capacity; i++)
        {
            Assert.Equal(i / PcStorage.BoxCapacity, storage.NextBox);
            storage.Deposit(Member(1));
        }

        Assert.True(storage.IsFull);
        Assert.Null(storage.NextBox);
        Assert.Throws<InvalidOperationException>(() => storage.Deposit(Member(1)));
        Assert.Equal(PcStorage.BoxCapacity, storage.BoxAt(0).Count);
        Assert.Equal(PcStorage.BoxCapacity, storage.BoxAt(PcStorage.BoxCount - 1).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.BoxAt(PcStorage.BoxCount));
    }

    [Fact]
    public void WithdrawingTakesThePokemonOutOfStorage()
    {
        var storage = new PcStorage();
        var species = SpeciesIds(3);
        foreach (var id in species) storage.Deposit(Member(id));

        var taken = storage.Withdraw(1);

        Assert.Equal(species[1], taken.SpeciesId);
        Assert.Equal([species[0], species[2]], storage.Stored.Select(member => member.SpeciesId));
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.Withdraw(2));
    }

    [Fact]
    public void AFullPartyAndFullBoxesTurnAPokemonAway()
    {
        var session = Session(Region());
        for (var i = 0; i < Party.MaxSize; i++) session.Party.Add(Member(1));
        for (var i = 0; i < PcStorage.Capacity; i++) session.Storage.Deposit(Member(1));

        var result = session.GainPokemon(TestData.Data, Member(4));

        Assert.Equal(PokemonGainKind.NoRoom, result.Kind);
        Assert.False(result.Accepted);
        Assert.False(session.Pokedex.HasCaught(4));
    }

    /// <summary>A full party used to leave a revived fossil stuck in the lab; it goes to a box instead.</summary>
    [Fact]
    public void AFossilRevivesIntoABoxWhenThePartyIsFull()
    {
        var region = Region();
        var session = Session(region);
        for (var i = 0; i < Party.MaxSize; i++) session.Party.Add(Member(1));
        var collected = session.CollectFossils(region.Dex.FossilAreaId);
        Assert.NotEmpty(collected);

        var revived = session.ReviveFossils(TestData.Data);

        Assert.Equal(collected, revived);
        Assert.Empty(session.PendingFossilRevivals);
        Assert.Equal(
            collected.Select(pickup => pickup.SpeciesId),
            session.Storage.Stored.Select(member => member.SpeciesId));
    }

    [Fact]
    public void SeenAndCaughtAreSeparateHalvesOfTheRecord()
    {
        var session = Session(Region());

        session.MarkSpeciesSeen(25);

        Assert.True(session.Pokedex.HasSeen(25));
        Assert.False(session.Pokedex.HasCaught(25));

        session.GainPokemon(TestData.Data, Member(25));
        Assert.True(session.Pokedex.HasCaught(25));
    }

    [Fact]
    public void TheDiplomaNeedsEveryDexSlotIncludingTheLegendaries()
    {
        var region = Region();
        var session = Session(region);
        var dex = DexDiploma.RegionalDex(region);

        Assert.Equal(dex.Count, dex.Distinct().Count());
        Assert.All(
            region.Dex.Entries.Select(entry => entry.SpeciesId).Distinct(),
            speciesId => Assert.Contains(speciesId, dex));
        Assert.All(
            region.Special.Legendaries.Select(legendary => legendary.SpeciesId),
            speciesId => Assert.Contains(speciesId, dex));

        // Catching all but one leaves the diploma unearned.
        foreach (var speciesId in dex.Take(dex.Count - 1)) session.Pokedex.MarkCaught(speciesId);
        Assert.Equal(dex.Count - 1, DexDiploma.CaughtCount(region, session.Pokedex));
        Assert.False(DexDiploma.IsEarned(region, session.Pokedex));

        session.Pokedex.MarkCaught(dex[^1]);
        Assert.True(DexDiploma.IsEarned(region, session.Pokedex));
    }
}
