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
            state, new UseMoveAction(1), new UseMoveAction(2), new ScriptedRandom(15));

        var moves = result.Events.OfType<MoveUsed>().ToArray();
        Assert.Equal(new[] { BattleActor.Player, BattleActor.Opponent }, moves.Select(move => move.Actor));
        Assert.Equal(1, result.State.TurnNumber);
        Assert.Collection(result.Events,
            item => Assert.IsType<TurnStarted>(item),
            item => Assert.IsType<MoveUsed>(item),
            item => Assert.IsType<DamageDealt>(item),
            item => Assert.IsType<MoveUsed>(item),
            item => Assert.IsType<DamageDealt>(item),
            item => Assert.IsType<TurnEnded>(item));
    }

    [Fact]
    public void FasterMoveWinsWhenPriorityTies()
    {
        var state = State(playerSpeed: 120, opponentSpeed: 80);
        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new UseMoveAction(2), new ScriptedRandom(15));

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

    [Fact]
    public void DamageUsesStabEffectivenessAndRandomRoll()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(1, "Ember", type: PokeType.Fire),
                type: PokeType.Fire, level: 50),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle"),
                type: PokeType.Grass, hp: 100, level: 50),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new WaitAction(), new ScriptedRandom(15, 38),
            Chart((PokeType.Fire, PokeType.Grass, 200)));

        var damage = Assert.Single(result.Events.OfType<DamageDealt>());
        Assert.Equal(57, damage.Amount);
        Assert.Equal(2.0, damage.Effectiveness);
        Assert.False(damage.Critical);
        Assert.Equal(43, result.State.Opponent.Hp);
    }

    [Fact]
    public void CriticalHitUsesGen5MultiplierAndEmitsEvent()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(1, "Tackle"), type: PokeType.Fire, level: 50),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle"), hp: 30, level: 50),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new WaitAction(), new ScriptedRandom(0, 38));

        var damage = Assert.Single(result.Events.OfType<DamageDealt>());
        Assert.Equal(38, damage.Amount);
        Assert.True(damage.Critical);
        Assert.Contains(result.Events, item => item is CriticalHit);
        Assert.Contains(result.Events, item => item is Fainted { Actor: BattleActor.Opponent });
        Assert.Equal(0, result.State.Opponent.Hp);
    }

    [Fact]
    public void MissLeavesHpUnchangedAndEmitsMissEvent()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(1, "Low Kick", accuracy: 50)),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle"), hp: 100),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new WaitAction(), new ScriptedRandom(75));

        Assert.Contains(result.Events, item => item is MoveMissed { Target: BattleActor.Opponent, MoveId: 1 });
        Assert.Empty(result.Events.OfType<DamageDealt>());
        Assert.Equal(100, result.State.Opponent.Hp);
    }

    [Fact]
    public void StatusMoveUsesExplicitFallbackEvent()
    {
        var state = State(100, 80);
        state = state with
        {
            Player = state.Player with { Moves = [Move(1, "Growl", power: null, damageClass: DamageClass.Status)] },
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new WaitAction(), new ScriptedRandom(0));

        Assert.Contains(result.Events, item => item is EffectNotImplemented { MoveId: 1 });
        Assert.Empty(result.Events.OfType<DamageDealt>());
    }

    private static BattleState State(int playerSpeed, int opponentSpeed, int playerPriority = 0)
        => new()
        {
            Player = Pokemon("player", playerSpeed, Move(1, "Quick Attack", priority: playerPriority)),
            Opponent = Pokemon("opponent", opponentSpeed, Move(2, "Tackle")),
        };

    private static BattlePokemon Pokemon(
        string id,
        int speed,
        Move move,
        PokeType type = PokeType.Normal,
        int hp = 35,
        int level = 10,
        int attack = 100,
        int defense = 100)
        => new()
        {
            Id = id,
            SpeciesId = id == "player" ? 25 : 4,
            Level = level,
            Speed = speed,
            Attack = attack,
            Defense = defense,
            SpecialAttack = attack,
            SpecialDefense = defense,
            Types = [type],
            MaxHp = hp,
            Hp = hp,
            Moves = [move],
        };

    private static Move Move(
        int id,
        string name,
        int priority = 0,
        PokeType type = PokeType.Normal,
        int? power = 40,
        int? accuracy = 100,
        DamageClass damageClass = DamageClass.Physical)
        => new()
        {
            Id = id,
            Name = name,
            Type = type,
            Power = power,
            Accuracy = accuracy,
            Pp = 35,
            Priority = priority,
            DamageClass = damageClass,
            EffectId = 1,
            Meta = new MoveMeta { CategoryId = 0, AilmentId = 0 },
        };

    private static TypeChart Chart(params (PokeType Attack, PokeType Defense, int Percent)[] overrides)
    {
        var size = Enum.GetValues<PokeType>().Length;
        var percents = Enumerable.Range(0, size)
            .Select(_ => Enumerable.Repeat(100, size).ToArray())
            .ToArray();
        foreach (var (attack, defense, percent) in overrides)
            percents[(int)attack][(int)defense] = percent;
        return new TypeChart { Percents = percents };
    }

    private sealed class ScriptedRandom : IBattleRandom
    {
        private readonly Queue<int> values;

        public ScriptedRandom(params int[] values)
        {
            this.values = new Queue<int>(values);
        }

        public int NextInt(int maxExclusive)
            => (values.Count == 0 ? 15 : values.Dequeue()) % maxExclusive;
    }
}
