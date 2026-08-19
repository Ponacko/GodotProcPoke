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
    ThrowBall,
    UseItem,
    Wait,
}

public abstract record BattleAction(BattleActionKind Kind);

public sealed record UseMoveAction(int MoveId) : BattleAction(BattleActionKind.UseMove);

/// <summary>Wild-battle-only action; the session resolves capture and shake events.</summary>
public sealed record ThrowBallAction(
    string BallId,
    double BallMultiplier = 1.0) : BattleAction(BattleActionKind.ThrowBall);

/// <summary>
/// A bag item used on the actor's own active Pokémon. The effect travels with the action because the
/// simulation reads no game data of its own — the caller, which owns <c>GameData</c>, resolves the item to
/// a <see cref="Medicine"/> first. Items act before every move and are never stopped by a status condition,
/// exactly as in Gen 5.
/// </summary>
public sealed record UseItemAction(
    int ItemId, string ItemName, Medicine Medicine) : BattleAction(BattleActionKind.UseItem);

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

public sealed record ItemUsed(
    int Turn, BattleActor Actor, int ItemId, string ItemName) : BattleEvent(Turn);

public sealed record BallThrown(
    int Turn, BattleActor Actor, string BallId) : BattleEvent(Turn);

public sealed record CaptureShake(
    int Turn, BattleActor Actor, int ShakeIndex, bool Succeeded) : BattleEvent(Turn);

public sealed record CaptureFailed(
    int Turn, BattleActor Actor, int SuccessfulShakes) : BattleEvent(Turn);

public sealed record PokemonCaptured(
    int Turn, BattleActor Actor, int SpeciesId) : BattleEvent(Turn);

public sealed record AbilityTriggered(
    int Turn, BattleActor Actor, string AbilityName) : BattleEvent(Turn);

public sealed record Fainted(
    int Turn, BattleActor Actor) : BattleEvent(Turn);

public sealed record MessageShown(
    int Turn, string Message) : BattleEvent(Turn);

/// <summary>Development-visible fallback for an effect not yet implemented by the simulation.</summary>
public sealed record EffectNotImplemented(
    int Turn, BattleActor Actor, int MoveId, string MoveName) : BattleEvent(Turn);
