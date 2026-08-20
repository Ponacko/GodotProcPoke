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

    /// <summary>CONTEXT.md's AI Tier 2: a boss heals below ~25% HP, and only a boss does.</summary>
    [Fact]
    public void BossAiReachesForAPotionBelowAQuarterHealth()
    {
        var potion = new UseItemAction(17, "Potion", new Medicine(MedicineEffect.RestoreHp, FixedHp: 20));
        var full = State(Move(1, "Tackle"), Move(2, "Ember", PokeType.Fire));
        var hurt = full with { Player = full.Player with { Hp = 9 } };   // 9/40 — under a quarter
        var scratched = full with { Player = full.Player with { Hp = 11 } }; // 11/40 — over it

        Assert.Same(potion, BattleAi.ChooseAction(
            hurt, BattleActor.Player, BattleAiTier.Boss, new ScriptedRandom(0), null, potion));
        Assert.IsType<UseMoveAction>(BattleAi.ChooseAction(
            scratched, BattleActor.Player, BattleAiTier.Boss, new ScriptedRandom(0), null, potion));
        // No potion left, and every lesser tier: it attacks regardless of how low it is.
        Assert.IsType<UseMoveAction>(BattleAi.ChooseAction(
            hurt, BattleActor.Player, BattleAiTier.Boss, new ScriptedRandom(0)));
        Assert.IsType<UseMoveAction>(BattleAi.ChooseAction(
            hurt, BattleActor.Player, BattleAiTier.Trainer, new ScriptedRandom(0, 0), null, potion));
        Assert.IsType<UseMoveAction>(BattleAi.ChooseAction(
            hurt, BattleActor.Player, BattleAiTier.Wild, new ScriptedRandom(0), null, potion));
    }

    /// <summary>
    /// The matchup test a tier-2 boss switches on: hopeless means both halves at once — nothing it knows
    /// reaches neutral damage, and the other side has something super-effective. Either half alone is a
    /// fight worth having.
    /// </summary>
    [Fact]
    public void HopelessNeedsBothBeingResistedAndBeingThreatened()
    {
        var chart = Chart(
            (PokeType.Fire, PokeType.Water, 50),    // its move is resisted
            (PokeType.Water, PokeType.Fire, 200));  // and it is weak in return
        var fire = Pokemon("fire", [Move(1, "Ember", PokeType.Fire)], PokeType.Fire);
        var water = Pokemon("water", [Move(2, "Water Gun", PokeType.Water)], PokeType.Water);

        Assert.True(BattleAi.IsHopelessMatchup(fire, water, chart));
        // The reverse is the fight you want: super-effective, and resisted in return.
        Assert.False(BattleAi.IsHopelessMatchup(water, fire, chart));

        // Resisted but unthreatened: still worth attacking into.
        var resistedOnly = Chart((PokeType.Fire, PokeType.Water, 50));
        Assert.False(BattleAi.IsHopelessMatchup(fire, water, resistedOnly));

        // Threatened but hitting neutrally: also worth attacking into.
        var threatenedOnly = Chart((PokeType.Water, PokeType.Fire, 200));
        Assert.False(BattleAi.IsHopelessMatchup(fire, water, threatenedOnly));
    }

    [Fact]
    public void ANeutralMoveIsEnoughToNotBeHopeless()
    {
        var chart = Chart(
            (PokeType.Fire, PokeType.Water, 50),
            (PokeType.Water, PokeType.Fire, 200));
        // The same weak-to-Water Pokémon, but it also knows a Normal move that lands for full damage.
        var withOption = Pokemon(
            "fire", [Move(1, "Ember", PokeType.Fire), Move(2, "Tackle")], PokeType.Fire);
        var water = Pokemon("water", [Move(3, "Water Gun", PokeType.Water)], PokeType.Water);

        Assert.Equal(1.0, BattleAi.BestEffectiveness(withOption, water, chart));
        Assert.False(BattleAi.IsHopelessMatchup(withOption, water, chart));
    }

    [Fact]
    public void MoveScoresRankTheBetterAnswerHigher()
    {
        var chart = Chart((PokeType.Fire, PokeType.Grass, 200));
        var grass = Pokemon("grass", [Move(9, "Tackle")], PokeType.Grass);
        var fire = Pokemon("fire", [Move(1, "Ember", PokeType.Fire)], PokeType.Fire);
        var normal = Pokemon("normal", [Move(2, "Tackle")], PokeType.Normal);

        Assert.True(
            BattleAi.BestMoveScore(fire, grass, chart) > BattleAi.BestMoveScore(normal, grass, chart));
    }

    [Fact]
    public void APokemonWithNoDamagingMoveScoresNothingAgainstAnyone()
    {
        var status = Pokemon("status", [Move(1, "Growl", power: null)], PokeType.Normal);
        var target = Pokemon("target", [Move(2, "Tackle")], PokeType.Normal);

        Assert.Equal(0, BattleAi.BestEffectiveness(status, target));
        // A Pokémon that cannot damage anything is never "hopeless" — there is nothing better to swap to
        // on that axis, and its own worth is a matter for the move scorer.
        Assert.False(BattleAi.IsHopelessMatchup(status, target));
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

    private static Move Move(int id, string name, PokeType type = PokeType.Normal, int? power = 40)
        => new()
        {
            Id = id,
            Name = name,
            Type = type,
            Power = power,
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
