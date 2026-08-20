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

    /// <summary>A wall: it survives anything but takes forever to win, so a battle runs long enough to
    /// watch what the opponent does on its way down.</summary>
    private static BattlePokemon Sturdy(int speciesId) => new()
    {
        Id = "player",
        SpeciesId = speciesId,
        Level = 50,
        Speed = 500,
        Attack = 12,
        Defense = 500,
        SpecialAttack = 12,
        SpecialDefense = 500,
        Types = [PokeType.Normal],
        MaxHp = 4000,
        Hp = 4000,
        Moves = [Data.Moves[33]],
    };

    [Fact]
    public void OnlyABossCarriesPotions()
    {
        var roster = new[] { new TrainerRosterMember(16, 12) };

        Assert.Equal(
            0,
            TrainerBattleSession.Start(Data, Overpowered(1), roster, seed: 1).OpponentPotionsLeft);
        Assert.Equal(
            TrainerBattleSession.BossPotions,
            TrainerBattleSession.Start(Data, Overpowered(1), roster, seed: 1, tier: BattleAiTier.Boss)
                .OpponentPotionsLeft);
        Assert.Throws<ArgumentException>(() => TrainerBattleSession.Start(
            Data, Overpowered(1), roster, seed: 1, tier: BattleAiTier.Wild));
    }

    /// <summary>
    /// CONTEXT.md's AI Tier 2 heal, end to end: whittled down in small steps, a boss must pass through the
    /// quarter-HP band, and it heals there rather than trading hits — spending its whole (small, fixed)
    /// supply before it goes down.
    /// </summary>
    [Fact]
    public void ABossSpendsItsPotionsOnTheWayDown()
    {
        var roster = new[] { new TrainerRosterMember(143, 50) }; // a big HP pool to grind through
        var session = TrainerBattleSession.Start(
            Data, Sturdy(1), roster, seed: 7, opponentIdPrefix: "Leader's", tier: BattleAiTier.Boss);

        var itemsUsed = 0;
        for (var turn = 0; turn < 2000 && session.Outcome == TrainerBattleOutcome.Ongoing; turn++)
        {
            var result = session.TakeTurn(new UseMoveAction(33));
            itemsUsed += result.Events.OfType<ItemUsed>().Count(item => item.Actor == BattleActor.Opponent);
        }

        Assert.Equal(TrainerBattleOutcome.PlayerWon, session.Outcome);
        Assert.Equal(TrainerBattleSession.BossPotions, itemsUsed);
        Assert.Equal(0, session.OpponentPotionsLeft);
    }

    /// <summary>
    /// The potion is scaled to the Pokémon it heals, not to the trainer's rank: a fixed Hyper Potion would
    /// fully heal a first-gym team twice over, and difficulty is meant to come from teams and levels.
    /// </summary>
    [Fact]
    public void ABossHealsWithAPotionSizedToItsPokemon()
    {
        var names = new List<string>();
        foreach (var level in new[] { 12, 50 })
        {
            var session = TrainerBattleSession.Start(
                Data, Sturdy(1), [new TrainerRosterMember(143, level)], seed: 7, tier: BattleAiTier.Boss);
            var maxHp = session.State.Opponent.MaxHp;

            for (var turn = 0; turn < 4000 && session.Outcome == TrainerBattleOutcome.Ongoing; turn++)
            foreach (var item in session.TakeTurn(new UseMoveAction(33)).Events.OfType<ItemUsed>())
            {
                names.Add(item.ItemName);
                // Worth having, but never a full heal on top of the fight.
                var restored = MedicineCatalog.For(item.ItemName)!.HpRestoredFor(maxHp);
                Assert.InRange(restored, maxHp / 2, maxHp);
            }
        }

        // A level-12 Snorlax and a level-50 one do not get the same bottle.
        Assert.NotEmpty(names);
        Assert.True(names.Distinct().Count() > 1, string.Join(", ", names));
    }

    /// <summary>The same grind against a standard trainer: no potions, ever.</summary>
    [Fact]
    public void AStandardTrainerNeverHeals()
    {
        var roster = new[] { new TrainerRosterMember(143, 50) };
        var session = TrainerBattleSession.Start(Data, Sturdy(1), roster, seed: 7);

        var itemsUsed = 0;
        for (var turn = 0; turn < 2000 && session.Outcome == TrainerBattleOutcome.Ongoing; turn++)
            itemsUsed += session.TakeTurn(new UseMoveAction(33)).Events.OfType<ItemUsed>().Count();

        Assert.Equal(TrainerBattleOutcome.PlayerWon, session.Outcome);
        Assert.Equal(0, itemsUsed);
    }

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
        // Every reward in the chain carries the player's real end-of-battle HP (clamped to whatever that
        // step's max HP is), not the pre-battle progression's HP.
        foreach (var reward in rewards)
        {
            var expectedMaxHp = BattlePokemonFactory.MaxHpOf(Data, reward.Progression.SpeciesId, reward.Progression.Level);
            Assert.Equal(Math.Min(session.State.Player.Hp, expectedMaxHp), reward.Progression.CurrentHp);
        }
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
