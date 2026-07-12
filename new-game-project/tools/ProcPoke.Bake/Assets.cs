using ProcPoke.Data;

namespace ProcPoke.Bake;

/// <summary>
/// The <c>assets</c> subcommand: downloads the Gen 5 animated battle sprites, menu icons, and cries that
/// share the species id scheme with the baked data (GDD §10). Assets stay out of git — this tool plus the
/// pinned SHAs below are the reproducibility story. Defaults to a small sample; pass <c>--all</c> for 1–649.
/// </summary>
internal static class Assets
{
    // Pinned asset-repo commits (independent of the CSV pin; assets are large and versioned separately).
    private const string SpritesRepo = "PokeAPI/sprites";
    private const string SpritesSha = "bf4c47ac82c33b330e33d98b8882d1cedb2f53e7";
    private const string CriesRepo = "PokeAPI/cries";
    private const string CriesSha = "7ba07038103b3482973fa781e25c09debbaaedd8";
    private const string PokeSpriteRepo = "msikma/pokesprite";
    private const string PokeSpriteSha = "c5aaa610ff2acdf7fd8e2dccd181bca8be9fcb3e";

    public static async Task RunAsync(Paths paths, string[] rest, bool force)
    {
        var all = rest.Contains("--all");
        var limit = ParseLimit(rest) ?? (all ? DataPin.MaxSpeciesId : 3);

        // Slugs come from the baked species data so icon lookups match exactly what shipped.
        var slugs = LoadSpeciesSlugs(paths);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ProcPoke.Bake/1.0");

        var spritesDir = Path.Combine(paths.Assets, "battle");
        var iconsDir = Path.Combine(paths.Assets, "icons");
        var criesDir = Path.Combine(paths.Assets, "cries");
        foreach (var d in new[] { spritesDir, iconsDir, criesDir }) Directory.CreateDirectory(d);

        var ok = 0;
        var missed = 0;
        for (var id = 1; id <= limit && id <= DataPin.MaxSpeciesId; id++)
        {
            var slug = slugs.GetValueOrDefault(id);
            var jobs = new (string Url, string Dest)[]
            {
                ($"https://raw.githubusercontent.com/{SpritesRepo}/{SpritesSha}/sprites/pokemon/versions/generation-v/black-white/animated/{id}.gif",
                    Path.Combine(spritesDir, $"{id}.gif")),
                ($"https://raw.githubusercontent.com/{CriesRepo}/{CriesSha}/cries/pokemon/legacy/{id}.ogg",
                    Path.Combine(criesDir, $"{id}.ogg")),
                (slug is null ? "" :
                    $"https://raw.githubusercontent.com/{PokeSpriteRepo}/{PokeSpriteSha}/pokemon-gen8/regular/{slug}.png",
                    Path.Combine(iconsDir, $"{id}.png")),
            };

            foreach (var (url, dest) in jobs)
            {
                if (url.Length == 0) continue;
                if (!force && File.Exists(dest)) { ok++; continue; }
                try
                {
                    var bytes = await http.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(dest, bytes);
                    ok++;
                }
                catch (HttpRequestException)
                {
                    missed++; // some species legitimately lack an animated sprite; carry on
                }
            }
            if (id % 50 == 0) Console.WriteLine($"  …{id}");
        }

        Console.WriteLine($"assets: {ok} files, {missed} missing → {paths.Assets} "
            + $"(sprites@{SpritesSha[..7]}, cries@{CriesSha[..7]}, icons@{PokeSpriteSha[..7]})");
    }

    private static int? ParseLimit(string[] rest)
    {
        var arg = rest.FirstOrDefault(a => a.StartsWith("--limit=", StringComparison.Ordinal));
        return arg is not null && int.TryParse(arg["--limit=".Length..], out var n) ? n : null;
    }

    private static Dictionary<int, string> LoadSpeciesSlugs(Paths paths)
    {
        // Prefer the committed data; fall back to the CSV cache if the bake hasn't run yet.
        var speciesJson = Path.Combine(paths.Data, GameDataLoader.Files.Species);
        if (File.Exists(speciesJson))
        {
            var data = GameDataLoader.Load(paths.Data);
            return data.Species.Values.ToDictionary(s => s.Id, s => s.Name.ToLowerInvariant().Replace(' ', '-'));
        }

        var pokemon = CsvTable.Load(paths.Csv("pokemon.csv"));
        var map = new Dictionary<int, string>();
        foreach (var row in pokemon.Rows)
        {
            if (!pokemon.Bool(row, "is_default")) continue;
            var sid = pokemon.Int(row, "species_id");
            if (sid <= DataPin.MaxSpeciesId) map[sid] = pokemon.Str(row, "identifier");
        }
        return map;
    }
}
