using ProcPoke.Data;

namespace ProcPoke.Bake;

/// <summary>
/// The <c>bake</c> subcommand. Loads the cached CSVs and transforms them into committed
/// <c>data/*.json</c>, snapshotted to Gen 5 (B2W2). All history-sensitive values are rewound through the
/// <c>_past</c> / <c>_changelog</c> tables (see the per-pass files). Output is deterministic: same pin +
/// same tool version ⇒ byte-identical files.
/// </summary>
internal sealed partial class Baker
{
    /// <summary>veekun local_language_id for English.</summary>
    private const int EnglishLang = 9;

    private readonly Paths _paths;
    private readonly Dictionary<string, CsvTable> _tables = new(StringComparer.Ordinal);

    /// <summary>Version-group id → sort order, for rewinding move changelogs.</summary>
    private readonly Dictionary<int, int> _vgOrder = new();

    private Baker(Paths paths)
    {
        _paths = paths;
        foreach (var (id, _, order) in LoadVersionGroups())
            _vgOrder[id] = order;
    }

    public static void Run(Paths paths)
    {
        if (!Directory.Exists(paths.CsvCache))
            throw new InvalidOperationException("CSV cache missing — run `ProcPoke.Bake fetch` first.");
        new Baker(paths).BakeAll();
    }

    private void BakeAll()
    {
        Directory.CreateDirectory(_paths.Data);

        var species = BakeSpecies();
        Write(GameDataLoader.Files.Species, species);
        Write(GameDataLoader.Files.Moves, BakeMoves());
        Write(GameDataLoader.Files.Learnsets, BakeLearnsets());
        Write(GameDataLoader.Files.TypeChart, BakeTypeChart());
        Write(GameDataLoader.Files.Evolutions, BakeEvolutions());
        Write(GameDataLoader.Files.Items, BakeItems());
        Write(GameDataLoader.Files.Natures, BakeNatures());
        Write(GameDataLoader.Files.GrowthRates, BakeGrowthRates());
        Write(GameDataLoader.Files.NameBlocklist, BakeNameBlocklist());
        Write(GameDataLoader.Files.Manifest, DataManifest.FromPin(species.Count));

        Console.WriteLine($"bake: {species.Count} species → {_paths.Data}");
    }

    // ---- shared helpers -----------------------------------------------------

    private CsvTable Table(string file)
    {
        if (!_tables.TryGetValue(file, out var t))
        {
            t = CsvTable.Load(_paths.Csv(file));
            _tables[file] = t;
        }
        return t;
    }

    private void Write<T>(string file, T value)
        => File.WriteAllText(Path.Combine(_paths.Data, file), DataJson.Serialize(value) + "\n");

    /// <summary>veekun type ids 1–17 map straight onto <see cref="PokeType"/>; 18 (Fairy) and up are dropped.</summary>
    private static PokeType? MapType(int typeId)
        => typeId is >= 1 and <= 17 ? (PokeType)(typeId - 1) : null;

    private static Stat MapStat(int statId) => statId switch
    {
        1 => Stat.Hp,
        2 => Stat.Attack,
        3 => Stat.Defense,
        4 => Stat.SpecialAttack,
        5 => Stat.SpecialDefense,
        6 => Stat.Speed,
        _ => throw new ArgumentOutOfRangeException(nameof(statId), statId, "not a permanent stat"),
    };

    /// <summary>Builds an id → English-name map from a *_names table with the given key/value columns.</summary>
    private Dictionary<int, string> EnglishNames(string file, string keyColumn)
    {
        var table = Table(file);
        var map = new Dictionary<int, string>();
        foreach (var row in table.Rows)
        {
            if (table.Int(row, "local_language_id") != EnglishLang) continue;
            map[table.Int(row, keyColumn)] = table.Str(row, "name");
        }
        return map;
    }

    private IEnumerable<(int Id, string Identifier, int Order)> LoadVersionGroups()
    {
        var t = CsvTable.Load(_paths.Csv("version_groups.csv"));
        foreach (var row in t.Rows)
            yield return (t.Int(row, "id"), t.Str(row, "identifier"), t.Int(row, "order"));
    }
}
