using ProcPoke.Data;

namespace ProcPoke.Battle;

/// <summary>A minimal immutable combatant snapshot consumed by the Phase 4 simulation seam.</summary>
public sealed record BattlePokemon
{
    public required string Id { get; init; }
    public required int SpeciesId { get; init; }
    public required int Level { get; init; }
    public required int Speed { get; init; }
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
        IBattleRandom random)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(playerAction);
        ArgumentNullException.ThrowIfNull(opponentAction);
        ArgumentNullException.ThrowIfNull(random);
        ValidateAction(state.Player, playerAction);
        ValidateAction(state.Opponent, opponentAction);

        var turn = state.TurnNumber + 1;
        var events = new List<BattleEvent> { new TurnStarted(turn) };
        var actions = Order(state, playerAction, opponentAction, random);
        foreach (var (actor, action) in actions)
        {
            var pokemon = actor == BattleActor.Player ? state.Player : state.Opponent;
            if (pokemon.IsFainted) continue;

            if (action is WaitAction)
            {
                events.Add(new MessageShown(turn, $"{pokemon.Id} waits."));
                continue;
            }

            var moveAction = (UseMoveAction)action;
            var move = pokemon.Moves.Single(candidate => candidate.Id == moveAction.MoveId);
            events.Add(new MoveUsed(turn, actor, move.Id, move.Name));
            events.Add(new EffectNotImplemented(turn, actor, move.Id, move.Name));
        }

        events.Add(new TurnEnded(turn));
        return new BattleTurnResult(state with { TurnNumber = turn }, events);
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
        if (state.Player.Speed != state.Opponent.Speed)
            return state.Player.Speed > state.Opponent.Speed ? [player, opponent] : [opponent, player];
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
}

public sealed record TurnEnded(int Turn) : BattleEvent(Turn);
