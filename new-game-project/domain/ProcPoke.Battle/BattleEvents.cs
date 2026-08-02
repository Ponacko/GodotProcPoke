using ProcPoke.Data;

namespace ProcPoke.Battle;

public enum BattleActor
{
    Player,
    Opponent,
}

public enum BattleActionKind
{
    UseMove,
    Wait,
}

public abstract record BattleAction(BattleActionKind Kind);

public sealed record UseMoveAction(int MoveId) : BattleAction(BattleActionKind.UseMove);

public sealed record WaitAction() : BattleAction(BattleActionKind.Wait);

public abstract record BattleEvent(int Turn);

public sealed record TurnStarted(int Turn) : BattleEvent(Turn);

public sealed record MoveUsed(
    int Turn, BattleActor Actor, int MoveId, string MoveName) : BattleEvent(Turn);

public sealed record MoveMissed(
    int Turn, BattleActor Actor, BattleActor Target, int MoveId) : BattleEvent(Turn);

public sealed record CriticalHit(
    int Turn, BattleActor Actor, BattleActor Target) : BattleEvent(Turn);

public sealed record DamageDealt(
    int Turn,
    BattleActor Actor,
    BattleActor Target,
    int Amount,
    double Effectiveness,
    bool Critical) : BattleEvent(Turn);

public sealed record StatStageChanged(
    int Turn, BattleActor Target, BattleStat Stat, int Stages) : BattleEvent(Turn);

public sealed record StatusInflicted(
    int Turn, BattleActor Actor, BattleActor Target, BattleStatus Status) : BattleEvent(Turn);

public sealed record StatusCured(
    int Turn, BattleActor Target, BattleStatus Status) : BattleEvent(Turn);

public sealed record ResidualDamage(
    int Turn, BattleActor Target, BattleStatus Status, int Amount) : BattleEvent(Turn);

public sealed record WeatherStarted(
    int Turn, BattleWeather Weather, int TurnsRemaining) : BattleEvent(Turn);

public sealed record WeatherEnded(
    int Turn, BattleWeather Weather) : BattleEvent(Turn);

public sealed record WeatherDamage(
    int Turn, BattleActor Target, BattleWeather Weather, int Amount) : BattleEvent(Turn);

public sealed record HpRestored(
    int Turn, BattleActor Target, int Amount) : BattleEvent(Turn);

public sealed record RecoilDamage(
    int Turn, BattleActor Target, int Amount) : BattleEvent(Turn);

public sealed record FlinchInflicted(
    int Turn, BattleActor Actor, BattleActor Target) : BattleEvent(Turn);

public sealed record AbilityTriggered(
    int Turn, BattleActor Actor, string AbilityName) : BattleEvent(Turn);

public sealed record Fainted(
    int Turn, BattleActor Actor) : BattleEvent(Turn);

public sealed record MessageShown(
    int Turn, string Message) : BattleEvent(Turn);

/// <summary>Development-visible fallback for an effect not yet implemented by the simulation.</summary>
public sealed record EffectNotImplemented(
    int Turn, BattleActor Actor, int MoveId, string MoveName) : BattleEvent(Turn);
