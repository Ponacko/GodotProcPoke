using ProcPoke.Data;

namespace ProcPoke.Battle;

public enum BattleAiTier
{
    Wild,
    Trainer,
    Boss,
}

/// <summary>
/// Selects an action for the active combatant, per the three-tier ladder (CONTEXT.md's AI Tier): wild picks
/// at random, a standard trainer picks the best damage with ~20% imperfection, and a boss picks the best
/// damage outright and reaches for a potion below a quarter HP. The tier-2 hopeless-matchup switch is still
/// missing: the opponent's roster is sent out in fixed order, so there is nothing to switch to yet.
/// </summary>
public static class BattleAi
{
    /// <summary>HP fraction below which a boss heals instead of attacking.</summary>
    public const double BossHealThreshold = 0.25;

    /// <param name="healingItem">A potion the boss still has, or null when it has none left. Ignored by
    /// every other tier — only a boss carries items.</param>
    public static BattleAction ChooseAction(
        BattleState state,
        BattleActor actor,
        BattleAiTier tier,
        IBattleRandom random,
        TypeChart? typeChart = null,
        UseItemAction? healingItem = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(random);
        var pokemon = actor == BattleActor.Player ? state.Player : state.Opponent;
        var target = actor == BattleActor.Player ? state.Opponent : state.Player;
        if (pokemon.IsFainted) return new WaitAction();
        if (pokemon.Moves.Count == 0)
            throw new InvalidOperationException($"{pokemon.Id} has no legal moves.");

        if (tier == BattleAiTier.Boss
            && healingItem is not null
            && pokemon.Hp < pokemon.MaxHp * BossHealThreshold)
            return healingItem;

        if (tier == BattleAiTier.Wild)
            return new UseMoveAction(pokemon.Moves[random.NextInt(pokemon.Moves.Count)].Id);

        if (tier == BattleAiTier.Trainer && random.NextInt(100) < 20)
            return new UseMoveAction(pokemon.Moves[random.NextInt(pokemon.Moves.Count)].Id);

        typeChart ??= NeutralTypeChart;
        var scored = pokemon.Moves
            .Select(move => (Move: move, Score: Score(move, pokemon, target, typeChart)))
            .ToArray();
        var bestScore = scored.Max(candidate => candidate.Score);
        var best = scored
            .Where(candidate => candidate.Score == bestScore)
            .Select(candidate => candidate.Move)
            .ToArray();
        return new UseMoveAction(best[random.NextInt(best.Length)].Id);
    }

    private static double Score(Move move, BattlePokemon attacker, BattlePokemon target, TypeChart chart)
    {
        if (move.Power is null)
        {
            if (move.Meta.Healing > 0 && attacker.Hp < attacker.MaxHp) return 35;
            if (move.Meta.AilmentId != 0 && target.Status == BattleStatus.None) return 30;
            if (move.Meta.StatChanges.Count > 0) return 25;
            return 1;
        }

        var effectiveness = target.Types.Aggregate(
            1.0, (multiplier, type) => multiplier * chart.Effectiveness(move.Type, type));
        var score = move.Power.Value * effectiveness;
        if (attacker.Types.Contains(move.Type)) score *= 1.5;
        if (move.Priority > 0) score += move.Priority * 0.01;
        return score;
    }

    private static readonly TypeChart NeutralTypeChart = new()
    {
        Percents = Enumerable.Range(0, Enum.GetValues<PokeType>().Length)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(100, Enum.GetValues<PokeType>().Length).ToArray())
            .ToArray(),
    };
}
