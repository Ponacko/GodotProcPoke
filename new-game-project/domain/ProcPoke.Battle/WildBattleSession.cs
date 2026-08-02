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

    private WildBattleSession(
        WildBattleDefinition encounter,
        BattleState state,
        IBattleRandom random,
        TypeChart typeChart)
    {
        Encounter = encounter;
        State = state;
        _random = random;
        _typeChart = typeChart;
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
            data.TypeChart);
    }

    public WildBattleTurnResult TakeTurn(BattleAction playerAction)
    {
        ArgumentNullException.ThrowIfNull(playerAction);
        if (Outcome != WildBattleOutcome.Ongoing)
            throw new InvalidOperationException("This wild battle has already ended.");

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
}
