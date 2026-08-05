using System;
using System.IO;
using System.Linq;
using ProcPoke.Battle;
using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Battle.Tests;

public sealed class TrainerBattleSessionTests
{
    private static readonly GameData Data = GameDataLoader.Load(FindDataDir());

    private static BattlePokemon Overpowered(int speciesId) => new()
    {
        Id = "player",
        SpeciesId = speciesId,
        Level = 100,
        Speed = 500,
        Attack = 500,
        Defense = 500,
        SpecialAttack = 500,
        SpecialDefense = 500,
        Types = [PokeType.Normal],
        MaxHp = 500,
        Hp = 500,
        Moves = [Data.Moves[33]],
    };

    [Fact]
    public void StartBuildsTheFirstRosterMemberAsTheOpponent()
    {
        var roster = new[] { new TrainerRosterMember(16, 12), new TrainerRosterMember(19, 14) };
        var session = TrainerBattleSession.Start(Data, Overpowered(1), roster, seed: 1);

        Assert.Equal(16, session.State.Opponent.SpeciesId);
        Assert.Equal(12, session.State.Opponent.Level);
        Assert.Equal(1, session.OpponentPosition);
        Assert.Equal(2, session.OpponentRosterSize);
        Assert.Equal(TrainerBattleOutcome.Ongoing, session.Outcome);
    }

    [Fact]
    public void StartPrefixesTheOpponentIdWhenGiven()
    {
        var roster = new[] { new TrainerRosterMember(16, 12) };
        var session = TrainerBattleSession.Start(Data, Overpowered(1), roster, seed: 1, opponentIdPrefix: "Rival's");

        Assert.Equal($"Rival's {Data.Species[16].Name}", session.State.Opponent.Id);
    }

    [Fact]
    public void ThrowingABallIsIllegalInATrainerBattle()
    {
        var roster = new[] { new TrainerRosterMember(16, 12) };
        var session = TrainerBattleSession.Start(Data, Overpowered(1), roster, seed: 1);

        Assert.Throws<ArgumentException>(() => session.TakeTurn(new ThrowBallAction("Poké Ball")));
    }

    [Fact]
    public void DefeatingARosterMemberSendsOutTheNextOneAndStaysOngoing()
    {
        var roster = new[] { new TrainerRosterMember(16, 5), new TrainerRosterMember(19, 5) };
        var session = TrainerBattleSession.Start(Data, Overpowered(1), roster, seed: 1);

        var result = session.TakeTurn(new UseMoveAction(33));

        Assert.Equal(TrainerBattleOutcome.Ongoing, result.Outcome);
        Assert.Equal(19, result.State.Opponent.SpeciesId);
        Assert.False(result.State.Opponent.IsFainted);
        Assert.Equal(2, session.OpponentPosition);
        Assert.Contains(result.Events, battleEvent => battleEvent is Fainted fainted
            && fainted.Actor == BattleActor.Opponent);
        Assert.Contains(result.Events, battleEvent => battleEvent is MessageShown shown
            && shown.Message.Contains(Data.Species[19].Name) && shown.Message.Contains("sent out"));
    }

    [Fact]
    public void DefeatingTheWholeRosterEndsTheBattleInAWin()
    {
        var roster = new[] { new TrainerRosterMember(16, 5), new TrainerRosterMember(19, 5) };
        var session = TrainerBattleSession.Start(Data, Overpowered(1), roster, seed: 1);

        TrainerBattleTurnResult result;
        do
        {
            result = session.TakeTurn(new UseMoveAction(33));
        } while (result.Outcome == TrainerBattleOutcome.Ongoing);

        Assert.Equal(TrainerBattleOutcome.PlayerWon, result.Outcome);
        Assert.Throws<InvalidOperationException>(() => session.TakeTurn(new UseMoveAction(33)));
    }

    [Fact]
    public void APlayerFaintEndsTheBattleInALossEvenWithOpponentRosterRemaining()
    {
        var player = new BattlePokemon
        {
            Id = "player",
            SpeciesId = 1,
            Level = 5,
            Speed = 1,
            Attack = 1,
            Defense = 1,
            SpecialAttack = 1,
            SpecialDefense = 1,
            Types = [PokeType.Grass],
            MaxHp = 1,
            Hp = 1,
            Moves = [Data.Moves[33]],
        };
        var roster = new[] { new TrainerRosterMember(16, 60), new TrainerRosterMember(19, 60) };
        var session = TrainerBattleSession.Start(Data, player, roster, seed: 1);

        var result = session.TakeTurn(new UseMoveAction(33));

        Assert.Equal(TrainerBattleOutcome.PlayerLost, result.Outcome);
        Assert.Equal(1, session.OpponentPosition);
    }

    [Fact]
    public void VictoryRewardsApplyOneDefeatPerRosterMemberWithTheTrainerMultiplier()
    {
        var roster = new[] { new TrainerRosterMember(16, 5), new TrainerRosterMember(19, 5) };
        var session = TrainerBattleSession.Start(Data, Overpowered(1), roster, seed: 1);
        TrainerBattleTurnResult result;
        do
        {
            result = session.TakeTurn(new UseMoveAction(33));
        } while (result.Outcome == TrainerBattleOutcome.Ongoing);

        var progression = BattleProgressionFactory.AtLevel(Data, 1, 5);
        var rewards = session.ApplyVictoryRewards(progression);

        Assert.Equal(2, rewards.Count);
        var expectedFirst = ExperienceCalculator.ForDefeat(
            Data.Species[16].BaseExperience, 5, 5, trainerBattle: true, participants: 1);
        Assert.Equal(expectedFirst, rewards[0].Experience.ExperienceGained);
        Assert.Equal(progression.TotalExperience + expectedFirst, rewards[0].Progression.TotalExperience);
        // The second defeat's reward is computed against the level the first defeat just leveled up to,
        // not the original starting progression — confirming the two calls chain rather than run independently.
        Assert.True(rewards[1].Progression.TotalExperience >= rewards[0].Progression.TotalExperience);
        Assert.Throws<InvalidOperationException>(() => session.ApplyVictoryRewards(progression));
    }

    [Fact]
    public void VictoryRewardsRequireAWonBattle()
    {
        var roster = new[] { new TrainerRosterMember(16, 60) };
        var session = TrainerBattleSession.Start(Data, Overpowered(1), roster, seed: 1);
        var progression = BattleProgressionFactory.AtLevel(Data, 1, 5);

        Assert.Throws<InvalidOperationException>(() => session.ApplyVictoryRewards(progression));
    }

    [Fact]
    public void StartRejectsAnEmptyRoster()
    {
        Assert.Throws<ArgumentException>(
            () => TrainerBattleSession.Start(Data, Overpowered(1), Array.Empty<TrainerRosterMember>(), seed: 1));
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
