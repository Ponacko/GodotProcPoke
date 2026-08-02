using ProcPoke.Battle;
using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Battle.Tests;

public sealed class WildBattleSessionTests
{
    private static readonly GameData Data = GameDataLoader.Load(FindDataDir());

    [Fact]
    public void StartBuildsTheEncounterSpeciesAtTheSelectedLevel()
    {
        var player = BattlePokemonFactory.Create(Data, 1, 20,
            new BattlePokemonBuildOptions { MoveIds = [33] });
        var session = WildBattleSession.Start(
            Data,
            player,
            new WildBattleDefinition(7, 16, 4),
            seed: 1234);

        Assert.Equal(7, session.Encounter.AreaId);
        Assert.Equal(16, session.State.Opponent.SpeciesId);
        Assert.Equal(4, session.State.Opponent.Level);
        Assert.Equal("Wild Pidgey", session.State.Opponent.Id);
        Assert.Equal(WildBattleOutcome.Ongoing, session.Outcome);
    }

    [Fact]
    public void TakingATurnUsesWildAiAndReturnsTheOrderedEventStream()
    {
        var player = BattlePokemonFactory.Create(Data, 1, 20,
            new BattlePokemonBuildOptions { MoveIds = [33] });
        var session = WildBattleSession.Start(
            Data,
            player,
            new WildBattleDefinition(7, 16, 20),
            seed: 1234);

        var result = session.TakeTurn(new UseMoveAction(33));

        Assert.Same(session.State, result.State);
        Assert.Equal(1, result.State.TurnNumber);
        Assert.IsType<TurnStarted>(result.Events[0]);
        Assert.Contains(result.Events, battleEvent => battleEvent is MoveUsed move
            && move.Actor == BattleActor.Player);
        Assert.Contains(result.Events, battleEvent => battleEvent is MoveUsed move
            && move.Actor == BattleActor.Opponent);
    }

    [Fact]
    public void EndedBattleRejectsAdditionalTurns()
    {
        var player = new BattlePokemon
        {
            Id = "overpowered",
            SpeciesId = 1,
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
        var session = WildBattleSession.Start(
            Data,
            player,
            new WildBattleDefinition(7, 16, 1),
            seed: 42,
            wildOptions: new BattlePokemonBuildOptions { MoveIds = [33] });

        WildBattleTurnResult result;
        do
        {
            result = session.TakeTurn(new UseMoveAction(33));
        } while (result.Outcome == WildBattleOutcome.Ongoing);

        Assert.Equal(WildBattleOutcome.PlayerWon, result.Outcome);
        Assert.Throws<InvalidOperationException>(() => session.TakeTurn(new UseMoveAction(33)));
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
