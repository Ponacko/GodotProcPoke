using ProcPoke.Battle;
using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Battle.Tests;

public sealed class BattleAiTests
{
    [Fact]
    public void WildAiChoosesAUniformLegalMove()
    {
        var state = State(Move(1, "Tackle"), Move(2, "Ember", PokeType.Fire));

        var action = BattleAi.ChooseAction(state, BattleActor.Player, BattleAiTier.Wild, new ScriptedRandom(1));

        Assert.Equal(2, Assert.IsType<UseMoveAction>(action).MoveId);
    }

    [Fact]
    public void TrainerAiPrefersSuperEffectiveDamageWhenItsImperfectRollMisses()
    {
        var state = State(Move(1, "Tackle"), Move(2, "Ember", PokeType.Fire)) with
        {
            Opponent = State(Move(3, "Tackle"), Move(4, "Tackle")).Player with
            {
                Types = [PokeType.Grass],
            },
        };

        var action = BattleAi.ChooseAction(state, BattleActor.Player, BattleAiTier.Trainer,
            new ScriptedRandom(99, 0), Chart((PokeType.Fire, PokeType.Grass, 200)));

        Assert.Equal(2, Assert.IsType<UseMoveAction>(action).MoveId);
    }

    [Fact]
    public void TrainerAiHasAControlledTwentyPercentImperfectionPath()
    {
        var state = State(Move(1, "Tackle"), Move(2, "Ember", PokeType.Fire)) with
        {
            Opponent = State(Move(3, "Tackle"), Move(4, "Tackle")).Player with
            {
                Types = [PokeType.Grass],
            },
        };

        var action = BattleAi.ChooseAction(state, BattleActor.Player, BattleAiTier.Trainer,
            new ScriptedRandom(0, 0), Chart((PokeType.Fire, PokeType.Grass, 200)));

        Assert.Equal(1, Assert.IsType<UseMoveAction>(action).MoveId);
    }

    [Fact]
    public void BossAiUsesTheSameSmartScoringWithoutTrainerImprecision()
    {
        var state = State(Move(1, "Tackle"), Move(2, "Ember", PokeType.Fire)) with
        {
            Opponent = State(Move(3, "Tackle"), Move(4, "Tackle")).Player with
            {
                Types = [PokeType.Grass],
            },
        };

        var action = BattleAi.ChooseAction(state, BattleActor.Player, BattleAiTier.Boss,
            new ScriptedRandom(0), Chart((PokeType.Fire, PokeType.Grass, 200)));

        Assert.Equal(2, Assert.IsType<UseMoveAction>(action).MoveId);
    }

    private static BattleState State(Move playerFirst, Move playerSecond)
        => new()
        {
            Player = Pokemon("player", [playerFirst, playerSecond], PokeType.Normal),
            Opponent = Pokemon("opponent", [Move(3, "Tackle")], PokeType.Normal),
        };

    private static BattlePokemon Pokemon(string id, IReadOnlyList<Move> moves, PokeType type)
        => new()
        {
            Id = id,
            SpeciesId = 1,
            Level = 10,
            Speed = 50,
            Types = [type],
            MaxHp = 40,
            Hp = 40,
            Moves = moves,
        };

    private static Move Move(int id, string name, PokeType type = PokeType.Normal)
        => new()
        {
            Id = id,
            Name = name,
            Type = type,
            Power = 40,
            Accuracy = 100,
            Pp = 35,
            Priority = 0,
            DamageClass = DamageClass.Physical,
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

        public ScriptedRandom(params int[] values) => this.values = new Queue<int>(values);

        public int NextInt(int maxExclusive)
            => (values.Count == 0 ? 0 : values.Dequeue()) % maxExclusive;
    }
}
