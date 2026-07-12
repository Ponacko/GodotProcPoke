using ProcPoke.Data;

namespace ProcPoke.Bake;

internal sealed partial class Baker
{
    /// <summary>Move ids that survived the Gen 5 filter — learnsets are pruned to these.</summary>
    private HashSet<int> _bakedMoveIds = [];

    private List<Move> BakeMoves()
    {
        var moves = Table("moves.csv");
        var names = EnglishNames("move_names.csv", "move_id");
        var meta = GroupBy(Table("move_meta.csv"), "move_id");
        var statChanges = GroupBy(Table("move_meta_stat_changes.csv"), "move_id");
        var changelog = GroupBy(Table("move_changelog.csv"), "move_id");

        var metaTable = Table("move_meta.csv");
        var statTable = Table("move_meta_stat_changes.csv");
        var logTable = Table("move_changelog.csv");

        var result = new List<Move>();

        foreach (var row in moves.Rows)
        {
            if (moves.Int(row, "generation_id") > DataPin.GenerationId) continue;
            var id = moves.Int(row, "id");

            // Rewind each field through changes that happened AFTER B2W2 (each changelog cell holds the
            // pre-change value; the earliest post-B2W2 change for a field == that field's B2W2 value).
            var log = changelog.GetValueOrDefault(id, []);
            int? Rewound(string column, int? current)
            {
                var candidates = log
                    .Where(r => _vgOrder.TryGetValue(logTable.Int(r, "changed_in_version_group_id"), out var o)
                                && o > DataPin.VersionGroupOrder
                                && logTable.Str(r, column).Length > 0)
                    .OrderBy(r => _vgOrder[logTable.Int(r, "changed_in_version_group_id")])
                    .ToList();
                return candidates.Count > 0 ? logTable.Int(candidates[0], column) : current;
            }

            var typeId = Rewound("type_id", moves.Int(row, "type_id"))!.Value;
            var type = MapType(typeId);
            if (type is null) continue; // shadow/??? typed non-standard moves are out of scope

            result.Add(new Move
            {
                Id = id,
                Name = names.GetValueOrDefault(id, TitleCase(moves.Str(row, "identifier"))),
                Type = type.Value,
                Power = Rewound("power", moves.IntOrNull(row, "power")),
                Accuracy = Rewound("accuracy", moves.IntOrNull(row, "accuracy")),
                Pp = Rewound("pp", moves.Int(row, "pp"))!.Value,
                Priority = Rewound("priority", moves.Int(row, "priority"))!.Value,
                DamageClass = MapDamageClass(moves.Int(row, "damage_class_id")),
                EffectId = Rewound("effect_id", moves.Int(row, "effect_id"))!.Value,
                EffectChance = Rewound("effect_chance", moves.IntOrNull(row, "effect_chance")),
                Meta = BuildMeta(id, meta, metaTable, statChanges, statTable),
            });
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        _bakedMoveIds = result.Select(m => m.Id).ToHashSet();
        return result;
    }

    private static DamageClass MapDamageClass(int id) => id switch
    {
        1 => DamageClass.Status,
        2 => DamageClass.Physical,
        3 => DamageClass.Special,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown damage class"),
    };

    private static MoveMeta BuildMeta(
        int moveId,
        Dictionary<int, List<string[]>> meta, CsvTable metaTable,
        Dictionary<int, List<string[]>> statChanges, CsvTable statTable)
    {
        var changes = statChanges.GetValueOrDefault(moveId, [])
            .Select(r => (Stat: MapStatFromMeta(statTable.Int(r, "stat_id")), Change: statTable.Int(r, "change")))
            .Where(c => c.Stat is not null)
            .Select(c => new MoveStatChange(c.Stat!.Value, c.Change))
            .ToList();

        var rows = meta.GetValueOrDefault(moveId, []);
        if (rows.Count == 0)
            return new MoveMeta { CategoryId = 0, AilmentId = 0, StatChanges = changes };

        var r0 = rows[0];
        return new MoveMeta
        {
            CategoryId = metaTable.Int(r0, "meta_category_id"),
            AilmentId = metaTable.Int(r0, "meta_ailment_id"),
            MinHits = metaTable.IntOrNull(r0, "min_hits"),
            MaxHits = metaTable.IntOrNull(r0, "max_hits"),
            MinTurns = metaTable.IntOrNull(r0, "min_turns"),
            MaxTurns = metaTable.IntOrNull(r0, "max_turns"),
            Drain = metaTable.Int(r0, "drain"),
            Healing = metaTable.Int(r0, "healing"),
            CritRateBonus = metaTable.Int(r0, "crit_rate"),
            AilmentChance = metaTable.Int(r0, "ailment_chance"),
            FlinchChance = metaTable.Int(r0, "flinch_chance"),
            StatChance = metaTable.Int(r0, "stat_chance"),
            StatChanges = changes,
        };
    }

    // meta stat changes can reference accuracy(7)/evasion(8), which are battle-only and not permanent stats.
    private static Stat? MapStatFromMeta(int statId)
        => statId is >= 1 and <= 6 ? MapStat(statId) : null;

    private List<SpeciesLearnset> BakeLearnsets()
    {
        var pm = Table("pokemon_moves.csv");
        var monToSpecies = DefaultFormSpeciesMap();

        var byMon = new Dictionary<int, List<LearnsetEntry>>();
        foreach (var row in pm.Rows)
        {
            if (pm.Int(row, "version_group_id") != DataPin.VersionGroupId) continue;
            var monId = pm.Int(row, "pokemon_id");
            if (!monToSpecies.TryGetValue(monId, out var sid)) continue;

            var method = MapLearnMethod(pm.Int(row, "pokemon_move_method_id"));
            if (method is null) continue;

            var moveId = pm.Int(row, "move_id");
            if (!_bakedMoveIds.Contains(moveId)) continue;

            var level = method == LearnMethod.LevelUp ? pm.Int(row, "level") : 0;
            (byMon.TryGetValue(sid, out var list) ? list : byMon[sid] = [])
                .Add(new LearnsetEntry(moveId, method.Value, level));
        }

        var result = new List<SpeciesLearnset>();
        foreach (var (sid, entries) in byMon)
        {
            var ordered = entries
                .Distinct()
                .OrderBy(e => e.Method)
                .ThenBy(e => e.Level)
                .ThenBy(e => e.MoveId)
                .ToList();
            result.Add(new SpeciesLearnset { SpeciesId = sid, Entries = ordered });
        }

        result.Sort((a, b) => a.SpeciesId.CompareTo(b.SpeciesId));
        return result;
    }

    private static LearnMethod? MapLearnMethod(int id) => id switch
    {
        1 => LearnMethod.LevelUp,
        2 => LearnMethod.Egg,
        3 => LearnMethod.Tutor,
        4 => LearnMethod.Machine,
        _ => null,
    };

    /// <summary>Maps each base-form pokemon id (species 1–649) to its species id.</summary>
    private Dictionary<int, int> DefaultFormSpeciesMap()
    {
        var pokemon = Table("pokemon.csv");
        var map = new Dictionary<int, int>();
        foreach (var row in pokemon.Rows)
        {
            if (!pokemon.Bool(row, "is_default")) continue;
            var sid = pokemon.Int(row, "species_id");
            if (sid > DataPin.MaxSpeciesId) continue;
            map[pokemon.Int(row, "id")] = sid;
        }
        return map;
    }
}
