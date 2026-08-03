using ProcPoke.Battle;
using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Battle.Tests;

public sealed class BattleRewardProgressionTests
{
    private static readonly GameData Data = GameDataLoader.Load(FindDataDir());

    [Fact]
    public void VictoryRewardComposesExperienceMovesAndLevelUpEvolution()
    {
        var growth = Data.GrowthRates[Data.Species[1].GrowthRate];
        var current = new BattleProgressionState
        {
            SpeciesId = 1,
            Level = 15,
            TotalExperience = growth.CumulativeExperience[15],
            EffortValues = new StatBlock(0, 0, 0, 0, 0, 0),
            MoveIds = [33, 45],
        };

        var result = BattleRewardResolver.ApplyDefeat(
            Data,
            current,
            defeatedSpeciesId: 149,
            defeatedLevel: 100,
            evolutionContext: new EvolutionContext
            {
                SpeciesId = 1,
                Level = 15,
                Attack = 25,
                Defense = 20,
            });

        Assert.True(result.Experience.ExperienceGained > 0);
        Assert.True(result.Experience.NewLevel >= 16);
        Assert.Contains(75, result.Moves.MoveIds);
        Assert.NotNull(result.Evolution);
        Assert.Equal(2, result.Evolution!.ToSpeciesId);
        Assert.Equal(2, result.Progression.SpeciesId);
        Assert.Equal(result.Experience.NewLevel, result.Progression.Level);
        Assert.Equal(result.Experience.Progress.TotalExperience, result.Progression.TotalExperience);
    }

    [Fact]
    public void SessionOnlyPaysVictoryRewardsOnceAfterTheOpponentFaints()
    {
        var player = new BattlePokemon
        {
            Id = "player",
            SpeciesId = 1,
            Level = 15,
            Speed = 500,
            Attack = 500,
            Defense = 500,
            SpecialAttack = 100,
            SpecialDefense = 500,
            Types = [PokeType.Normal],
            MaxHp = 500,
            Hp = 500,
            Moves = [Data.Moves[33]],
        };
        var session = WildBattleSession.Start(
            Data,
            player,
            new WildBattleDefinition(7, 16, 1),
            seed: 123,
            wildOptions: new BattlePokemonBuildOptions { MoveIds = [33] });

        var turn = session.TakeTurn(new UseMoveAction(33));
        Assert.Equal(WildBattleOutcome.PlayerWon, turn.Outcome);

        var growth = Data.GrowthRates[Data.Species[1].GrowthRate];
        var current = new BattleProgressionState
        {
            SpeciesId = 1,
            Level = 15,
            TotalExperience = growth.CumulativeExperience[15],
            EffortValues = new StatBlock(0, 0, 0, 0, 0, 0),
            MoveIds = [33],
        };
        var reward = session.ApplyVictoryRewards(current);

        Assert.True(reward.Experience.ExperienceGained > 0);
        Assert.Throws<InvalidOperationException>(() => session.ApplyVictoryRewards(current));
    }

    [Fact]
    public void RewardCannotBeClaimedFromAnUnfinishedBattle()
    {
        var player = BattlePokemonFactory.Create(Data, 1, 20,
            new BattlePokemonBuildOptions { MoveIds = [33] });
        var session = WildBattleSession.Start(
            Data,
            player,
            new WildBattleDefinition(7, 16, 20),
            seed: 123,
            wildOptions: new BattlePokemonBuildOptions { MoveIds = [33] });
        var growth = Data.GrowthRates[Data.Species[1].GrowthRate];
        var current = new BattleProgressionState
        {
            SpeciesId = 1,
            Level = 20,
            TotalExperience = growth.CumulativeExperience[20],
            EffortValues = new StatBlock(0, 0, 0, 0, 0, 0),
            MoveIds = [33],
        };

        Assert.Throws<InvalidOperationException>(() => session.ApplyVictoryRewards(current));
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
