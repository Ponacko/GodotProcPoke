using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;
using ProcPoke.MapGen;

// ProcPoke.MapGen — the headless generation harness (DEVELOPMENT-PLAN Phase 2).
// Prints the topology of one or more generated regions as text, and (--png) renders per-area maps plus a
// spatially stitched region overview (ticket 2b).
//
//   dotnet run --project tools/ProcPoke.MapGen -- [seed] [--badges N] [--count K]

var seed = ParseULong(Arg(0), 1);
var badges = ParseInt(Flag("--badges"), 8);
var count = ParseInt(Flag("--count"), 1);
var carve = args.Contains("--carve");
var png = args.Contains("--png");

var data = GameDataLoader.Load(FindDataDir());

for (var i = 0; i < count; i++)
{
    var thisSeed = seed + (ulong)i;
    var settings = new GenerationSettings { Seed = thisSeed, BadgeCount = badges };
    var region = RegionGenerator.Generate(settings, data);

    Console.WriteLine($"════ seed {thisSeed} ════");
    Console.WriteLine(RegionGraphText.Render(region.Graph, region.Gating, region.Biomes,
        region.Names, region.Identity, region.Starters, region.Dex, data, region.Special, region.Encounters,
        region.Trainers, region.Bosses, region.Rivals, region.Npcs));

    if (carve || png)
    {
        // Maps are carved once inside the pipeline (ticket 2c) — the harness reads region.Carved.
        var carvedById = region.Carved;

        if (carve)
        {
            // Show critical-path areas as ASCII — the first few by default, or just one with --area N.
            var only = ParseInt(Flag("--area"), -1);
            var picked = only >= 0
                ? region.Graph.Areas.Where(a => a.Id == only)
                : region.Graph.CriticalPath.Take(4);
            foreach (var a in picked)
            {
                var carved = carvedById[a.Id];
                Console.WriteLine($"── [{a.PathIndex}] {a.Archetype}  {a.Size}  {region.Biomes.Of(a.Id)}  ({carved.Grid.Width}×{carved.Grid.Height}) ──");
                Console.WriteLine(AsciiRenderer.Render(carved));
            }
        }

        if (png)
        {
            var dir = Path.Combine(".cache", "mapgen", $"seed-{thisSeed}");
            Directory.CreateDirectory(dir);
            foreach (var a in region.Graph.CriticalPath)
                MapImage.SaveArea(Path.Combine(dir, $"{a.PathIndex:D2}-{a.Archetype}.png"), carvedById[a.Id]);
            foreach (var a in region.Graph.OffSpineAreas)
                MapImage.SaveArea(Path.Combine(dir, $"branch-{a.Id}-{a.Archetype}.png"), carvedById[a.Id]);
            MapImage.SaveOverview(Path.Combine(dir, "_overview.png"), region.Graph, carvedById.Values.ToList(), region.Openings);
            Console.WriteLine($"png: wrote {region.Graph.Areas.Count} area images + overview → {dir}");
        }
    }
}

return 0;

string? Arg(int index)
{
    var positional = args.Where(a => !a.StartsWith('-')).ToArray();
    return index < positional.Length ? positional[index] : null;
}

string? Flag(string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static ulong ParseULong(string? s, ulong fallback) => ulong.TryParse(s, out var v) ? v : fallback;
static int ParseInt(string? s, int fallback) => int.TryParse(s, out var v) ? v : fallback;

static string FindDataDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ProcPoke.slnx")))
            return Path.Combine(dir.FullName, "data");
        dir = dir.Parent;
    }
    throw new InvalidOperationException("could not locate repo root (ProcPoke.slnx) from the MapGen assembly.");
}
