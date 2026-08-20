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

    /// <summary>HP a roster member is carrying, once it has been out and come back. A Pokémon withdrawn
    /// from a hopeless matchup returns as damaged as it left, the way mainline switching works.</summary>
    private readonly Dictionary<int, int> _carriedHp = [];
    private readonly HashSet<int> _defeatedSlots = [];
    private int _potionsLeft;
    private int _opponentIndex;
    private bool _switchedOnce;
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

    /// <summary>
    /// The potions a boss may reach for, weakest first. Which one it uses depends on the Pokémon being
    /// healed, not on the trainer's rank: a fixed Hyper Potion would fully heal a first-gym team twice
    /// over, and difficulty is meant to come from team composition and levels, never from deeper AI
    /// (CONTEXT.md's AI Tier).
    /// </summary>
    private static readonly string[] BossPotionLadder = ["Potion", "Super Potion", "Hyper Potion"];

    /// <summary>Share of a Pokémon's maximum HP a boss's potion should be worth — enough to matter, not
    /// enough to undo the whole fight.</summary>
    private const double BossPotionShare = 0.5;

    public BattleState State { get; private set; }
    public TrainerBattleOutcome Outcome { get; private set; } = TrainerBattleOutcome.Ongoing;

    /// <summary>1-based index of the opponent's currently active roster member (for a "2/5" style display).</summary>
    public int OpponentPosition => _opponentIndex + 1;
    public int OpponentRosterSize => _roster.Count;

    /// <summary>Whether the boss has already spent its one hopeless-matchup switch (CONTEXT.md).</summary>
    public bool HasSwitched => _switchedOnce;

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

        var opponentAction = ChooseOpponentAction();
        if (opponentAction is UseItemAction) _potionsLeft--;

        // A switch happens before anything else in the turn, so the player's move lands on whoever came in.
        var switchEvents = new List<BattleEvent>();
        if (opponentAction is SwitchAction switchAction)
        {
            _switchedOnce = true;
            switchEvents.Add(SendOut(switchAction.RosterSlot, State.TurnNumber + 1, fainted: false));
        }

        var turn = BattleTurnResolver.Resolve(State, playerAction, opponentAction, _random, _typeChart);
        State = turn.State;
        var events = switchEvents.Concat(turn.Events).ToList();

        if (State.Player.IsFainted)
        {
            Outcome = TrainerBattleOutcome.PlayerLost;
            return new TrainerBattleTurnResult(State, events, Outcome);
        }

        if (!State.Opponent.IsFainted)
            return new TrainerBattleTurnResult(State, events, Outcome);

        _defeated.Add(_roster[_opponentIndex]);
        _defeatedSlots.Add(_opponentIndex);
        _carriedHp.Remove(_opponentIndex);
        if (NextSlotAfterFaint() is not int nextSlot)
        {
            Outcome = TrainerBattleOutcome.PlayerWon;
            return new TrainerBattleTurnResult(State, events, Outcome);
        }

        events.Add(SendOut(nextSlot, State.TurnNumber, fainted: true));
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

    /// <summary>
    /// What the trainer does this turn. A boss reaches for a potion below a quarter HP first (staying alive
    /// outranks position), and otherwise leaves a hopeless matchup once per battle — the two tier-2
    /// behaviours CONTEXT.md names, in the order it names them. Every other tier just picks a move.
    /// </summary>
    private BattleAction ChooseOpponentAction()
    {
        var action = BattleAi.ChooseAction(
            State, BattleActor.Opponent, _tier, _random, _typeChart, BossPotion());
        if (action is UseItemAction) return action;

        if (_tier != BattleAiTier.Boss || _switchedOnce) return action;
        if (!BattleAi.IsHopelessMatchup(State.Opponent, State.Player, _typeChart)) return action;
        return BetterMatchupSlot() is int slot ? new SwitchAction(slot) : action;
    }

    /// <summary>
    /// The alive team member with the best answer to what the player has out, or null when nobody is better
    /// off than the Pokémon already in — there is no point spending a turn to swap one hopeless matchup for
    /// another.
    /// </summary>
    private int? BetterMatchupSlot()
    {
        int? best = null;
        var bestScore = BattleAi.BestMoveScore(State.Opponent, State.Player, _typeChart);

        for (var slot = 0; slot < _roster.Count; slot++)
        {
            if (slot == _opponentIndex || _defeatedSlots.Contains(slot)) continue;
            var candidate = BuildOpponent(_data, _roster[slot], _opponentIdPrefix);
            if (BattleAi.IsHopelessMatchup(candidate, State.Player, _typeChart)) continue;

            var score = BattleAi.BestMoveScore(candidate, State.Player, _typeChart);
            if (score <= bestScore) continue;
            bestScore = score;
            best = slot;
        }
        return best;
    }

    /// <summary>The next member to send out when the active one faints: mainline order, first alive.</summary>
    private int? NextSlotAfterFaint()
    {
        for (var slot = 0; slot < _roster.Count; slot++)
            if (!_defeatedSlots.Contains(slot)) return slot;
        return null;
    }

    /// <summary>
    /// Puts a roster member into the battle and records what the one leaving was carrying, so a Pokémon
    /// withdrawn and sent back in later resumes at the HP it left with rather than healing for free.
    /// </summary>
    private PokemonSwitched SendOut(int slot, int turn, bool fainted)
    {
        var leaving = State.Opponent;
        if (!fainted) _carriedHp[_opponentIndex] = leaving.Hp;

        _opponentIndex = slot;
        var incoming = BuildOpponent(_data, _roster[slot], _opponentIdPrefix);
        if (_carriedHp.TryGetValue(slot, out var carried))
            incoming = incoming with { Hp = Math.Min(carried, incoming.MaxHp) };

        State = State with { Opponent = incoming };
        return new PokemonSwitched(turn, BattleActor.Opponent, leaving.SpeciesId, incoming.SpeciesId, fainted);
    }

    /// <summary>
    /// The boss's next potion, or null once it has spent them all: the weakest one on the ladder worth at
    /// least <see cref="BossPotionShare"/> of the active Pokémon's maximum, so a low-level gym leader heals
    /// with a Potion and a Champion with a Hyper Potion.
    /// </summary>
    private UseItemAction? BossPotion()
    {
        if (_potionsLeft <= 0) return null;
        var wanted = State.Opponent.MaxHp * BossPotionShare;

        UseItemAction? weakest = null;
        foreach (var name in BossPotionLadder)
        {
            if (_data.Items.Values.FirstOrDefault(candidate => candidate.Name == name) is not { } item) continue;
            if (MedicineCatalog.For(name) is not { } medicine) continue;
            weakest ??= new UseItemAction(item.Id, item.Name, medicine);
            if (medicine.HpRestoredFor(State.Opponent.MaxHp) >= wanted)
                return new UseItemAction(item.Id, item.Name, medicine);
        }
        // Nothing on the ladder reaches half its health: the strongest is the best it can do.
        return weakest is null ? null : StrongestPotion() ?? weakest;
    }

    private UseItemAction? StrongestPotion()
    {
        for (var i = BossPotionLadder.Length - 1; i >= 0; i--)
        {
            var name = BossPotionLadder[i];
            if (_data.Items.Values.FirstOrDefault(candidate => candidate.Name == name) is not { } item) continue;
            if (MedicineCatalog.For(name) is { } medicine)
                return new UseItemAction(item.Id, item.Name, medicine);
        }
        return null;
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
