using ProcPoke.Battle;
using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Battle.Tests;

public sealed class BattlePokemonFactoryTests
{
    private static readonly GameData Data = GameDataLoader.Load(FindDataDir());

    [Fact]
    public void Gen5StatFormulaBuildsExpectedNeutralBulbasaurStats()
    {
        var pokemon = BattlePokemonFactory.Create(Data, 1, 50, new BattlePokemonBuildOptions
        {
            IndividualValues = new StatBlock(31, 31, 31, 31, 31, 31),
            EffortValues = new StatBlock(252, 252, 0, 0, 0, 0),
            NatureId = 1,
            MoveIds = [33],
        });

        Assert.Equal(152, pokemon.MaxHp);
        Assert.Equal(101, pokemon.Attack);
        Assert.Equal(69, pokemon.Defense);
        Assert.Equal(85, pokemon.SpecialAttack);
        Assert.Equal(85, pokemon.SpecialDefense);
        Assert.Equal(65, pokemon.Speed);
    }

    [Fact]
    public void NatureRaisesAndLowersTheExpectedBattleStats()
    {
        var pokemon = BattlePokemonFactory.Create(Data, 1, 50, new BattlePokemonBuildOptions
        {
            IndividualValues = new StatBlock(31, 31, 31, 31, 31, 31),
            NatureId = 2, // Bold: +Defense, -Attack.
            MoveIds = [33],
        });

        Assert.Equal(62, pokemon.Attack);
        Assert.Equal(75, pokemon.Defense);
    }

    [Fact]
    public void StartingHpSeedsCurrentHpButNeverRaisesOrLowersMaxHp()
    {
        var full = BattlePokemonFactory.Create(Data, 1, 50, new BattlePokemonBuildOptions { MoveIds = [33] });

        var partial = BattlePokemonFactory.Create(Data, 1, 50,
            new BattlePokemonBuildOptions { MoveIds = [33], StartingHp = 10 });
        Assert.Equal(full.MaxHp, partial.MaxHp);
        Assert.Equal(10, partial.Hp);

        // A stale/inflated starting HP (e.g. from a species swap) clamps down to the real max, it never
        // grants extra HP above what the fresh calculation says this Pokémon should have.
        var overfull = BattlePokemonFactory.Create(Data, 1, 50,
            new BattlePokemonBuildOptions { MoveIds = [33], StartingHp = full.MaxHp + 500 });
        Assert.Equal(full.MaxHp, overfull.Hp);
        Assert.Equal(full.MaxHp, overfull.MaxHp);

        Assert.Throws<ArgumentOutOfRangeException>(() => BattlePokemonFactory.Create(Data, 1, 50,
            new BattlePokemonBuildOptions { MoveIds = [33], StartingHp = -1 }));
    }

    [Fact]
    public void MaxHpOfMatchesTheFullFactoryWithoutBuildingMovesOrANature()
    {
        var full = BattlePokemonFactory.Create(Data, 1, 50, new BattlePokemonBuildOptions
        {
            IndividualValues = new StatBlock(31, 31, 31, 31, 31, 31),
            EffortValues = new StatBlock(252, 0, 0, 0, 0, 0),
            MoveIds = [33],
        });

        var maxHp = BattlePokemonFactory.MaxHpOf(
            Data, 1, 50, new StatBlock(252, 0, 0, 0, 0, 0), new StatBlock(31, 31, 31, 31, 31, 31));

        Assert.Equal(full.MaxHp, maxHp);
        Assert.Throws<ArgumentException>(() => BattlePokemonFactory.MaxHpOf(Data, speciesId: 99_999, level: 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => BattlePokemonFactory.MaxHpOf(Data, 1, level: 101));
    }

    [Fact]
    public void MissingMovesAreSelectedFromTheLatestLevelUpLearnsetEntries()
    {
        var pokemon = BattlePokemonFactory.Create(Data, 1, 40);

        Assert.Equal(new[] { 38, 388, 235, 402 }, pokemon.Moves.Select(move => move.Id));
    }

    [Fact]
    public void ExplicitMoveSlotsAndInvalidEvTotalsAreValidated()
    {
        var pokemon = BattlePokemonFactory.Create(Data, 1, 5, new BattlePokemonBuildOptions
        {
            MoveIds = [33, 45],
            Id = "starter",
        });
        Assert.Equal("starter", pokemon.Id);
        Assert.Equal(new[] { 33, 45 }, pokemon.Moves.Select(move => move.Id));

        Assert.Throws<ArgumentException>(() => BattlePokemonFactory.Create(Data, 1, 5,
            new BattlePokemonBuildOptions { EffortValues = new StatBlock(255, 255, 1, 0, 0, 0) }));
    }

    private static string FindDataDir()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProcPoke.slnx")))
                return Path.Combine(directory.FullName, "data");
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository data directory.");
    }
}
