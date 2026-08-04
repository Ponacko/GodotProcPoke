using System.Collections.Generic;
using System.Linq;
using ProcPoke.Battle;
using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Battle.Tests;

/// <summary>
/// Pins the simulator against independently-documented Generation 5 (Black/White 2) mechanics —
/// DEVELOPMENT-PLAN.md's Phase 4 exit criterion calls for a "reference test suite: known Gen 5
/// damage-calc cases, catch-rate cases, turn-order edge cases." Unlike <see cref="BattleSimulationTests"/>
/// (which checks the formula against itself), every expected value here is cited to a primary source, so a
/// systematic formula bug — not just a regression — would be caught.
///
/// Facts already covered elsewhere, cited here for a complete picture rather than duplicated:
/// - Critical hits double damage in Gen 5 (not the 1.5× of Gen 6+) —
///   <see cref="BattleSimulationTests.CriticalHitUsesGen5MultiplierAndEmitsEvent"/>.
///   Source: https://bulbapedia.bulbagarden.net/wiki/Damage ("Critical is ... 2 in Generation V").
/// - A higher-priority move always acts before a faster, lower-priority one (the "priority ties" edge
///   case named by the exit criterion is really "priority beats speed, even at a tie in the loser's
///   favor") — <see cref="BattleSimulationTests.HigherPriorityMoveActsBeforeFasterOpponent"/> and
///   <see cref="BattleSimulationTests.FasterMoveWinsWhenPriorityTies"/>.
///   Source: https://bulbapedia.bulbagarden.net/wiki/Priority.
/// - Exactly-equal Speed within a priority bracket is broken by a genuine coin flip, not a fixed
///   tie-break rule — <see cref="BattleSimulationTests.EqualSpeedUsesInjectedTieBreakerDeterministically"/>.
///   Source: https://bulbapedia.bulbagarden.net/wiki/Stat ("in the case that two Pokémon have the same
///   Speed, one of them will randomly go first").
/// </summary>
public sealed class Gen5ReferenceTests
{
    /// <summary>
    /// Source: https://bulbapedia.bulbagarden.net/wiki/Damage and
    /// https://bulbapedia.bulbagarden.net/wiki/User:FIQ/Damage_calculation — Generation III onward
    /// (Gen V included) draws the damage roll from a random integer in [85, 100] divided by 100 (16
    /// possible values, 85%–100%). This is a different, older scheme than Generation I's 217–255⁄255 —
    /// which is what this simulator implemented until this suite caught it (see BattleSimulation.cs's
    /// <c>randomMultiplier</c>).
    /// </summary>
    [Fact]
    public void DamageRandomRollSpansEightyFivePercentToOneHundredPercentInGeneration5()
    {
        // level 100, power 117, equal Attack/Defense (100 each), no STAB/no type effectiveness/no
        // crit/no weather — chosen so the pre-random base damage is exactly 100, making the 85%/100%
        // roll endpoints land on round, unambiguous numbers.
        var attacker = Pokemon("attacker", speed: 100, Move(1, "Reference Move", PokeType.Fighting, power: 117),
            type: PokeType.Normal, level: 100, attack: 100, defense: 100);
        var defender = Pokemon("defender", speed: 1, Move(2, "Splash", PokeType.Normal, power: null),
            type: PokeType.Normal, level: 100, attack: 100, defense: 100, hp: 999);

        var minRoll = BattleTurnResolver.Resolve(
            new BattleState { Player = attacker, Opponent = defender },
            new UseMoveAction(1), new WaitAction(), new ScriptedRandom(1, 0));
        var maxRoll = BattleTurnResolver.Resolve(
            new BattleState { Player = attacker, Opponent = defender },
            new UseMoveAction(1), new WaitAction(), new ScriptedRandom(1, 15));

        Assert.Equal(85, Assert.Single(minRoll.Events.OfType<DamageDealt>()).Amount);
        Assert.Equal(100, Assert.Single(maxRoll.Events.OfType<DamageDealt>()).Amount);
    }

    /// <summary>
    /// Source: https://bulbapedia.bulbagarden.net/wiki/Paralysis_(status_condition) — "the Speed of a
    /// paralyzed Pokémon is decreased by 50%" is stated as the Generation VII+ value, explicitly "as
    /// opposed to 75%" beforehand — i.e. Generations III–VI (Gen V included) reduce Speed to exactly
    /// ¼ (100% − 75%), not the later gens' ½. Pins the exact boundary rather than just "some slowdown."
    /// </summary>
    [Fact]
    public void ParalysisReducesSpeedToExactlyOneQuarterInGeneration5()
    {
        // Paralyzed attacker's effective speed is 100/4 = 25 exactly.
        var paralyzed = Pokemon("paralyzed", speed: 100, Move(1, "Tackle", PokeType.Normal))
            with { Status = BattleStatus.Paralysis };

        var justFaster = Pokemon("justFaster", speed: 26, Move(2, "Tackle", PokeType.Normal));
        var beatsTheParalyzedSide = BattleTurnResolver.Resolve(
            new BattleState { Player = paralyzed, Opponent = justFaster },
            new UseMoveAction(1), new UseMoveAction(2), new ScriptedRandom(1));
        Assert.Equal(BattleActor.Opponent, beatsTheParalyzedSide.Events.OfType<MoveUsed>().First().Actor);

        var justSlower = Pokemon("justSlower", speed: 24, Move(2, "Tackle", PokeType.Normal));
        // First scripted value (1) must dodge the independent 25%-full-paralysis roll (random.NextInt(4)
        // == 0) so the paralyzed side's own MoveUsed actually fires and this reflects turn ORDER.
        var losesToTheParalyzedSide = BattleTurnResolver.Resolve(
            new BattleState { Player = paralyzed, Opponent = justSlower },
            new UseMoveAction(1), new UseMoveAction(2), new ScriptedRandom(1));
        Assert.Equal(BattleActor.Player, losesToTheParalyzedSide.Events.OfType<MoveUsed>().First().Actor);
    }

    /// <summary>
    /// Source: https://bulbapedia.bulbagarden.net/wiki/Catch_rate — "sleep and freeze's multipliers were
    /// boosted from 2 to 2.5" as of Generation V; paralysis/poison/burn stayed at 1.5. Confirms
    /// CaptureResolver's Gen5-specific bonus values (the classic pre-Gen5 2.0×/1.5× would be a bug here).
    /// </summary>
    [Theory]
    [InlineData(BattleStatus.Sleep, 165)]
    [InlineData(BattleStatus.Freeze, 165)]
    [InlineData(BattleStatus.Paralysis, 99)]
    [InlineData(BattleStatus.Poison, 99)]
    [InlineData(BattleStatus.BadPoison, 99)]
    [InlineData(BattleStatus.None, 66)]
    public void CaptureStatusBonusMatchesGeneration5Values(BattleStatus status, int expectedModifiedCatchValue)
    {
        // CatchRate 200, full HP 100/100, a plain Poké Ball (×1) — chosen so the unboosted modified
        // catch value (⌊(3×100−2×100)×200/(3×100)⌋ = 66) is round, and 66×2.5=165 / 66×1.5=99 land exactly.
        var attempt = new CaptureAttempt { CatchRate = 200, MaxHp = 100, Hp = 100, Status = status };

        var result = CaptureResolver.Resolve(attempt, new ScriptedRandom(0, 0, 0, 0));

        Assert.Equal(expectedModifiedCatchValue, result.ModifiedCatchValue);
    }

    private static BattlePokemon Pokemon(
        string id, int speed, Move move, PokeType type = PokeType.Normal,
        int level = 50, int attack = 100, int defense = 100, int hp = 200)
        => new()
        {
            Id = id,
            SpeciesId = 1,
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

    private static Move Move(int id, string name, PokeType type, int? power = 40)
        => new()
        {
            Id = id,
            Name = name,
            Type = type,
            Power = power,
            Accuracy = null,
            Pp = 20,
            Priority = 0,
            DamageClass = DamageClass.Physical,
            EffectId = 1,
            Meta = new MoveMeta { CategoryId = 0, AilmentId = 0 },
        };

    private sealed class ScriptedRandom : IBattleRandom
    {
        private readonly Queue<int> values;

        public ScriptedRandom(params int[] values) => this.values = new Queue<int>(values);

        public int NextInt(int maxExclusive) => (values.Count == 0 ? 15 : values.Dequeue()) % maxExclusive;
    }
}
