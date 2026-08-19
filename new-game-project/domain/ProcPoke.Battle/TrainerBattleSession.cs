using ProcPoke.Data;

namespace ProcPoke.Battle;

/// <summary>One opponent roster slot: engine-neutral payload mirroring <see cref="WildBattleDefinition"/>
/// but for a trainer's full team (rival beats, gym leaders, generic trainers — anyone with more than one
/// Pokémon).</summary>
public sealed record TrainerRosterMember(int SpeciesId, int Level);

public enum TrainerBattleOutcome
{
    Ongoing,
    PlayerWon,
    PlayerLost,
}

public sealed record TrainerBattleTurnResult(
    BattleState State,
    IReadOnlyList<BattleEvent> Events,
    TrainerBattleOutcome Outcome);

/// <summary>
/// Owns one trainer battle's state across a full opponent roster: the player faces roster[0], then
/// roster[1], and so on as each faints, exactly like a mainline trainer sending out their next Pokémon.
/// There is no player-side switching yet — <see cref="Overworld.Party"/> only ever holds one battle-ready
/// member today — so a fainted player Pokémon ends the battle in a loss, same as a wild battle. Catching is
/// never legal here (no <see cref="ThrowBallAction"/>). The AI tier is the caller's call: gym leaders, the
/// Elite Four, the Champion and villain bosses fight at <see cref="BattleAiTier.Boss"/>, everyone else at
/// <see cref="BattleAiTier.Trainer"/>.
/// </summary>
public sealed class TrainerBattleSession
{
    private readonly IBattleRandom _random;
    private readonly TypeChart _typeChart;
    private readonly GameData _data;
    private readonly IReadOnlyList<TrainerRosterMember> _roster;
    private readonly string? _opponentIdPrefix;
    private readonly List<TrainerRosterMember> _defeated = [];
    private readonly BattleAiTier _tier;
    private int _potionsLeft;
    private int _opponentIndex;
    private bool _rewardsApplied;

    private TrainerBattleSession(
        IReadOnlyList<TrainerRosterMember> roster,
        string? opponentIdPrefix,
        BattleState state,
        IBattleRandom random,
        TypeChart typeChart,
        GameData data,
        BattleAiTier tier)
    {
        _roster = roster;
        _opponentIdPrefix = opponentIdPrefix;
        State = state;
        _random = random;
        _typeChart = typeChart;
        _data = data;
        _tier = tier;
        _potionsLeft = tier == BattleAiTier.Boss ? BossPotions : 0;
    }

    /// <summary>How many potions a boss brings (CONTEXT.md's AI Tier 2 heal). Small and fixed: difficulty
    /// is meant to come from team composition and levels, not from an unkillable healer.</summary>
    public const int BossPotions = 2;

    /// <summary>The medicine a boss reaches for, and the reason this session needs <c>GameData</c> for more
    /// than building opponents.</summary>
    private const string BossPotionName = "Hyper Potion";

    public BattleState State { get; private set; }
    public TrainerBattleOutcome Outcome { get; private set; } = TrainerBattleOutcome.Ongoing;

    /// <summary>1-based index of the opponent's currently active roster member (for a "2/5" style display).</summary>
    public int OpponentPosition => _opponentIndex + 1;
    public int OpponentRosterSize => _roster.Count;

    public static TrainerBattleSession Start(
        GameData data,
        BattlePokemon player,
        IReadOnlyList<TrainerRosterMember> roster,
        ulong seed,
        string? opponentIdPrefix = null,
        BattleAiTier tier = BattleAiTier.Trainer)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(roster);
        if (player.IsFainted)
            throw new ArgumentException("A trainer battle requires a non-fainted player Pokémon.", nameof(player));
        if (roster.Count == 0)
            throw new ArgumentException("A trainer battle requires a non-empty roster.", nameof(roster));

        if (tier == BattleAiTier.Wild)
            throw new ArgumentException("A trainer battle is never fought at the wild tier.", nameof(tier));

        var opponent = BuildOpponent(data, roster[0], opponentIdPrefix);
        return new TrainerBattleSession(
            roster,
            opponentIdPrefix,
            new BattleState { Player = player, Opponent = opponent },
            new BattleRandom(seed),
            data.TypeChart,
            data,
            tier);
    }

    public TrainerBattleTurnResult TakeTurn(BattleAction playerAction)
    {
        ArgumentNullException.ThrowIfNull(playerAction);
        if (Outcome != TrainerBattleOutcome.Ongoing)
            throw new InvalidOperationException("This trainer battle has already ended.");
        if (playerAction is ThrowBallAction)
            throw new ArgumentException("You cannot throw a ball in a trainer battle.", nameof(playerAction));

        var opponentAction = BattleAi.ChooseAction(
            State, BattleActor.Opponent, _tier, _random, _typeChart, BossPotion());
        if (opponentAction is UseItemAction) _potionsLeft--;
        var turn = BattleTurnResolver.Resolve(State, playerAction, opponentAction, _random, _typeChart);
        State = turn.State;

        if (State.Player.IsFainted)
        {
            Outcome = TrainerBattleOutcome.PlayerLost;
            return new TrainerBattleTurnResult(State, turn.Events, Outcome);
        }

        if (!State.Opponent.IsFainted)
            return new TrainerBattleTurnResult(State, turn.Events, Outcome);

        _defeated.Add(_roster[_opponentIndex]);
        _opponentIndex++;
        if (_opponentIndex >= _roster.Count)
        {
            Outcome = TrainerBattleOutcome.PlayerWon;
            return new TrainerBattleTurnResult(State, turn.Events, Outcome);
        }

        var next = BuildOpponent(_data, _roster[_opponentIndex], _opponentIdPrefix);
        State = State with { Opponent = next };
        var events = new List<BattleEvent>(turn.Events) { new MessageShown(State.TurnNumber, $"{next.Id} was sent out!") };
        return new TrainerBattleTurnResult(State, events, Outcome);
    }

    /// <summary>Applies one defeat reward per roster member actually defeated, threading progression
    /// (level, EVs, moves, evolution) through in order — matching how mainline awards XP the moment each
    /// opposing Pokémon faints, not once at the end.</summary>
    public IReadOnlyList<BattleRewardResult> ApplyVictoryRewards(
        BattleProgressionState current, EvolutionContext? evolutionContext = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (Outcome != TrainerBattleOutcome.PlayerWon)
            throw new InvalidOperationException("Experience rewards require a won trainer battle.");
        if (_rewardsApplied)
            throw new InvalidOperationException("This battle has already paid its progression reward.");
        if (current.SpeciesId != State.Player.SpeciesId)
            throw new ArgumentException("Progression state does not belong to the battle's player Pokémon.", nameof(current));

        var results = new List<BattleRewardResult>(_defeated.Count);
        var running = current;
        foreach (var defeated in _defeated)
        {
            var result = BattleRewardResolver.ApplyDefeat(
                _data, running, defeated.SpeciesId, defeated.Level, State.Player.Hp,
                evolutionContext, trainerBattle: true, participants: 1);
            results.Add(result);
            running = result.Progression;
        }
        _rewardsApplied = true;
        return results;
    }

    /// <summary>The boss's next potion, or null once it has spent them all.</summary>
    private UseItemAction? BossPotion()
    {
        if (_potionsLeft <= 0) return null;
        var item = _data.Items.Values.FirstOrDefault(candidate => candidate.Name == BossPotionName);
        if (item is null || MedicineCatalog.For(BossPotionName) is not { } medicine) return null;
        return new UseItemAction(item.Id, item.Name, medicine);
    }

    /// <summary>Potions this boss has left; zero for every non-boss trainer.</summary>
    public int OpponentPotionsLeft => _potionsLeft;

    private static BattlePokemon BuildOpponent(GameData data, TrainerRosterMember member, string? idPrefix)
    {
        if (member.Level is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(member), "Trainer roster level must be between 1 and 100.");
        if (!data.Species.TryGetValue(member.SpeciesId, out var species))
            throw new ArgumentException($"Unknown species {member.SpeciesId}.", nameof(member));
        var id = idPrefix is null ? species.Name : $"{idPrefix} {species.Name}";
        return BattlePokemonFactory.Create(data, member.SpeciesId, member.Level,
            new BattlePokemonBuildOptions { Id = id });
    }
}
