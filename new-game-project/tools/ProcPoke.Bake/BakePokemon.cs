using ProcPoke.Data;

namespace ProcPoke.Bake;

internal sealed partial class Baker
{
    /// <summary>
    /// PokeAPI's "past value" convention: a <c>_past</c> row's generation_id is the last generation the
    /// value was in effect. To read the value as of Gen 5, take, per field, the past row with the
    /// smallest generation_id that is still ≥ 5; if none exists the current table already holds the Gen 5
    /// value. Returns the rows of that earliest applicable generation (empty ⇒ use current).
    /// </summary>
    private static List<TRow> PastSnapshot<TRow>(IEnumerable<TRow> rows, Func<TRow, int> gen)
    {
        var applicable = rows.Where(r => gen(r) >= DataPin.GenerationId).ToList();
        if (applicable.Count == 0) return [];
        var earliest = applicable.Min(gen);
        return applicable.Where(r => gen(r) == earliest).ToList();
    }

    private List<PokemonSpecies> BakeSpecies()
    {
        var speciesTable = Table("pokemon_species.csv");
        var pokemon = Table("pokemon.csv");
        var stats = Table("pokemon_stats.csv");
        var statsPast = Table("pokemon_stats_past.csv");
        var types = Table("pokemon_types.csv");
        var typesPast = Table("pokemon_types_past.csv");
        var abilities = Table("pokemon_abilities.csv");
        var abilitiesPast = Table("pokemon_abilities_past.csv");

        var growthName = GrowthRateIdentifiers();
        var abilityNames = EnglishNames("ability_names.csv", "ability_id");
        var abilityIdentifiers = AbilityIdentifiers();
        var speciesNames = EnglishNames("pokemon_species_names.csv", "pokemon_species_id");

        // Default pokemon row per species (base form) → pokemon id + base experience.
        var defaultPokemon = new Dictionary<int, (int PokemonId, int BaseExp)>();
        foreach (var row in pokemon.Rows)
        {
            if (!pokemon.Bool(row, "is_default")) continue;
            var sid = pokemon.Int(row, "species_id");
            if (sid > DataPin.MaxSpeciesId) continue;
            defaultPokemon[sid] = (pokemon.Int(row, "id"), pokemon.Int(row, "base_experience"));
        }

        // Group the per-pokemon CSVs once.
        var statsByMon = GroupBy(stats, "pokemon_id");
        var statsPastByMon = GroupBy(statsPast, "pokemon_id");
        var typesByMon = GroupBy(types, "pokemon_id");
        var typesPastByMon = GroupBy(typesPast, "pokemon_id");
        var abilitiesByMon = GroupBy(abilities, "pokemon_id");
        var abilitiesPastByMon = GroupBy(abilitiesPast, "pokemon_id");

        var result = new List<PokemonSpecies>();

        foreach (var row in speciesTable.Rows)
        {
            var sid = speciesTable.Int(row, "id");
            if (sid > DataPin.MaxSpeciesId) continue;
            if (!defaultPokemon.TryGetValue(sid, out var def))
                throw new InvalidDataException($"species {sid} has no default pokemon row.");

            var monId = def.PokemonId;
            var name = speciesNames.GetValueOrDefault(sid, TitleCase(speciesTable.Str(row, "identifier")));

            var (baseStats, evYield) = ResolveStats(sid, monId, statsByMon, statsPastByMon, stats, statsPast);
            var pokeTypes = ResolveTypes(sid, monId, typesByMon, typesPastByMon, types, typesPast);
            var (a1, a2, hidden) = ResolveAbilities(
                monId, abilitiesByMon, abilitiesPastByMon, abilities, abilitiesPast,
                abilityNames, abilityIdentifiers);

            result.Add(new PokemonSpecies
            {
                Id = sid,
                Name = name,
                Types = pokeTypes,
                BaseStats = baseStats,
                Ability1 = a1,
                Ability2 = a2,
                HiddenAbility = hidden,
                CatchRate = speciesTable.Int(row, "capture_rate"),
                BaseExperience = def.BaseExp,
                EvYield = evYield,
                GrowthRate = growthName[speciesTable.Int(row, "growth_rate_id")],
                GenderRate = speciesTable.Int(row, "gender_rate"),
                IsLegendary = speciesTable.Bool(row, "is_legendary"),
                IsMythical = speciesTable.Bool(row, "is_mythical"),
            });
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    private (StatBlock Base, StatBlock Ev) ResolveStats(
        int sid, int monId,
        Dictionary<int, List<string[]>> byMon, Dictionary<int, List<string[]>> pastByMon,
        CsvTable stats, CsvTable statsPast)
    {
        var baseVals = new Dictionary<Stat, int>();
        var evVals = new Dictionary<Stat, int>();
        foreach (var r in byMon.GetValueOrDefault(monId, []))
        {
            var statId = stats.Int(r, "stat_id");
            if (statId is < 1 or > 6) continue;
            var st = MapStat(statId);
            baseVals[st] = stats.Int(r, "base_stat");
            evVals[st] = stats.Int(r, "effort");
        }

        // Per-stat past override (only base stat; EV yields have no historical table).
        var past = pastByMon.GetValueOrDefault(monId, []);
        foreach (var group in past.Where(r => statsPast.Int(r, "stat_id") is >= 1 and <= 6)
                     .GroupBy(r => statsPast.Int(r, "stat_id")))
        {
            var snap = PastSnapshot(group, r => statsPast.Int(r, "generation_id"));
            if (snap.Count == 0) continue;
            baseVals[MapStat(group.Key)] = statsPast.Int(snap[0], "base_stat");
        }

        if (baseVals.Count != 6)
            throw new InvalidDataException($"species {sid} missing base stats (got {baseVals.Count}).");

        StatBlock Block(Dictionary<Stat, int> v) => new(
            v.GetValueOrDefault(Stat.Hp), v.GetValueOrDefault(Stat.Attack), v.GetValueOrDefault(Stat.Defense),
            v.GetValueOrDefault(Stat.SpecialAttack), v.GetValueOrDefault(Stat.SpecialDefense), v.GetValueOrDefault(Stat.Speed));

        return (Block(baseVals), Block(evVals));
    }

    private List<PokeType> ResolveTypes(
        int sid, int monId,
        Dictionary<int, List<string[]>> byMon, Dictionary<int, List<string[]>> pastByMon,
        CsvTable types, CsvTable typesPast)
    {
        // Typing is restamped atomically per generation; take the whole earliest-applicable past set.
        var past = pastByMon.GetValueOrDefault(monId, []);
        var snap = PastSnapshot(past, r => typesPast.Int(r, "generation_id"));

        List<(int Slot, int TypeId)> rows = snap.Count > 0
            ? snap.Select(r => (typesPast.Int(r, "slot"), typesPast.Int(r, "type_id"))).ToList()
            : byMon.GetValueOrDefault(monId, []).Select(r => (types.Int(r, "slot"), types.Int(r, "type_id"))).ToList();

        var result = rows.OrderBy(r => r.Slot)
            .Select(r => MapType(r.TypeId))
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .ToList();

        if (result.Count == 0)
            throw new InvalidDataException($"species {sid} resolved to no Gen 5 types.");
        return result;
    }

    private (AbilitySlot A1, AbilitySlot? A2, AbilitySlot? Hidden) ResolveAbilities(
        int monId,
        Dictionary<int, List<string[]>> byMon, Dictionary<int, List<string[]>> pastByMon,
        CsvTable abilities, CsvTable abilitiesPast,
        Dictionary<int, string> abilityNames, Dictionary<int, string> abilityIdentifiers)
    {
        // Resolve each slot independently; a past snapshot may blank a slot (null ability_id).
        var currentBySlot = byMon.GetValueOrDefault(monId, [])
            .ToDictionary(r => abilities.Int(r, "slot"), r => abilities.IntOrNull(r, "ability_id"));

        var pastBySlot = pastByMon.GetValueOrDefault(monId, [])
            .GroupBy(r => abilitiesPast.Int(r, "slot"));

        var resolved = new Dictionary<int, int?>(currentBySlot);
        foreach (var group in pastBySlot)
        {
            var snap = PastSnapshot(group, r => abilitiesPast.Int(r, "generation_id"));
            if (snap.Count == 0) continue;
            resolved[group.Key] = abilitiesPast.IntOrNull(snap[0], "ability_id");
        }

        AbilitySlot? Slot(int slot)
        {
            if (!resolved.TryGetValue(slot, out var id) || id is null) return null;
            var name = abilityNames.GetValueOrDefault(id.Value)
                ?? TitleCase(abilityIdentifiers.GetValueOrDefault(id.Value, $"ability-{id}"));
            return new AbilitySlot(id.Value, name);
        }

        var a1 = Slot(1) ?? throw new InvalidDataException($"pokemon {monId} has no slot-1 ability.");
        return (a1, Slot(2), Slot(3));
    }

    private TypeChart BakeTypeChart()
    {
        const int n = 17;
        var matrix = new int[n][];
        for (var i = 0; i < n; i++)
        {
            matrix[i] = new int[n];
            Array.Fill(matrix[i], 100);
        }

        var eff = Table("type_efficacy.csv");
        foreach (var r in eff.Rows)
        {
            var dt = MapType(eff.Int(r, "damage_type_id"));
            var tt = MapType(eff.Int(r, "target_type_id"));
            if (dt is null || tt is null) continue;
            matrix[(int)dt][(int)tt] = eff.Int(r, "damage_factor");
        }

        var past = Table("type_efficacy_past.csv");
        foreach (var group in past.Rows.GroupBy(r => (past.Int(r, "damage_type_id"), past.Int(r, "target_type_id"))))
        {
            var dt = MapType(group.Key.Item1);
            var tt = MapType(group.Key.Item2);
            if (dt is null || tt is null) continue;
            var snap = PastSnapshot(group, r => past.Int(r, "generation_id"));
            if (snap.Count == 0) continue;
            matrix[(int)dt][(int)tt] = past.Int(snap[0], "damage_factor");
        }

        return new TypeChart { Percents = matrix.Select(row => (IReadOnlyList<int>)row).ToList() };
    }

    private Dictionary<int, string> GrowthRateIdentifiers()
    {
        var t = Table("growth_rates.csv");
        return t.Rows.ToDictionary(r => t.Int(r, "id"), r => t.Str(r, "identifier"));
    }

    private Dictionary<int, string> AbilityIdentifiers()
    {
        var t = Table("abilities.csv");
        return t.Rows.ToDictionary(r => t.Int(r, "id"), r => t.Str(r, "identifier"));
    }

    private static Dictionary<int, List<string[]>> GroupBy(CsvTable table, string column)
    {
        var map = new Dictionary<int, List<string[]>>();
        foreach (var row in table.Rows)
        {
            var key = table.Int(row, column);
            if (!map.TryGetValue(key, out var list))
                map[key] = list = [];
            list.Add(row);
        }
        return map;
    }

    private static string TitleCase(string identifier)
        => string.Join('-', identifier.Split('-').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
}
