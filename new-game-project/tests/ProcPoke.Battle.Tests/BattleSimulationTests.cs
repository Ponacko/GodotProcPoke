using ProcPoke.Battle;
using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Battle.Tests;

public sealed class BattleSimulationTests
{
    [Fact]
    public void HigherPriorityMoveActsBeforeFasterOpponent()
    {
        var state = State(playerSpeed: 40, opponentSpeed: 200, playerPriority: 1);
        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new UseMoveAction(2), new ScriptedRandom(0));

        var moves = result.Events.OfType<MoveUsed>().ToArray();
        Assert.Equal(new[] { BattleActor.Player, BattleActor.Opponent }, moves.Select(move => move.Actor));
        Assert.Equal(1, result.State.TurnNumber);
        Assert.Collection(result.Events,
            item => Assert.IsType<TurnStarted>(item),
            item => Assert.IsType<MoveUsed>(item),
            item => Assert.IsType<EffectNotImplemented>(item),
            item => Assert.IsType<MoveUsed>(item),
            item => Assert.IsType<EffectNotImplemented>(item),
            item => Assert.IsType<TurnEnded>(item));
    }

    [Fact]
    public void FasterMoveWinsWhenPriorityTies()
    {
        var state = State(playerSpeed: 120, opponentSpeed: 80);
        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new UseMoveAction(2), new ScriptedRandom(0));

        Assert.Equal(BattleActor.Player, result.Events.OfType<MoveUsed>().First().Actor);
    }

    [Fact]
    public void EqualSpeedUsesInjectedTieBreakerDeterministically()
    {
        var state = State(playerSpeed: 100, opponentSpeed: 100);
        var playerFirst = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new UseMoveAction(2), new ScriptedRandom(0));
        var opponentFirst = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new UseMoveAction(2), new ScriptedRandom(1));

        Assert.Equal(BattleActor.Player, playerFirst.Events.OfType<MoveUsed>().First().Actor);
        Assert.Equal(BattleActor.Opponent, opponentFirst.Events.OfType<MoveUsed>().First().Actor);
    }

    [Fact]
    public void UnknownMoveIsRejectedBeforeEventsAreEmitted()
    {
        var state = State(playerSpeed: 100, opponentSpeed: 100);

        Assert.Throws<ArgumentException>(() => BattleTurnResolver.Resolve(
            state, new UseMoveAction(999), new WaitAction(), new ScriptedRandom(0)));
    }

    [Fact]
    public void BattleRandomIsReplayable()
    {
        var first = new BattleRandom(42);
        var second = new BattleRandom(42);

        Assert.Equal(Enumerable.Range(0, 20).Select(_ => first.NextInt(100)),
            Enumerable.Range(0, 20).Select(_ => second.NextInt(100)));
    }

    private static BattleState State(int playerSpeed, int opponentSpeed, int playerPriority = 0)
        => new()
        {
            Player = Pokemon("player", playerSpeed, Move(1, "Quick Attack", priority: playerPriority)),
            Opponent = Pokemon("opponent", opponentSpeed, Move(2, "Tackle")),
        };

    private static BattlePokemon Pokemon(string id, int speed, Move move)
        => new()
        {
            Id = id,
            SpeciesId = id == "player" ? 25 : 4,
            Level = 10,
            Speed = speed,
            MaxHp = 35,
            Hp = 35,
            Moves = [move],
        };

    private static Move Move(int id, string name, int priority = 0)
        => new()
        {
            Id = id,
            Name = name,
            Type = PokeType.Normal,
            Power = 40,
            Accuracy = 100,
            Pp = 35,
            Priority = priority,
            DamageClass = DamageClass.Physical,
            EffectId = 1,
            Meta = new MoveMeta { CategoryId = 0, AilmentId = 0 },
        };

    private sealed class ScriptedRandom(int value) : IBattleRandom
    {
        public int NextInt(int maxExclusive) => value % maxExclusive;
    }
}
