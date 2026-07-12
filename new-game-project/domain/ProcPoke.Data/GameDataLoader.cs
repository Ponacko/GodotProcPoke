using System.Text.Json;

namespace ProcPoke.Data;

/// <summary>
/// Reads committed <c>data/*.json</c> into a <see cref="GameData"/>. The reader shares record types and
/// JSON options with the bake tool (the writer), so the two cannot drift in shape.
/// </summary>
public static class GameDataLoader
{
    /// <summary>The file names the bake produces, relative to a data directory.</summary>
    public static class Files
    {
        public const string Species = "species.json";
        public const string Moves = "moves.json";
        public const string Learnsets = "learnsets.json";
        public const string TypeChart = "type_chart.json";
        public const string Evolutions = "evolutions.json";
        public const string Items = "items.json";
        public const string Natures = "natures.json";
        public const string GrowthRates = "growth_rates.json";
        public const string NameBlocklist = "name_blocklist.json";
        public const string Manifest = "manifest.json";
    }

    public static GameData Load(string dataDir)
    {
        var species = ReadArray<PokemonSpecies>(dataDir, Files.Species).ToDictionary(s => s.Id);
        var moves = ReadArray<Move>(dataDir, Files.Moves).ToDictionary(m => m.Id);
        var learnsets = ReadArray<SpeciesLearnset>(dataDir, Files.Learnsets)
            .ToDictionary(l => l.SpeciesId, l => l.Entries);
        var items = ReadArray<Item>(dataDir, Files.Items).ToDictionary(i => i.Id);
        var growthRates = ReadArray<GrowthRate>(dataDir, Files.GrowthRates)
            .ToDictionary(g => g.Name);

        return new GameData
        {
            Species = species,
            Moves = moves,
            Learnsets = learnsets,
            TypeChart = Read<TypeChart>(dataDir, Files.TypeChart),
            Evolutions = ReadArray<EvolutionRule>(dataDir, Files.Evolutions),
            Items = items,
            Natures = ReadArray<Nature>(dataDir, Files.Natures),
            GrowthRates = growthRates,
            NameBlocklist = ReadArray<string>(dataDir, Files.NameBlocklist),
            Manifest = Read<DataManifest>(dataDir, Files.Manifest),
        };
    }

    private static T Read<T>(string dataDir, string file)
    {
        var path = Path.Combine(dataDir, file);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, DataJson.Options)
            ?? throw new InvalidDataException($"{file} deserialized to null.");
    }

    private static IReadOnlyList<T> ReadArray<T>(string dataDir, string file)
        => Read<List<T>>(dataDir, file);
}
