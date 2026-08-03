using ProcPoke.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace ProcPoke.Bake;

/// <summary>
/// The <c>assets</c> subcommand: downloads the Gen 5 animated battle sprites (front and, where available,
/// back), menu icons, and cries that share the species id scheme with the baked data (GDD §10). Each
/// downloaded battle GIF is also split into a numbered PNG-per-frame sequence plus a <c>frames.json</c>
/// delay manifest, so Godot (which has no native GIF import) only ever has to load PNGs. Assets stay out
/// of git — this tool plus the pinned SHAs below are the reproducibility story. Defaults to a small
/// sample; pass <c>--all</c> for 1–649.
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

            (ok, missed) = await FetchBattleGif(http,
                $"https://raw.githubusercontent.com/{SpritesRepo}/{SpritesSha}/sprites/pokemon/versions/generation-v/black-white/animated/{id}.gif",
                Path.Combine(spritesDir, $"{id}.gif"), Path.Combine(spritesDir, $"{id}"), force, ok, missed);
            (ok, missed) = await FetchBattleGif(http,
                $"https://raw.githubusercontent.com/{SpritesRepo}/{SpritesSha}/sprites/pokemon/versions/generation-v/black-white/animated/back/{id}.gif",
                Path.Combine(spritesDir, $"{id}-back.gif"), Path.Combine(spritesDir, $"{id}-back"), force, ok, missed);

            var plainJobs = new (string Url, string Dest)[]
            {
                ($"https://raw.githubusercontent.com/{CriesRepo}/{CriesSha}/cries/pokemon/legacy/{id}.ogg",
                    Path.Combine(criesDir, $"{id}.ogg")),
                (slug is null ? "" :
                    $"https://raw.githubusercontent.com/{PokeSpriteRepo}/{PokeSpriteSha}/pokemon-gen8/regular/{slug}.png",
                    Path.Combine(iconsDir, $"{id}.png")),
            };
            foreach (var (url, dest) in plainJobs)
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
                    missed++; // some species legitimately lack a cry/icon; carry on
                }
            }
            if (id % 50 == 0) Console.WriteLine($"  …{id}");
        }

        Console.WriteLine($"assets: {ok} files, {missed} missing → {paths.Assets} "
            + $"(sprites@{SpritesSha[..7]}, cries@{CriesSha[..7]}, icons@{PokeSpriteSha[..7]})");
    }

    /// <summary>Downloads one battle GIF (front or back; back legitimately doesn't exist for many species)
    /// and, on success, splits it into <paramref name="frameDir"/>/frame_NN.png + frames.json so Godot never
    /// has to decode a GIF itself.</summary>
    private static async Task<(int ok, int missed)> FetchBattleGif(
        HttpClient http, string url, string gifDest, string frameDir, bool force, int ok, int missed)
    {
        var framesJson = Path.Combine(frameDir, "frames.json");
        if (!force && File.Exists(gifDest) && File.Exists(framesJson)) return (ok + 1, missed);

        byte[] bytes;
        try
        {
            bytes = await http.GetByteArrayAsync(url);
        }
        catch (HttpRequestException)
        {
            return (ok, missed + 1); // e.g. no back sprite exists for this species
        }

        await File.WriteAllBytesAsync(gifDest, bytes);
        try
        {
            SplitGifFrames(bytes, frameDir);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (ok, missed + 1); // malformed/unexpected GIF; the raw file is still saved, frames are not
        }
        return (ok + 1, missed);
    }

    private static void SplitGifFrames(byte[] gifBytes, string frameDir)
    {
        Directory.CreateDirectory(frameDir);
        using var image = Image.Load<Rgba32>(gifBytes);
        var delaysMs = new int[image.Frames.Count];
        for (var i = 0; i < image.Frames.Count; i++)
        {
            using var frame = image.Frames.CloneFrame(i);
            frame.SaveAsPng(Path.Combine(frameDir, $"frame_{i:00}.png"));
            var gifMeta = image.Frames[i].Metadata.GetGifMetadata();
            delaysMs[i] = gifMeta.FrameDelay * 10; // GIF delay units are centiseconds
        }

        var manifest = $$"""
            {"frameCount": {{image.Frames.Count}}, "delaysMs": [{{string.Join(", ", delaysMs)}}]}
            """;
        File.WriteAllText(Path.Combine(frameDir, "frames.json"), manifest);
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
