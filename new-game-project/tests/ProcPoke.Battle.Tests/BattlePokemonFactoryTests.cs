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
