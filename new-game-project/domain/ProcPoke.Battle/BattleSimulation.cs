using ProcPoke.Data;

namespace ProcPoke.Battle;

/// <summary>A minimal immutable combatant snapshot consumed by the Phase 4 simulation seam.</summary>
public sealed record BattlePokemon
{
    public required string Id { get; init; }
    public required int SpeciesId { get; init; }
    public required int Level { get; init; }
    public required int Speed { get; init; }
    public int Attack { get; init; } = 1;
    public int Defense { get; init; } = 1;
    public int SpecialAttack { get; init; } = 1;
    public int SpecialDefense { get; init; } = 1;
    public IReadOnlyList<PokeType> Types { get; init; } = [PokeType.Normal];
    public int CriticalStage { get; init; }
    public BattleStatStages Stages { get; init; } = new();
    public BattleStatus Status { get; init; }

    /// <summary>Remaining blocked action checks for sleep; zero for every other status.</summary>
    public int SleepTurns { get; init; }

    /// <summary>Next Toxic residual multiplier (1/16, then 2/16, …); zero for normal poison.</summary>
    public int ToxicTurns { get; init; }

    /// <summary>Turn on which Freeze was inflicted, so the target always loses that current action.</summary>
    public int FrozenSinceTurn { get; init; }

    /// <summary>Transient flinch flag; it is consumed by the next action check.</summary>
    public bool Flinched { get; init; }
    public required int MaxHp { get; init; }
    public required int Hp { get; init; }
    public required IReadOnlyList<Move> Moves { get; init; }

    public bool IsFainted => Hp <= 0;
}

public sealed record BattleState
{
    public required BattlePokemon Player { get; init; }
    public required BattlePokemon Opponent { get; init; }
    public int TurnNumber { get; init; }
    public BattleWeather Weather { get; init; }

    /// <summary>Remaining end-of-turn weather ticks; zero only while the weather is clear.</summary>
    public int WeatherTurns { get; init; }
}

public sealed record BattleTurnResult(
    BattleState State,
    IReadOnlyList<BattleEvent> Events);

/// <summary>Injected randomness keeps ties and future accuracy/crit rolls replayable and testable.</summary>
public interface IBattleRandom
{
    int NextInt(int maxExclusive);
}

/// <summary>Deterministic PCG-XSH-RR random source for battle replay and headless tests.</summary>
public sealed class BattleRandom : IBattleRandom
{
    private const ulong Multiplier = 6364136223846793005UL;
    private ulong _state;
    private readonly ulong _increment;

    public BattleRandom(ulong seed, ulong sequence = 1)
    {
        _increment = (sequence << 1) | 1UL;
        _state = 0;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        var range = (uint)maxExclusive;
        var limit = uint.MaxValue - uint.MaxValue % range;
        uint value;
        do value = NextUInt(); while (value >= limit);
        return (int)(value % range);
    }

    private uint NextUInt()
    {
        var old = _state;
        _state = old * Multiplier + _increment;
        var shifted = (uint)(((old >> 18) ^ old) >> 27);
        var rotation = (int)(old >> 59);
        return (shifted >> rotation) | (shifted << ((-rotation) & 31));
    }
}

/// <summary>
/// Resolves one atomic turn into narration. Full Gen 5 effects are added behind this stable event contract;
/// unsupported move behavior is explicit and never silently disappears.
/// </summary>
public static class BattleTurnResolver
{
    public static BattleTurnResult Resolve(
        BattleState state,
        BattleAction playerAction,
        BattleAction opponentAction,
        IBattleRandom random,
        TypeChart? typeChart = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(playerAction);
        ArgumentNullException.ThrowIfNull(opponentAction);
        ArgumentNullException.ThrowIfNull(random);
        ValidateAction(state.Player, playerAction);
        ValidateAction(state.Opponent, opponentAction);

        var turn = state.TurnNumber + 1;
        var events = new List<BattleEvent> { new TurnStarted(turn) };
        typeChart ??= NeutralTypeChart;
        var actions = Order(state, playerAction, opponentAction, random);
        var workingState = state;
        foreach (var (actor, action) in actions)
        {
            var pokemon = actor == BattleActor.Player ? workingState.Player : workingState.Opponent;
            if (pokemon.IsFainted) continue;
            if (!CanAct(turn, actor, ref workingState, events, random)) continue;

            pokemon = actor == BattleActor.Player ? workingState.Player : workingState.Opponent;

            if (action is WaitAction)
            {
                events.Add(new MessageShown(turn, $"{pokemon.Id} waits."));
                continue;
            }

            var moveAction = (UseMoveAction)action;
            var move = pokemon.Moves.Single(candidate => candidate.Id == moveAction.MoveId);
            var targetActor = actor == BattleActor.Player ? BattleActor.Opponent : BattleActor.Player;
            var target = targetActor == BattleActor.Player ? workingState.Player : workingState.Opponent;
            events.Add(new MoveUsed(turn, actor, move.Id, move.Name));
            if (!Hits(move, pokemon, target, workingState.Weather, random))
            {
                events.Add(new MoveMissed(turn, actor, targetActor, move.Id));
                continue;
            }

            if (move.Power is null || move.DamageClass == DamageClass.Status)
            {
                var changedStats = ApplyStatChanges(move, turn, actor, targetActor, ref workingState, events, random);
                var inflictedStatus = ApplyAilment(move, turn, actor, targetActor, ref workingState, events, random);
                var startedWeather = ApplyWeather(move, turn, ref workingState, events);
                var healed = ApplyHealing(move, turn, actor, ref workingState, events);
                if (!changedStats && !inflictedStatus && !startedWeather && !healed)
                    events.Add(new EffectNotImplemented(turn, actor, move.Id, move.Name));
                continue;
            }

            var totalDamage = 0;
            var hitCount = HitCount(move, random);
            for (var hit = 0; hit < hitCount; hit++)
            {
                pokemon = actor == BattleActor.Player ? workingState.Player : workingState.Opponent;
                target = targetActor == BattleActor.Player ? workingState.Player : workingState.Opponent;
                if (target.IsFainted) break;

                var critical = IsCritical(pokemon, move, random);
                if (critical) events.Add(new CriticalHit(turn, actor, targetActor));
                var effectiveness = Effectiveness(typeChart, move.Type, target.Types);
                var damage = DamageFor(pokemon, target, move, effectiveness, critical, workingState.Weather, random);
                totalDamage += damage;
                events.Add(new DamageDealt(turn, actor, targetActor, damage, effectiveness, critical));
                var updatedTarget = target with { Hp = Math.Max(0, target.Hp - damage) };
                workingState = targetActor == BattleActor.Player
                    ? workingState with { Player = updatedTarget }
                    : workingState with { Opponent = updatedTarget };
                if (updatedTarget.IsFainted)
                {
                    events.Add(new Fainted(turn, targetActor));
                    break;
                }
            }

            ApplyDrainOrRecoil(move, turn, actor, totalDamage, ref workingState, events);
            target = targetActor == BattleActor.Player ? workingState.Player : workingState.Opponent;
            if (!target.IsFainted && totalDamage > 0)
            {
                ApplyStatChanges(move, turn, actor, targetActor, ref workingState, events, random);
                ApplyAilment(move, turn, actor, targetActor, ref workingState, events, random);
                ApplyFlinch(move, turn, actor, targetActor, ref workingState, events, random);
            }
        }

        ApplyEndOfTurnStatuses(turn, ref workingState, events);
        ApplyEndOfTurnWeather(turn, ref workingState, events);
        AdvanceWeather(turn, ref workingState, events);
        events.Add(new TurnEnded(turn));
        return new BattleTurnResult(workingState with { TurnNumber = turn }, events);
    }

    private static IReadOnlyList<(BattleActor Actor, BattleAction Action)> Order(
        BattleState state,
        BattleAction playerAction,
        BattleAction opponentAction,
        IBattleRandom random)
    {
        var player = (BattleActor.Player, playerAction);
        var opponent = (BattleActor.Opponent, opponentAction);
        var playerPriority = Priority(state.Player, playerAction);
        var opponentPriority = Priority(state.Opponent, opponentAction);
        if (playerPriority != opponentPriority)
            return playerPriority > opponentPriority ? [player, opponent] : [opponent, player];
        var playerSpeed = EffectiveSpeed(state.Player);
        var opponentSpeed = EffectiveSpeed(state.Opponent);
        if (playerSpeed != opponentSpeed)
            return playerSpeed > opponentSpeed ? [player, opponent] : [opponent, player];
        return random.NextInt(2) == 0 ? [player, opponent] : [opponent, player];
    }

    private static int Priority(BattlePokemon pokemon, BattleAction action)
        => action is UseMoveAction moveAction
            ? pokemon.Moves.Single(move => move.Id == moveAction.MoveId).Priority
            : 0;

    private static void ValidateAction(BattlePokemon pokemon, BattleAction action)
    {
        if (action is UseMoveAction moveAction && pokemon.Moves.All(move => move.Id != moveAction.MoveId))
            throw new ArgumentException($"{pokemon.Id} does not know move {moveAction.MoveId}", nameof(action));
    }

    private static bool Hits(
        Move move, BattlePokemon attacker, BattlePokemon defender, BattleWeather weather, IBattleRandom random)
    {
        if (move.Accuracy is null) return true;
        if (WeatherMakesCertain(move, weather)) return true;
        if (WeatherMakesInaccurate(move, weather)) return random.NextInt(100) < 50;
        var effectiveStage = Math.Clamp(attacker.Stages.Accuracy - defender.Stages.Evasion, -6, 6);
        var accuracy = Math.Min(100, ModifiedAccuracy(move.Accuracy.Value, effectiveStage));
        if (accuracy >= 100) return true;
        return random.NextInt(100) < accuracy;
    }

    private static bool IsCritical(BattlePokemon pokemon, Move move, IBattleRandom random)
    {
        var stage = Math.Clamp(pokemon.CriticalStage + move.Meta.CritRateBonus, 0, 3);
        var threshold = stage switch
        {
            0 => 1,
            1 => 2,
            2 => 4,
            _ => 5,
        };
        return random.NextInt(16) < threshold;
    }

    private static double Effectiveness(
        TypeChart chart, PokeType attackType, IReadOnlyList<PokeType> defendingTypes)
        => defendingTypes.Aggregate(1.0, (multiplier, defendingType)
            => multiplier * chart.Effectiveness(attackType, defendingType));

    private static int DamageFor(
        BattlePokemon attacker,
        BattlePokemon defender,
        Move move,
        double effectiveness,
        bool critical,
        BattleWeather weather,
        IBattleRandom random)
    {
        if (effectiveness == 0) return 0;
        var attackStat = move.DamageClass == DamageClass.Special
            ? BattleStat.SpecialAttack
            : BattleStat.Attack;
        var defenseStat = move.DamageClass == DamageClass.Special
            ? BattleStat.SpecialDefense
            : BattleStat.Defense;
        var attackStage = critical && attacker.Stages.Of(attackStat) < 0
            ? 0
            : attacker.Stages.Of(attackStat);
        var defenseStage = critical && defender.Stages.Of(defenseStat) > 0
            ? 0
            : defender.Stages.Of(defenseStat);
        var attack = ModifiedStat(
            move.DamageClass == DamageClass.Special ? attacker.SpecialAttack : attacker.Attack,
            attackStage);
        var defense = ModifiedStat(
            move.DamageClass == DamageClass.Special ? defender.SpecialDefense : defender.Defense,
            defenseStage);
        if (move.DamageClass == DamageClass.Physical && attacker.Status == BattleStatus.Burn)
            attack = Math.Max(1, attack / 2);
        if (move.DamageClass == DamageClass.Special && defender.Types.Contains(PokeType.Rock)
            && weather == BattleWeather.Sandstorm)
            defense = defense * 3 / 2;
        attack = Math.Max(1, attack);
        defense = Math.Max(1, defense);
        var levelFactor = (2 * attacker.Level / 5) + 2;
        var baseDamage = levelFactor * move.Power!.Value * attack / defense;
        baseDamage = baseDamage / 50 + 2;
        var stab = attacker.Types.Contains(move.Type) ? 1.5 : 1.0;
        var criticalMultiplier = critical ? 2.0 : 1.0;
        var randomMultiplier = (217 + random.NextInt(39)) / 255.0;
        var weatherMultiplier = WeatherDamageMultiplier(move.Type, weather);
        var damage = (int)Math.Floor(baseDamage * stab * effectiveness * criticalMultiplier * weatherMultiplier * randomMultiplier);
        return Math.Max(1, damage);
    }

    private static int ModifiedStat(int stat, int stage)
    {
        stage = Math.Clamp(stage, -6, 6);
        return stage >= 0
            ? stat * (2 + stage) / 2
            : stat * 2 / (2 - stage);
    }

    private static int ModifiedAccuracy(int accuracy, int stage)
    {
        stage = Math.Clamp(stage, -6, 6);
        return stage >= 0
            ? accuracy * (3 + stage) / 3
            : accuracy * 3 / (3 - stage);
    }

    private static int EffectiveSpeed(BattlePokemon pokemon)
    {
        var speed = ModifiedStat(pokemon.Speed, pokemon.Stages.Speed);
        return pokemon.Status == BattleStatus.Paralysis ? Math.Max(1, speed / 4) : speed;
    }

    private static bool WeatherMakesCertain(Move move, BattleWeather weather)
        => (weather == BattleWeather.Rain && move.Id is 87 or 542)
           || (weather == BattleWeather.Hail && move.Id == 59);

    private static bool WeatherMakesInaccurate(Move move, BattleWeather weather)
        => weather == BattleWeather.Sun && move.Id is 87 or 542;

    private static double WeatherDamageMultiplier(PokeType type, BattleWeather weather) => weather switch
    {
        BattleWeather.Rain when type == PokeType.Water => 1.5,
        BattleWeather.Rain when type == PokeType.Fire => 0.5,
        BattleWeather.Sun when type == PokeType.Fire => 1.5,
        BattleWeather.Sun when type == PokeType.Water => 0.5,
        _ => 1.0,
    };

    private static bool ApplyWeather(
        Move move, int turn, ref BattleState state, ICollection<BattleEvent> events)
    {
        var weather = move.EffectId switch
        {
            116 => BattleWeather.Sandstorm,
            137 => BattleWeather.Rain,
            138 => BattleWeather.Sun,
            165 => BattleWeather.Hail,
            _ => BattleWeather.Clear,
        };
        if (weather == BattleWeather.Clear) return false;

        state = state with { Weather = weather, WeatherTurns = 5 };
        events.Add(new WeatherStarted(turn, weather, 5));
        return true;
    }

    private static bool ApplyStatChanges(
        Move move,
        int turn,
        BattleActor actor,
        BattleActor targetActor,
        ref BattleState state,
        ICollection<BattleEvent> events,
        IBattleRandom random)
    {
        if (move.Meta.StatChanges.Count == 0) return false;
        if (!StatChangesProc(move, random)) return true;

        // PokeAPI target 7 is the user. All retained stage-changing moves use either it or a
        // single-opponent target in Gen 5 singles. The fallback only serves deliberately small
        // synthetic moves in tests and callers that have not loaded baked game data.
        var allPositive = move.Meta.StatChanges.All(change => change.Stages > 0);
        var isSecondary = move.Meta.StatChance > 0 || move.EffectChance is > 0;
        var fallbackTargetsOpponent = move.DamageClass == DamageClass.Status
            ? !allPositive
            : isSecondary && !allPositive;
        var affectedActor = move.Meta.StatChangeTargetId is int targetId
            ? targetId == 7 ? actor : targetActor
            : fallbackTargetsOpponent ? targetActor : actor;
        var pokemon = affectedActor == BattleActor.Player ? state.Player : state.Opponent;
        var stages = pokemon.Stages;
        var changed = false;
        foreach (var change in move.Meta.StatChanges)
        {
            var next = Math.Clamp(stages.Of(change.Stat) + change.Stages, -6, 6);
            if (next == stages.Of(change.Stat)) continue;
            stages = stages.With(change.Stat, next);
            events.Add(new StatStageChanged(turn, affectedActor, change.Stat, next));
            changed = true;
        }

        if (!changed) return true;
        var updated = pokemon with { Stages = stages };
        state = affectedActor == BattleActor.Player
            ? state with { Player = updated }
            : state with { Opponent = updated };
        return true;
    }

    private static bool StatChangesProc(Move move, IBattleRandom random)
    {
        var chance = move.Meta.StatChance > 0 ? move.Meta.StatChance : move.EffectChance;
        return chance is null or 0 || chance >= 100 || random.NextInt(100) < chance;
    }

    private static bool ApplyAilment(
        Move move,
        int turn,
        BattleActor actor,
        BattleActor targetActor,
        ref BattleState state,
        ICollection<BattleEvent> events,
        IBattleRandom random)
    {
        var status = StatusFor(move);
        if (status == BattleStatus.None) return false;
        if (!AilmentProcs(move, random)) return true;

        var affectedActor = move.Meta.AilmentTargetId == 7 ? actor : targetActor;
        var pokemon = affectedActor == BattleActor.Player ? state.Player : state.Opponent;
        if (pokemon.IsFainted || pokemon.Status != BattleStatus.None || !CanReceive(status, pokemon)) return true;

        // Sleep must block at least the target's current action. It subsequently lasts one to
        // three more action checks, matching the Gen 5 one-to-three-turn duration.
        var sleepTurns = status == BattleStatus.Sleep ? random.NextInt(3) + 2 : 0;
        var updated = pokemon with
        {
            Status = status,
            SleepTurns = sleepTurns,
            ToxicTurns = status == BattleStatus.BadPoison ? 1 : 0,
            FrozenSinceTurn = status == BattleStatus.Freeze ? turn : 0,
        };
        state = affectedActor == BattleActor.Player
            ? state with { Player = updated }
            : state with { Opponent = updated };
        events.Add(new StatusInflicted(turn, actor, affectedActor, status));
        return true;
    }

    private static BattleStatus StatusFor(Move move) => move.Meta.AilmentId switch
    {
        1 => BattleStatus.Paralysis,
        2 => BattleStatus.Sleep,
        3 => BattleStatus.Freeze,
        4 => BattleStatus.Burn,
        5 => move.EffectId == 34 ? BattleStatus.BadPoison : BattleStatus.Poison,
        _ => BattleStatus.None,
    };

    private static bool AilmentProcs(Move move, IBattleRandom random)
    {
        var chance = move.Meta.AilmentChance > 0 ? move.Meta.AilmentChance : move.EffectChance;
        return chance is null or 0 || chance >= 100 || random.NextInt(100) < chance;
    }

    private static bool CanReceive(BattleStatus status, BattlePokemon pokemon) => status switch
    {
        BattleStatus.Burn => !pokemon.Types.Contains(PokeType.Fire),
        BattleStatus.Freeze => !pokemon.Types.Contains(PokeType.Ice),
        BattleStatus.Poison or BattleStatus.BadPoison
            => !pokemon.Types.Contains(PokeType.Poison) && !pokemon.Types.Contains(PokeType.Steel),
        _ => true,
    };

    private static bool CanAct(
        int turn,
        BattleActor actor,
        ref BattleState state,
        ICollection<BattleEvent> events,
        IBattleRandom random)
    {
        var pokemon = actor == BattleActor.Player ? state.Player : state.Opponent;
        switch (pokemon.Status)
        {
            case BattleStatus.Sleep when pokemon.SleepTurns > 1:
                UpdateActor(actor, pokemon with { SleepTurns = pokemon.SleepTurns - 1 }, ref state);
                events.Add(new MessageShown(turn, $"{pokemon.Id} is asleep."));
                return false;
            case BattleStatus.Sleep:
                ClearStatus(turn, actor, pokemon, ref state, events);
                return true;
            case BattleStatus.Freeze when pokemon.FrozenSinceTurn == turn:
                events.Add(new MessageShown(turn, $"{pokemon.Id} is frozen solid."));
                return false;
            case BattleStatus.Freeze when random.NextInt(100) >= 20:
                events.Add(new MessageShown(turn, $"{pokemon.Id} is frozen solid."));
                return false;
            case BattleStatus.Freeze:
                ClearStatus(turn, actor, pokemon, ref state, events);
                return true;
            case BattleStatus.Paralysis when random.NextInt(4) == 0:
                events.Add(new MessageShown(turn, $"{pokemon.Id} is fully paralyzed."));
                return false;
            default:
                break;
        }

        if (pokemon.Flinched)
        {
            UpdateActor(actor, pokemon with { Flinched = false }, ref state);
            events.Add(new MessageShown(turn, $"{pokemon.Id} flinches and cannot move!"));
            return false;
        }

        return true;
    }

    private static int HitCount(Move move, IBattleRandom random)
    {
        var min = move.Meta.MinHits ?? 1;
        var max = move.Meta.MaxHits ?? min;
        if (min < 1 || max < min)
            throw new InvalidOperationException($"Move {move.Id} has invalid hit bounds {min}..{max}.");
        return min == max ? min : min + random.NextInt(max - min + 1);
    }

    private static bool ApplyHealing(
        Move move,
        int turn,
        BattleActor actor,
        ref BattleState state,
        ICollection<BattleEvent> events)
    {
        if (move.Meta.Healing == 0) return false;
        var pokemon = actor == BattleActor.Player ? state.Player : state.Opponent;
        if (pokemon.IsFainted) return true;

        var amount = Math.Max(1, pokemon.MaxHp * Math.Abs(move.Meta.Healing) / 100);
        amount = Math.Min(amount, pokemon.MaxHp - pokemon.Hp);
        if (amount == 0) return true;
        UpdateActor(actor, pokemon with { Hp = pokemon.Hp + amount }, ref state);
        events.Add(new HpRestored(turn, actor, amount));
        return true;
    }

    private static void ApplyDrainOrRecoil(
        Move move,
        int turn,
        BattleActor actor,
        int totalDamage,
        ref BattleState state,
        ICollection<BattleEvent> events)
    {
        var percent = move.Meta.Drain;
        if (move.EffectId == 255 && percent == 0) percent = -25; // Struggle's fixed Gen 5 recoil.
        if (percent == 0 || totalDamage <= 0) return;

        var pokemon = actor == BattleActor.Player ? state.Player : state.Opponent;
        var amount = Math.Max(1, totalDamage * Math.Abs(percent) / 100);
        if (percent > 0)
        {
            amount = Math.Min(amount, pokemon.MaxHp - pokemon.Hp);
            if (amount == 0) return;
            UpdateActor(actor, pokemon with { Hp = pokemon.Hp + amount }, ref state);
            events.Add(new HpRestored(turn, actor, amount));
            return;
        }

        var updated = pokemon with { Hp = Math.Max(0, pokemon.Hp - amount) };
        UpdateActor(actor, updated, ref state);
        events.Add(new RecoilDamage(turn, actor, amount));
        if (updated.IsFainted) events.Add(new Fainted(turn, actor));
    }

    private static bool ApplyFlinch(
        Move move,
        int turn,
        BattleActor actor,
        BattleActor targetActor,
        ref BattleState state,
        ICollection<BattleEvent> events,
        IBattleRandom random)
    {
        var chance = move.Meta.FlinchChance;
        if (chance <= 0 || random.NextInt(100) >= chance) return false;
        var target = targetActor == BattleActor.Player ? state.Player : state.Opponent;
        if (target.IsFainted || target.Flinched) return true;
        UpdateActor(targetActor, target with { Flinched = true }, ref state);
        events.Add(new FlinchInflicted(turn, actor, targetActor));
        return true;
    }

    private static void ApplyEndOfTurnStatuses(
        int turn, ref BattleState state, ICollection<BattleEvent> events)
    {
        ApplyEndOfTurnStatus(turn, BattleActor.Player, ref state, events);
        ApplyEndOfTurnStatus(turn, BattleActor.Opponent, ref state, events);
    }

    private static void ApplyEndOfTurnStatus(
        int turn, BattleActor actor, ref BattleState state, ICollection<BattleEvent> events)
    {
        var pokemon = actor == BattleActor.Player ? state.Player : state.Opponent;
        if (pokemon.IsFainted || pokemon.Status is not (BattleStatus.Burn or BattleStatus.Poison or BattleStatus.BadPoison))
            return;

        var multiplier = pokemon.Status == BattleStatus.BadPoison ? pokemon.ToxicTurns : 2;
        var amount = Math.Max(1, pokemon.MaxHp * multiplier / 16);
        var updated = pokemon with { Hp = Math.Max(0, pokemon.Hp - amount) };
        if (updated.Status == BattleStatus.BadPoison)
            updated = updated with { ToxicTurns = Math.Min(15, updated.ToxicTurns + 1) };
        UpdateActor(actor, updated, ref state);
        events.Add(new ResidualDamage(turn, actor, pokemon.Status, amount));
        if (updated.IsFainted) events.Add(new Fainted(turn, actor));
    }

    private static void ApplyEndOfTurnWeather(
        int turn, ref BattleState state, ICollection<BattleEvent> events)
    {
        if (state.Weather is not (BattleWeather.Sandstorm or BattleWeather.Hail)) return;
        ApplyEndOfTurnWeatherDamage(turn, BattleActor.Player, ref state, events);
        ApplyEndOfTurnWeatherDamage(turn, BattleActor.Opponent, ref state, events);
    }

    private static void ApplyEndOfTurnWeatherDamage(
        int turn, BattleActor actor, ref BattleState state, ICollection<BattleEvent> events)
    {
        var pokemon = actor == BattleActor.Player ? state.Player : state.Opponent;
        if (pokemon.IsFainted || IsWeatherImmune(pokemon, state.Weather)) return;

        var amount = Math.Max(1, pokemon.MaxHp / 16);
        var updated = pokemon with { Hp = Math.Max(0, pokemon.Hp - amount) };
        UpdateActor(actor, updated, ref state);
        events.Add(new WeatherDamage(turn, actor, state.Weather, amount));
        if (updated.IsFainted) events.Add(new Fainted(turn, actor));
    }

    private static bool IsWeatherImmune(BattlePokemon pokemon, BattleWeather weather) => weather switch
    {
        BattleWeather.Sandstorm => pokemon.Types.Any(type => type is PokeType.Rock or PokeType.Ground or PokeType.Steel),
        BattleWeather.Hail => pokemon.Types.Contains(PokeType.Ice),
        _ => true,
    };

    private static void AdvanceWeather(int turn, ref BattleState state, ICollection<BattleEvent> events)
    {
        if (state.Weather == BattleWeather.Clear) return;
        var turns = state.WeatherTurns - 1;
        if (turns > 0)
        {
            state = state with { WeatherTurns = turns };
            return;
        }

        var previous = state.Weather;
        state = state with { Weather = BattleWeather.Clear, WeatherTurns = 0 };
        events.Add(new WeatherEnded(turn, previous));
    }

    private static void ClearStatus(
        int turn, BattleActor actor, BattlePokemon pokemon, ref BattleState state, ICollection<BattleEvent> events)
    {
        var previous = pokemon.Status;
        UpdateActor(actor, pokemon with
        {
            Status = BattleStatus.None,
            SleepTurns = 0,
            ToxicTurns = 0,
            FrozenSinceTurn = 0,
        }, ref state);
        events.Add(new StatusCured(turn, actor, previous));
    }

    private static void UpdateActor(BattleActor actor, BattlePokemon pokemon, ref BattleState state)
        => state = actor == BattleActor.Player
            ? state with { Player = pokemon }
            : state with { Opponent = pokemon };

    private static readonly TypeChart NeutralTypeChart = new()
    {
        Percents = Enumerable.Range(0, Enum.GetValues<PokeType>().Length)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(100, Enum.GetValues<PokeType>().Length).ToArray())
            .ToArray(),
    };
}

public sealed record TurnEnded(int Turn) : BattleEvent(Turn);
