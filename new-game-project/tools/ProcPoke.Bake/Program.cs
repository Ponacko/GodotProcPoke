using ProcPoke.Bake;
using ProcPoke.Data;

// ProcPoke.Bake — the single data-pipeline tool (GDD §10, DEVELOPMENT-PLAN Phase 1).
//
//   fetch    download the pinned veekun CSVs into the gitignored cache
//   bake     transform the cached CSVs into committed data/*.json, snapshotted to B2W2
//   assets   download the pinned sprite / icon / cry sets (kept out of git)
//
// The pin lives in ProcPoke.Data.DataPin, shared with the drift test.

var cmd = args.Length > 0 ? args[0] : "";
var rest = args.Skip(1).ToArray();
var force = rest.Contains("--force");

var root = FindRepoRoot();
var paths = new Paths(root);

try
{
    switch (cmd)
    {
        case "fetch":
            await Fetch.RunAsync(paths, force);
            break;
        case "bake":
            Baker.Run(paths);
            break;
        case "assets":
            await Assets.RunAsync(paths, rest, force);
            break;
        default:
            Console.Error.WriteLine(
                "usage: ProcPoke.Bake <fetch|bake|assets> [--force]\n" +
                $"  source pin: {DataPin.SourceRepo}@{DataPin.SourceCommitSha[..12]} ({DataPin.VersionGroup})");
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ProcPoke.slnx")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new InvalidOperationException("could not locate repo root (ProcPoke.slnx not found above cwd).");
}

/// <summary>Well-known directories the tool reads and writes, all relative to the repo root.</summary>
internal sealed class Paths(string root)
{
    public string Root { get; } = root;

    /// <summary>Gitignored CSV download cache.</summary>
    public string CsvCache { get; } = Path.Combine(root, ".cache", "pokeapi-csv");

    /// <summary>Committed baked data.</summary>
    public string Data { get; } = Path.Combine(root, "data");

    /// <summary>Gitignored asset download tree.</summary>
    public string Assets { get; } = Path.Combine(root, ".cache", "assets");

    public string Csv(string file) => Path.Combine(CsvCache, file);
}
