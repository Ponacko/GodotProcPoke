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

    [Fact]
    public void StatChangeStatusMoveUpdatesStateAndEmitsTheAppliedStage()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(14, "Swords Dance", power: null,
                damageClass: DamageClass.Status,
                meta: Meta(new MoveStatChange(BattleStat.Attack, 2)))),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle")),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(14), new WaitAction(), new ScriptedRandom());

        Assert.Equal(2, result.State.Player.Stages.Attack);
        Assert.Contains(result.Events, item => item is StatStageChanged
        {
            Target: BattleActor.Player,
            Stat: BattleStat.Attack,
            Stages: 2,
        });
        Assert.DoesNotContain(result.Events, item => item is EffectNotImplemented);
    }

    [Fact]
    public void StatDropStatusMoveTargetsOpponent()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(45, "Growl", power: null,
                damageClass: DamageClass.Status,
                meta: Meta(new MoveStatChange(BattleStat.Attack, -1)))),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle")),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(45), new WaitAction(), new ScriptedRandom());

        Assert.Equal(-1, result.State.Opponent.Stages.Attack);
        Assert.Contains(result.Events, item => item is StatStageChanged
        {
            Target: BattleActor.Opponent,
            Stat: BattleStat.Attack,
            Stages: -1,
        });
    }

    [Fact]
    public void BakedTargetMetadataKeepsMixedSelfChangesOnTheUser()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(504, "Shell Smash", power: null,
                damageClass: DamageClass.Status,
                meta: MetaTarget(7,
                    new MoveStatChange(BattleStat.Defense, -1),
                    new MoveStatChange(BattleStat.Attack, 2)))),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle")),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(504), new WaitAction(), new ScriptedRandom());

        Assert.Equal(-1, result.State.Player.Stages.Defense);
        Assert.Equal(2, result.State.Player.Stages.Attack);
        Assert.Equal(0, result.State.Opponent.Stages.Defense);
    }

    [Fact]
    public void SpeedStagesChangeTurnOrder()
    {
        var unboosted = State(playerSpeed: 60, opponentSpeed: 80);
        var state = unboosted with
        {
            Player = unboosted.Player with
            {
                Stages = new BattleStatStages { Speed = 2 },
            },
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new UseMoveAction(2), new ScriptedRandom(15));

        Assert.Equal(BattleActor.Player, result.Events.OfType<MoveUsed>().First().Actor);
    }

    [Fact]
    public void AccuracyAndEvasionStagesModifyTheHitCheck()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(1, "Tackle")) with
            {
                Stages = new BattleStatStages { Accuracy = -1 },
            },
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle")),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new WaitAction(), new ScriptedRandom(75));

        Assert.Contains(result.Events, item => item is MoveMissed { MoveId: 1 });
        Assert.Empty(result.Events.OfType<DamageDealt>());
    }

    [Fact]
    public void AttackStageModifiesFormulaDamage()
    {
        var baseline = State(playerSpeed: 100, opponentSpeed: 80);
        var boosted = baseline with
        {
            Player = baseline.Player with { Stages = new BattleStatStages { Attack = 2 } },
        };

        var baselineResult = BattleTurnResolver.Resolve(
            baseline, new UseMoveAction(1), new WaitAction(), new ScriptedRandom(15, 38));
        var boostedResult = BattleTurnResolver.Resolve(
            boosted, new UseMoveAction(1), new WaitAction(), new ScriptedRandom(15, 38));

        Assert.Equal(9, Assert.Single(baselineResult.Events.OfType<DamageDealt>()).Amount);
        Assert.Equal(16, Assert.Single(boostedResult.Events.OfType<DamageDealt>()).Amount);
    }

    [Fact]
    public void MajorAilmentStatusMoveInflictsItsBakedTarget()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(86, "Thunder Wave", power: null,
                damageClass: DamageClass.Status, meta: MetaAilment(1, targetId: 10))),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle")),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(86), new WaitAction(), new ScriptedRandom(15));

        Assert.Equal(BattleStatus.Paralysis, result.State.Opponent.Status);
        Assert.Contains(result.Events, item => item is StatusInflicted
        {
            Actor: BattleActor.Player,
            Target: BattleActor.Opponent,
            Status: BattleStatus.Paralysis,
        });
    }

    [Fact]
    public void SleepPreventsTheTargetFromTakingItsCurrentAction()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(147, "Spore", power: null,
                damageClass: DamageClass.Status, meta: MetaAilment(2, targetId: 10))),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle")),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(147), new UseMoveAction(2), new ScriptedRandom(0));

        Assert.Equal(BattleStatus.Sleep, result.State.Opponent.Status);
        Assert.DoesNotContain(result.Events, item => item is MoveUsed { Actor: BattleActor.Opponent });
        Assert.Contains(result.Events, item => item is MessageShown { Message: "opponent is asleep." });
    }

    [Fact]
    public void FreezePreventsTheTargetFromImmediatelyThawingAndActing()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(58, "Ice Beam", power: null,
                damageClass: DamageClass.Status, meta: MetaAilment(3, targetId: 10))),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle")),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(58), new UseMoveAction(2), new ScriptedRandom(0));

        Assert.Equal(BattleStatus.Freeze, result.State.Opponent.Status);
        Assert.DoesNotContain(result.Events, item => item is MoveUsed { Actor: BattleActor.Opponent });
    }

    [Fact]
    public void ParalysisQuartersSpeedBeforeTurnOrder()
    {
        var state = State(playerSpeed: 100, opponentSpeed: 80) with
        {
            Player = State(playerSpeed: 100, opponentSpeed: 80).Player with { Status = BattleStatus.Paralysis },
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new UseMoveAction(2), new ScriptedRandom(15, 38));

        Assert.Equal(BattleActor.Opponent, result.Events.OfType<MoveUsed>().First().Actor);
    }

    [Fact]
    public void BurnHalvesPhysicalDamageAndDealsOneEighthResidualDamage()
    {
        var state = State(playerSpeed: 100, opponentSpeed: 80) with
        {
            Player = State(playerSpeed: 100, opponentSpeed: 80).Player with { Status = BattleStatus.Burn },
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(1), new WaitAction(), new ScriptedRandom(15, 38));

        Assert.Equal(6, Assert.Single(result.Events.OfType<DamageDealt>()).Amount);
        Assert.Equal(31, result.State.Player.Hp);
        Assert.Contains(result.Events, item => item is ResidualDamage
        {
            Target: BattleActor.Player,
            Status: BattleStatus.Burn,
            Amount: 4,
        });
    }

    [Fact]
    public void ToxicResidualDamageEscalatesEachTurn()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(1, "Tackle"), hp: 100),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle"), hp: 100) with
            {
                Status = BattleStatus.BadPoison,
                ToxicTurns = 1,
            },
        };

        var first = BattleTurnResolver.Resolve(
            state, new WaitAction(), new WaitAction(), new ScriptedRandom());
        var second = BattleTurnResolver.Resolve(
            first.State, new WaitAction(), new WaitAction(), new ScriptedRandom());

        Assert.Equal(94, first.State.Opponent.Hp);
        Assert.Equal(2, first.State.Opponent.ToxicTurns);
        Assert.Equal(82, second.State.Opponent.Hp);
        Assert.Equal(3, second.State.Opponent.ToxicTurns);
        Assert.Contains(first.Events, item => item is ResidualDamage { Amount: 6, Status: BattleStatus.BadPoison });
        Assert.Contains(second.Events, item => item is ResidualDamage { Amount: 12, Status: BattleStatus.BadPoison });
    }

    [Fact]
    public void RainDanceStartsFiveTurnBattleWeather()
    {
        var state = new BattleState
        {
            Player = Pokemon("player", 100, Move(240, "Rain Dance", power: null,
                damageClass: DamageClass.Status, effectId: 137)),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle")),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(240), new WaitAction(), new ScriptedRandom());

        Assert.Equal(BattleWeather.Rain, result.State.Weather);
        Assert.Equal(4, result.State.WeatherTurns);
        Assert.Contains(result.Events, item => item is WeatherStarted
        {
            Weather: BattleWeather.Rain,
            TurnsRemaining: 5,
        });
    }

    [Fact]
    public void RainBoostsWaterDamageAndMakesThunderCertain()
    {
        var state = new BattleState
        {
            Weather = BattleWeather.Rain,
            WeatherTurns = 5,
            Player = Pokemon("player", 100, Move(87, "Thunder", type: PokeType.Electric, accuracy: 70),
                type: PokeType.Electric),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle")),
        };

        var thunder = BattleTurnResolver.Resolve(
            state, new UseMoveAction(87), new WaitAction(), new ScriptedRandom(99, 15));

        Assert.Single(thunder.Events.OfType<DamageDealt>());

        var waterState = state with
        {
            Player = Pokemon("player", 100, Move(55, "Water Gun", type: PokeType.Water), type: PokeType.Water),
        };
        var water = BattleTurnResolver.Resolve(
            waterState, new UseMoveAction(55), new WaitAction(), new ScriptedRandom(15, 38));

        Assert.Equal(13, Assert.Single(water.Events.OfType<DamageDealt>()).Amount);
    }

    [Fact]
    public void SandstormDealsResidualDamageExceptToItsImmuneTypes()
    {
        var state = new BattleState
        {
            Weather = BattleWeather.Sandstorm,
            WeatherTurns = 5,
            Player = Pokemon("player", 100, Move(1, "Tackle"), hp: 100),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle"), type: PokeType.Rock, hp: 100),
        };

        var result = BattleTurnResolver.Resolve(
            state, new WaitAction(), new WaitAction(), new ScriptedRandom());

        Assert.Equal(94, result.State.Player.Hp);
        Assert.Equal(100, result.State.Opponent.Hp);
        Assert.Equal(4, result.State.WeatherTurns);
        Assert.Contains(result.Events, item => item is WeatherDamage
        {
            Target: BattleActor.Player,
            Weather: BattleWeather.Sandstorm,
            Amount: 6,
        });
    }

    [Fact]
    public void WeatherExpiresAfterItsFinalEndOfTurnTick()
    {
        var state = State(playerSpeed: 100, opponentSpeed: 80) with
        {
            Weather = BattleWeather.Hail,
            WeatherTurns = 1,
        };

        var result = BattleTurnResolver.Resolve(
            state, new WaitAction(), new WaitAction(), new ScriptedRandom());

        Assert.Equal(BattleWeather.Clear, result.State.Weather);
        Assert.Equal(0, result.State.WeatherTurns);
        Assert.Contains(result.Events, item => item is WeatherEnded { Weather: BattleWeather.Hail });
    }

    [Fact]
    public void FixedMultiHitMoveEmitsOneDamageEventPerHit()
    {
        var move = Move(3, "Double Slap", power: 15) with
        {
            Meta = MetaEffects(minHits: 2, maxHits: 2),
        };
        var state = new BattleState
        {
            Player = Pokemon("player", 100, move, hp: 100, level: 50),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle"), hp: 100, level: 50),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(3), new WaitAction(), new ScriptedRandom(15, 38, 15, 38));

        var hits = result.Events.OfType<DamageDealt>().ToArray();
        Assert.Equal(2, hits.Length);
        Assert.Equal(100 - hits.Sum(hit => hit.Amount), result.State.Opponent.Hp);
    }

    [Fact]
    public void DrainRestoresTheConfiguredFractionOfTotalDamage()
    {
        var move = Move(71, "Absorb", type: PokeType.Grass, damageClass: DamageClass.Special) with
        {
            Meta = MetaEffects(drain: 50),
        };
        var state = new BattleState
        {
            Player = Pokemon("player", 100, move, type: PokeType.Grass, hp: 20, level: 50) with { MaxHp = 100 },
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle"), type: PokeType.Water, hp: 100, level: 50),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(71), new WaitAction(), new ScriptedRandom(15, 38));

        var damage = Assert.Single(result.Events.OfType<DamageDealt>());
        var healing = Assert.Single(result.Events.OfType<HpRestored>());
        Assert.Equal(Math.Max(1, damage.Amount / 2), healing.Amount);
        Assert.Equal(20 + healing.Amount, result.State.Player.Hp);
    }

    [Fact]
    public void RecoilIsEmittedAndAppliedToTheAttacker()
    {
        var move = Move(36, "Take Down", power: 90) with
        {
            Meta = MetaEffects(drain: -25),
        };
        var state = new BattleState
        {
            Player = Pokemon("player", 100, move, hp: 100, level: 50),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle"), hp: 100, level: 50),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(36), new WaitAction(), new ScriptedRandom(15, 38));

        var damage = Assert.Single(result.Events.OfType<DamageDealt>());
        var recoil = Assert.Single(result.Events.OfType<RecoilDamage>());
        Assert.Equal(Math.Max(1, damage.Amount / 4), recoil.Amount);
        Assert.Equal(100 - recoil.Amount, result.State.Player.Hp);
    }

    [Fact]
    public void HealingMoveRestoresHpWithoutFallbackEvent()
    {
        var move = Move(105, "Recover", power: null, damageClass: DamageClass.Status) with
        {
            Meta = MetaEffects(healing: 50),
        };
        var state = new BattleState
        {
            Player = Pokemon("player", 100, move, hp: 40) with { MaxHp = 100 },
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle")),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(105), new WaitAction(), new ScriptedRandom());

        Assert.Equal(90, result.State.Player.Hp);
        Assert.Equal(50, Assert.Single(result.Events.OfType<HpRestored>()).Amount);
        Assert.DoesNotContain(result.Events, item => item is EffectNotImplemented);
    }

    [Fact]
    public void FlinchBlocksTheTargetAndIsConsumedByItsNextAction()
    {
        var move = Move(23, "Stomp", power: 65) with
        {
            Meta = MetaEffects(flinchChance: 100),
        };
        var state = new BattleState
        {
            Player = Pokemon("player", 100, move),
            Opponent = Pokemon("opponent", 80, Move(2, "Tackle")),
        };

        var result = BattleTurnResolver.Resolve(
            state, new UseMoveAction(23), new UseMoveAction(2), new ScriptedRandom(15, 38, 0));

        Assert.Contains(result.Events, item => item is FlinchInflicted
        {
            Actor: BattleActor.Player,
            Target: BattleActor.Opponent,
        });
        Assert.DoesNotContain(result.Events, item => item is MoveUsed { Actor: BattleActor.Opponent });
        Assert.False(result.State.Opponent.Flinched);
    }

    [Fact]
    public void CaptureUsesGen5HpAndStatusModifiers()
    {
        var healthy = CaptureResolver.Resolve(new CaptureAttempt
        {
            CatchRate = 45,
            MaxHp = 100,
            Hp = 100,
        }, new ScriptedRandom(0, 0, 0, 0));
        var weakenedAndAsleep = CaptureResolver.Resolve(new CaptureAttempt
        {
            CatchRate = 45,
            MaxHp = 100,
            Hp = 1,
            Status = BattleStatus.Sleep,
        }, new ScriptedRandom(0, 0, 0, 0));

        Assert.Equal(15, healthy.ModifiedCatchValue);
        Assert.Equal(110, weakenedAndAsleep.ModifiedCatchValue);
        Assert.True(weakenedAndAsleep.ShakeThreshold > healthy.ShakeThreshold);
    }

    [Fact]
    public void CaptureConsumesFourDeterministicShakeChecks()
    {
        var result = CaptureResolver.Resolve(new CaptureAttempt
        {
            CatchRate = 255,
            MaxHp = 100,
            Hp = 1,
        }, new ScriptedRandom(0, 0, 0, 65535));

        Assert.False(result.Captured);
        Assert.Equal(4, result.Shakes.Count);
        Assert.Equal(3, result.SuccessfulShakes);
    }

    [Fact]
    public void CaptureAtGuaranteedThresholdDoesNotConsumeRandomness()
    {
        var random = new CountingRandom();
        var result = CaptureResolver.Resolve(new CaptureAttempt
        {
            CatchRate = 255,
            MaxHp = 100,
            Hp = 1,
            Status = BattleStatus.Sleep,
            BallMultiplier = 3.0,
        }, random);

        Assert.True(result.Captured);
        Assert.Equal(0, random.Calls);
        Assert.Equal(255, result.ModifiedCatchValue);
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
        DamageClass damageClass = DamageClass.Physical,
        MoveMeta? meta = null,
        int effectId = 1)
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
            EffectId = effectId,
            Meta = meta ?? Meta(),
        };

    private static MoveMeta Meta(params MoveStatChange[] statChanges)
        => new() { CategoryId = 0, AilmentId = 0, StatChanges = statChanges };

    private static MoveMeta MetaEffects(
        int? minHits = null,
        int? maxHits = null,
        int drain = 0,
        int healing = 0,
        int flinchChance = 0)
        => new()
        {
            CategoryId = 0,
            AilmentId = 0,
            MinHits = minHits,
            MaxHits = maxHits,
            Drain = drain,
            Healing = healing,
            FlinchChance = flinchChance,
        };

    private static MoveMeta MetaTarget(int targetId, params MoveStatChange[] statChanges)
        => new()
        {
            CategoryId = 0,
            AilmentId = 0,
            StatChanges = statChanges,
            StatChangeTargetId = targetId,
        };

    private static MoveMeta MetaAilment(int ailmentId, int targetId, int chance = 0)
        => new()
        {
            CategoryId = 0,
            AilmentId = ailmentId,
            AilmentChance = chance,
            AilmentTargetId = targetId,
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

    private sealed class CountingRandom : IBattleRandom
    {
        public int Calls { get; private set; }

        public int NextInt(int maxExclusive)
        {
            Calls++;
            return 0;
        }
    }
}
