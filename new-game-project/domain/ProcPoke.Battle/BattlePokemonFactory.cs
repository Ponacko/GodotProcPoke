using ProcPoke.Data;

namespace ProcPoke.Battle;

public sealed record BattlePokemonBuildOptions
{
    public StatBlock IndividualValues { get; init; } = new(15, 15, 15, 15, 15, 15);
    public StatBlock EffortValues { get; init; }
    public int NatureId { get; init; } = 1;
    public string? Id { get; init; }
    public IReadOnlyList<int>? MoveIds { get; init; }
}

/// <summary>
/// Builds the immutable battle snapshot from baked species data using Gen 5 stat formulas. Runtime
/// callers choose IVs, EVs, nature, and moves explicitly; defaults are deterministic wild-battle values.
/// </summary>
public static class BattlePokemonFactory
{
    public static BattlePokemon Create(
        GameData data,
        int speciesId,
        int level,
        BattlePokemonBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= new BattlePokemonBuildOptions();
        if (!data.Species.TryGetValue(speciesId, out var species))
            throw new ArgumentException($"Unknown species {speciesId}.", nameof(speciesId));
        if (level is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(level));
        ValidateValues(options.IndividualValues, 31, "individual values");
        ValidateValues(options.EffortValues, 255, "effort values");
        if (Sum(options.EffortValues) > 510)
            throw new ArgumentException("Effort values exceed the total Gen 5 cap.", nameof(options));
        var nature = data.Natures.FirstOrDefault(candidate => candidate.Id == options.NatureId)
            ?? throw new ArgumentException($"Unknown nature {options.NatureId}.", nameof(options));
        var moveIds = ResolveMoves(data, speciesId, level, options.MoveIds);

        var hp = CalculateStat(species.BaseStats.Hp, options.IndividualValues.Hp,
            options.EffortValues.Hp, level, Stat.Hp, nature);
        var attack = CalculateStat(species.BaseStats.Attack, options.IndividualValues.Attack,
            options.EffortValues.Attack, level, Stat.Attack, nature);
        var defense = CalculateStat(species.BaseStats.Defense, options.IndividualValues.Defense,
            options.EffortValues.Defense, level, Stat.Defense, nature);
        var specialAttack = CalculateStat(species.BaseStats.SpecialAttack, options.IndividualValues.SpecialAttack,
            options.EffortValues.SpecialAttack, level, Stat.SpecialAttack, nature);
        var specialDefense = CalculateStat(species.BaseStats.SpecialDefense, options.IndividualValues.SpecialDefense,
            options.EffortValues.SpecialDefense, level, Stat.SpecialDefense, nature);
        var speed = CalculateStat(species.BaseStats.Speed, options.IndividualValues.Speed,
            options.EffortValues.Speed, level, Stat.Speed, nature);

        return new BattlePokemon
        {
            Id = options.Id ?? $"{species.Name} Lv{level}",
            SpeciesId = speciesId,
            Level = level,
            Speed = speed,
            Attack = attack,
            Defense = defense,
            SpecialAttack = specialAttack,
            SpecialDefense = specialDefense,
            Types = species.Types,
            MaxHp = hp,
            Hp = hp,
            Moves = moveIds.Select(moveId => data.Moves[moveId]).ToArray(),
        };
    }

    public static int CalculateStat(
        int baseStat,
        int individualValue,
        int effortValue,
        int level,
        Stat stat,
        Nature nature)
    {
        ArgumentNullException.ThrowIfNull(nature);
        var raw = (2 * baseStat + individualValue + effortValue / 4) * level / 100;
        if (stat == Stat.Hp) return raw + level + 10;
        var statValue = raw + 5;
        if (nature.IncreasedStat == stat) statValue = statValue * 110 / 100;
        if (nature.DecreasedStat == stat) statValue = statValue * 90 / 100;
        return Math.Max(1, statValue);
    }

    private static IReadOnlyList<int> ResolveMoves(
        GameData data, int speciesId, int level, IReadOnlyList<int>? requested)
    {
        if (requested is not null)
        {
            if (requested.Count is < 1 or > 4 || requested.Distinct().Count() != requested.Count)
                throw new ArgumentException("A battle Pokémon must have one to four distinct moves.", nameof(requested));
            if (requested.Any(moveId => !data.Moves.ContainsKey(moveId)))
                throw new ArgumentException("Requested moves must exist in baked data.", nameof(requested));
            return requested.ToArray();
        }

        var learned = data.Learnsets.GetValueOrDefault(speciesId, [])
            .Where(entry => entry.Method == LearnMethod.LevelUp && entry.Level <= level)
            .OrderBy(entry => entry.Level)
            .ThenBy(entry => entry.MoveId)
            .Select(entry => entry.MoveId)
            .Distinct()
            .TakeLast(4)
            .ToArray();
        if (learned.Length == 0)
            throw new InvalidOperationException($"Species {speciesId} has no level-up moves at level {level}.");
        return learned;
    }

    private static void ValidateValues(StatBlock values, int max, string label)
    {
        if (new[] { values.Hp, values.Attack, values.Defense, values.SpecialAttack,
            values.SpecialDefense, values.Speed }.Any(value => value is < 0 || value > max))
            throw new ArgumentException($"{label} must be within [0, {max}].");
    }

    private static int Sum(StatBlock values)
        => values.Hp + values.Attack + values.Defense + values.SpecialAttack
            + values.SpecialDefense + values.Speed;
}
