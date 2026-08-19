using System.Linq;
using ProcPoke.Data;

namespace ProcPoke.Battle;

/// <summary>Persistent progression fields needed to apply one post-battle reward. <see cref="CurrentHp"/>
/// carries between battles (a Pokémon Center, not a fresh battle, is what restores it to max) — it is set
/// here by the caller holding the just-ended battle's live state, never guessed at by this record itself.</summary>
public sealed record BattleProgressionState
{
    public required int SpeciesId { get; init; }
    public required int Level { get; init; }
    public required int TotalExperience { get; init; }
    public required StatBlock EffortValues { get; init; }
    public required IReadOnlyList<int> MoveIds { get; init; }
    public required int CurrentHp { get; init; }
}

public sealed record BattleRewardResult(
    ExperienceGain Experience,
    MoveLearningResult Moves,
    EvolutionResult? Evolution,
    BattleProgressionState Progression);

/// <summary>Builds a fresh <see cref="BattleProgressionState"/> for a species starting at a given level —
/// e.g. a starter Pokémon at level 5 — with no EVs and its auto-derived level-up moveset.</summary>
public static class BattleProgressionFactory
{
    public static BattleProgressionState AtLevel(GameData data, int speciesId, int level)
    {
        ArgumentNullException.ThrowIfNull(data);
        var pokemon = BattlePokemonFactory.Create(data, speciesId, level);
        var growth = data.GrowthRates[data.Species[speciesId].GrowthRate];
        return new BattleProgressionState
        {
            SpeciesId = speciesId,
            Level = level,
            TotalExperience = growth.CumulativeExperience[level],
            EffortValues = new StatBlock(0, 0, 0, 0, 0, 0),
            MoveIds = pokemon.Moves.Select(move => move.Id).ToArray(),
            CurrentHp = pokemon.MaxHp,
        };
    }
}

/// <summary>Composes XP/EV, level-up move learning, and level-up evolution into one reward boundary.</summary>
public static class BattleRewardResolver
{
    /// <param name="survivingHp">The player Pokémon's HP at the moment this opponent fainted (from the
    /// live <c>BattleState</c> the caller is holding) — clamped into the result's (possibly higher, on a
    /// level-up or evolution) max HP.</param>
    public static BattleRewardResult ApplyDefeat(
        GameData data,
        BattleProgressionState current,
        int defeatedSpeciesId,
        int defeatedLevel,
        int survivingHp,
        EvolutionContext? evolutionContext = null,
        bool trainerBattle = false,
        int participants = 1)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(current);
        ValidateCurrent(data, current);
        if (survivingHp < 0) throw new ArgumentOutOfRangeException(nameof(survivingHp));

        var gain = ExperienceProgression.ApplyDefeat(
            data,
            current.SpeciesId,
            new ExperienceProgress
            {
                TotalExperience = current.TotalExperience,
                EffortValues = current.EffortValues,
            },
            defeatedSpeciesId,
            defeatedLevel,
            trainerBattle,
            participants);
        var moves = MoveLearningResolver.Resolve(
            data,
            current.SpeciesId,
            gain.PreviousLevel,
            gain.NewLevel,
            current.MoveIds);

        EvolutionResult? evolution = null;
        if (gain.NewLevel > gain.PreviousLevel)
        {
            // Equal Attack/Defense is neutral for the Tyrogue-style relative-stat rule (EvolutionResolver
            // compares Math.Sign(Attack - Defense)); a caller with no real battle stats only qualifies
            // for the "equal" branch of that rule, never "higher"/"lower".
            var context = evolutionContext ?? new EvolutionContext
            {
                SpeciesId = current.SpeciesId,
                Level = current.Level,
                Attack = 1,
                Defense = 1,
            };
            context = context with
            {
                SpeciesId = current.SpeciesId,
                Level = gain.NewLevel,
                KnownMoveIds = moves.MoveIds.ToHashSet(),
            };
            if (context.Trigger == EvolutionTrigger.LevelUp)
                evolution = EvolutionResolver.TryResolve(data, context);
        }

        var resultSpeciesId = evolution?.ToSpeciesId ?? current.SpeciesId;
        // A level-up or evolution can raise max HP; re-derive it rather than trusting the pre-battle max,
        // so a HP total above the OLD max (but within the new one) isn't clamped away.
        var maxHp = BattlePokemonFactory.MaxHpOf(data, resultSpeciesId, gain.NewLevel, gain.Progress.EffortValues);

        return new BattleRewardResult(
            gain,
            moves,
            evolution,
            new BattleProgressionState
            {
                SpeciesId = resultSpeciesId,
                Level = gain.NewLevel,
                TotalExperience = gain.Progress.TotalExperience,
                EffortValues = gain.Progress.EffortValues,
                MoveIds = moves.MoveIds,
                CurrentHp = Math.Min(survivingHp, maxHp),
            });
    }

    private static void ValidateCurrent(GameData data, BattleProgressionState current)
    {
        if (!data.Species.ContainsKey(current.SpeciesId))
            throw new ArgumentException($"Unknown progression species {current.SpeciesId}.", nameof(current));
        if (current.Level is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(current), "Level must be in [1, 100].");
        if (current.TotalExperience < 0)
            throw new ArgumentOutOfRangeException(nameof(current), "Total experience cannot be negative.");
        if (current.MoveIds.Count is < 1 or > 4
            || current.MoveIds.Distinct().Count() != current.MoveIds.Count
            || current.MoveIds.Any(moveId => !data.Moves.ContainsKey(moveId)))
            throw new ArgumentException("Progression must contain one to four distinct baked moves.", nameof(current));
    }
}
