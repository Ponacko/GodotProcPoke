using ProcPoke.Data;

namespace ProcPoke.Battle;

public enum BattleAiTier
{
    Wild,
    Trainer,
    Boss,
}

/// <summary>
/// Selects a move for the active combatant. Party switching and item use require a party/inventory
/// state that is not part of the current single-active battle contract and remain presentation wiring.
/// </summary>
public static class BattleAi
{
    public static BattleAction ChooseAction(
        BattleState state,
        BattleActor actor,
        BattleAiTier tier,
        IBattleRandom random,
        TypeChart? typeChart = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(random);
        var pokemon = actor == BattleActor.Player ? state.Player : state.Opponent;
        var target = actor == BattleActor.Player ? state.Opponent : state.Player;
        if (pokemon.IsFainted) return new WaitAction();
        if (pokemon.Moves.Count == 0)
            throw new InvalidOperationException($"{pokemon.Id} has no legal moves.");

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
