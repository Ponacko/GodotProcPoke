using ProcPoke.Data;

namespace ProcPoke.Battle;

/// <summary>Engine-neutral payload produced by the overworld encounter selector.</summary>
public sealed record WildBattleDefinition(
    int AreaId,
    int SpeciesId,
    int Level,
    bool HiddenAbility = false);

public enum WildBattleOutcome
{
    Ongoing,
    PlayerWon,
    PlayerLost,
    Captured,
}

public sealed record WildBattleTurnResult(
    BattleState State,
    IReadOnlyList<BattleEvent> Events,
    WildBattleOutcome Outcome);

/// <summary>
/// Owns one wild battle's state and injected random stream. Presentation submits a player action and
/// replays the returned events; it does not need to know how the wild action or outcome was resolved.
/// </summary>
public sealed class WildBattleSession
{
    private readonly IBattleRandom _random;
    private readonly TypeChart _typeChart;
    private readonly int _catchRate;
    private readonly GameData _data;
    private bool _rewardsApplied;

    private WildBattleSession(
        WildBattleDefinition encounter,
        BattleState state,
        IBattleRandom random,
        TypeChart typeChart,
        int catchRate,
        GameData data)
    {
        Encounter = encounter;
        State = state;
        _random = random;
        _typeChart = typeChart;
        _catchRate = catchRate;
        _data = data;
    }

    public WildBattleDefinition Encounter { get; }
    public BattleState State { get; private set; }
    public WildBattleOutcome Outcome { get; private set; } = WildBattleOutcome.Ongoing;

    public static WildBattleSession Start(
        GameData data,
        BattlePokemon player,
        WildBattleDefinition encounter,
        ulong seed,
        BattlePokemonBuildOptions? wildOptions = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(encounter);
        if (player.IsFainted)
            throw new ArgumentException("A wild battle requires a non-fainted player Pokémon.", nameof(player));
        if (encounter.Level is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(encounter), "Wild level must be between 1 and 100.");

        var species = data.Species.TryGetValue(encounter.SpeciesId, out var found)
            ? found
            : throw new ArgumentException($"Unknown species {encounter.SpeciesId}.", nameof(encounter));
        var options = wildOptions ?? new BattlePokemonBuildOptions();
        options = options with { Id = options.Id ?? $"Wild {species.Name}" };
        var opponent = BattlePokemonFactory.Create(data, encounter.SpeciesId, encounter.Level, options);
        return new WildBattleSession(
            encounter,
            new BattleState { Player = player, Opponent = opponent },
            new BattleRandom(seed),
            data.TypeChart,
            species.CatchRate,
            data);
    }

    public WildBattleTurnResult TakeTurn(BattleAction playerAction)
    {
        ArgumentNullException.ThrowIfNull(playerAction);
        if (Outcome != WildBattleOutcome.Ongoing)
            throw new InvalidOperationException("This wild battle has already ended.");

        if (playerAction is ThrowBallAction throwBall)
            return ThrowBall(throwBall);

        var wildAction = BattleAi.ChooseAction(
            State, BattleActor.Opponent, BattleAiTier.Wild, _random, _typeChart);
        var turn = BattleTurnResolver.Resolve(State, playerAction, wildAction, _random, _typeChart);
        State = turn.State;
        Outcome = State.Opponent.IsFainted
            ? WildBattleOutcome.PlayerWon
            : State.Player.IsFainted
                ? WildBattleOutcome.PlayerLost
                : WildBattleOutcome.Ongoing;
        return new WildBattleTurnResult(State, turn.Events, Outcome);
    }

    public BattleRewardResult ApplyVictoryRewards(
        BattleProgressionState current,
        EvolutionContext? evolutionContext = null,
        bool trainerBattle = false,
        int participants = 1)
    {
        if (Outcome != WildBattleOutcome.PlayerWon)
            throw new InvalidOperationException("Experience rewards require a won wild battle.");
        if (_rewardsApplied)
            throw new InvalidOperationException("This battle has already paid its progression reward.");
        if (current.SpeciesId != State.Player.SpeciesId)
            throw new ArgumentException("Progression state does not belong to the battle's player Pokémon.", nameof(current));

        var result = BattleRewardResolver.ApplyDefeat(
            _data,
            current,
            State.Opponent.SpeciesId,
            State.Opponent.Level,
            evolutionContext,
            trainerBattle,
            participants);
        _rewardsApplied = true;
        return result;
    }

    private WildBattleTurnResult ThrowBall(ThrowBallAction action)
    {
        if (string.IsNullOrWhiteSpace(action.BallId))
            throw new ArgumentException("A thrown ball needs an id.", nameof(action));
        if (action.BallMultiplier <= 0 || double.IsNaN(action.BallMultiplier)
            || double.IsInfinity(action.BallMultiplier))
            throw new ArgumentOutOfRangeException(nameof(action), "Ball multiplier must be finite and positive.");

        var turn = State.TurnNumber + 1;
        var events = new List<BattleEvent>
        {
            new TurnStarted(turn),
            new BallThrown(turn, BattleActor.Player, action.BallId),
        };
        var capture = CaptureResolver.Resolve(new CaptureAttempt
        {
            CatchRate = _catchRate,
            MaxHp = State.Opponent.MaxHp,
            Hp = State.Opponent.Hp,
            Status = State.Opponent.Status,
            BallMultiplier = action.BallMultiplier,
        }, _random);
        for (var index = 0; index < capture.Shakes.Count; index++)
            events.Add(new CaptureShake(turn, BattleActor.Player, index + 1, capture.Shakes[index]));

        if (capture.Captured)
        {
            events.Add(new PokemonCaptured(turn, BattleActor.Player, State.Opponent.SpeciesId));
            events.Add(new TurnEnded(turn));
            State = State with { TurnNumber = turn };
            Outcome = WildBattleOutcome.Captured;
            return new WildBattleTurnResult(State, events, Outcome);
        }

        events.Add(new CaptureFailed(turn, BattleActor.Player, capture.SuccessfulShakes));
        var wildAction = BattleAi.ChooseAction(
            State, BattleActor.Opponent, BattleAiTier.Wild, _random, _typeChart);
        var waitMessage = $"{State.Player.Id} waits.";
        var wildTurn = BattleTurnResolver.Resolve(
            State, new WaitAction(), wildAction, _random, _typeChart);
        State = wildTurn.State;
        events.AddRange(wildTurn.Events.Skip(1)
            .Where(battleEvent => battleEvent is not MessageShown shown || shown.Message != waitMessage));
        Outcome = State.Player.IsFainted ? WildBattleOutcome.PlayerLost : WildBattleOutcome.Ongoing;
        return new WildBattleTurnResult(State, events, Outcome);
    }
}
