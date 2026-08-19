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
            CurrentHp = 1,
        };
        var survivingHp = BattlePokemonFactory.MaxHpOf(Data, 1, 15) - 3;

        var result = BattleRewardResolver.ApplyDefeat(
            Data,
            current,
            defeatedSpeciesId: 149,
            defeatedLevel: 100,
            survivingHp: survivingHp,
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
        // The evolved, leveled-up species has strictly more max HP than the pre-battle species did, so the
        // survivingHp carries forward unchanged rather than being clamped to the OLD (lower) max.
        Assert.Equal(survivingHp, result.Progression.CurrentHp);
    }

    [Fact]
    public void SurvivingHpCarriesForwardUnchangedWhenThereIsNoLevelUpOrClamping()
    {
        var growth = Data.GrowthRates[Data.Species[1].GrowthRate];
        // Halfway to the next level's threshold, so the small XP a level-3 opponent grants a level-20
        // Pokémon can't accidentally tip a level-up — this is deliberately the plain, nothing-special case.
        var midway = growth.CumulativeExperience[20]
            + (growth.CumulativeExperience[21] - growth.CumulativeExperience[20]) / 2;
        var current = new BattleProgressionState
        {
            SpeciesId = 1,
            Level = 20,
            TotalExperience = midway,
            EffortValues = new StatBlock(0, 0, 0, 0, 0, 0),
            MoveIds = [33],
            CurrentHp = 1,
        };
        var survivingHp = BattlePokemonFactory.MaxHpOf(Data, 1, 20) - 5;

        var result = BattleRewardResolver.ApplyDefeat(
            Data, current, defeatedSpeciesId: 10, defeatedLevel: 3, survivingHp: survivingHp);

        Assert.Equal(20, result.Progression.Level);
        Assert.Equal(survivingHp, result.Progression.CurrentHp);
    }

    [Fact]
    public void SurvivingHpIsClampedToTheResultingSpeciesMaxHpNotCarriedOverIfTooHigh()
    {
        var current = new BattleProgressionState
        {
            SpeciesId = 16, // Pidgey
            Level = 5,
            TotalExperience = Data.GrowthRates[Data.Species[16].GrowthRate].CumulativeExperience[5],
            EffortValues = new StatBlock(0, 0, 0, 0, 0, 0),
            MoveIds = [16],
            CurrentHp = 1,
        };

        // No real battle ever produces this much surviving HP; whatever level/species this defeat resolves
        // to (a small XP gain might still tip a level-up), the clamp must land at exactly that result's own
        // max HP — recomputed independently here, not trusted from the resolver's internals.
        var result = BattleRewardResolver.ApplyDefeat(
            Data, current, defeatedSpeciesId: 10, defeatedLevel: 3, survivingHp: 99_999);
        var expectedMaxHp = BattlePokemonFactory.MaxHpOf(Data, result.Progression.SpeciesId, result.Progression.Level);

        Assert.Equal(expectedMaxHp, result.Progression.CurrentHp);
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
            CurrentHp = 500,
        };
        var reward = session.ApplyVictoryRewards(current);

        Assert.True(reward.Experience.ExperienceGained > 0);
        // The player's own HP, whatever it ended the battle at, carries forward — clamped to the resulting
        // species/level's real max HP (this test's synthetic 500-HP player exceeds every real Bulbasaur-line
        // stat, so the clamp is expected to bite here, not a no-op).
        var expectedMaxHp = BattlePokemonFactory.MaxHpOf(Data, reward.Progression.SpeciesId, reward.Progression.Level);
        Assert.Equal(Math.Min(session.State.Player.Hp, expectedMaxHp), reward.Progression.CurrentHp);
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
            CurrentHp = player.MaxHp,
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
