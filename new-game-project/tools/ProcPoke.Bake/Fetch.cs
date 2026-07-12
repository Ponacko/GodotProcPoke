using ProcPoke.Data;

namespace ProcPoke.Bake;

/// <summary>
/// The <c>fetch</c> subcommand: downloads exactly the CSVs the bake needs from
/// <see cref="DataPin.SourceRepo"/> at <see cref="DataPin.SourceCommitSha"/> into the gitignored cache.
/// Idempotent — already-cached files are skipped unless <c>--force</c>.
/// </summary>
internal static class Fetch
{
    /// <summary>Every CSV any bake pass reads. Fetching only these keeps the cache small and auditable.</summary>
    public static readonly string[] Files =
    [
        // species
        "pokemon.csv", "pokemon_species.csv", "pokemon_species_names.csv",
        "pokemon_stats.csv", "pokemon_stats_past.csv",
        "pokemon_types.csv", "pokemon_types_past.csv",
        "pokemon_abilities.csv", "pokemon_abilities_past.csv",
        "abilities.csv", "ability_names.csv",
        // moves
        "moves.csv", "move_changelog.csv", "move_names.csv",
        "move_meta.csv", "move_meta_stat_changes.csv",
        "version_groups.csv",
        // learnsets
        "pokemon_moves.csv", "pokemon_move_methods.csv",
        // type chart
        "type_efficacy.csv", "type_efficacy_past.csv",
        // evolutions
        "pokemon_evolution.csv", "evolution_triggers.csv",
        // items
        "items.csv", "item_names.csv", "item_categories.csv",
        // natures / growth
        "natures.csv", "nature_names.csv",
        "growth_rates.csv", "experience.csv",
        // name blocklist
        "location_names.csv", "region_names.csv",
    ];

    public static async Task RunAsync(Paths paths, bool force)
    {
        Directory.CreateDirectory(paths.CsvCache);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ProcPoke.Bake/1.0");

        var baseUrl = $"https://raw.githubusercontent.com/{DataPin.SourceRepo}/{DataPin.SourceCommitSha}/data/v2/csv";
        var downloaded = 0;

        foreach (var file in Files)
        {
            var dest = paths.Csv(file);
            if (!force && File.Exists(dest))
                continue;

            var url = $"{baseUrl}/{file}";
            Console.WriteLine($"  fetch {file}");
            var bytes = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(dest, bytes);
            downloaded++;
        }

        Console.WriteLine($"fetch: {downloaded} downloaded, {Files.Length - downloaded} cached "
            + $"({DataPin.SourceRepo}@{DataPin.SourceCommitSha[..12]}).");
    }
}
