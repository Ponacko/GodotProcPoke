using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;
using ProcPoke.MapGen;

// ProcPoke.MapGen — the headless generation harness (DEVELOPMENT-PLAN Phase 2).
// Prints the topology of one or more generated regions as text. The PNG map renderer arrives in 2b.
//
//   dotnet run --project tools/ProcPoke.MapGen -- [seed] [--badges N] [--count K]

var seed = ParseULong(Arg(0), 1);
var badges = ParseInt(Flag("--badges"), 8);
var count = ParseInt(Flag("--count"), 1);
var carve = args.Contains("--carve");
var png = args.Contains("--png");

for (var i = 0; i < count; i++)
{
    var thisSeed = seed + (ulong)i;
    var settings = new GenerationSettings { Seed = thisSeed, BadgeCount = badges };
    var region = RegionGenerator.Generate(settings);

    Console.WriteLine($"════ seed {thisSeed} ════");
    Console.WriteLine(RegionGraphText.Render(region.Graph, region.Gating, region.Biomes));

    if (carve || png)
    {
        var streams = new RngStreams(thisSeed);
        CarvedArea CarveArea(Area a) => AreaCarver.Carve(a, region.Biomes.Of(a.Id), streams.Stream("carve", a.Id),
            AreaCarver.GateOnExitOf(a, region.Gating));

        if (carve)
        {
            // Show the first few critical-path areas as ASCII.
            foreach (var a in region.Graph.CriticalPath.Take(4))
            {
                var carved = CarveArea(a);
                Console.WriteLine($"── [{a.PathIndex}] {a.Archetype}  {a.Size}  {region.Biomes.Of(a.Id)}  ({carved.Grid.Width}×{carved.Grid.Height}) ──");
                Console.WriteLine(AsciiRenderer.Render(carved));
            }
        }

        if (png)
        {
            var dir = Path.Combine(".cache", "mapgen", $"seed-{thisSeed}");
            Directory.CreateDirectory(dir);
            var carvedPath = region.Graph.CriticalPath.Select(CarveArea).ToList();
            foreach (var (a, c) in region.Graph.CriticalPath.Zip(carvedPath))
                MapImage.SaveArea(Path.Combine(dir, $"{a.PathIndex:D2}-{a.Archetype}.png"), c);
            foreach (var a in region.Graph.OffSpineAreas)
                MapImage.SaveArea(Path.Combine(dir, $"branch-{a.Id}-{a.Archetype}.png"), CarveArea(a));
            MapImage.SaveOverview(Path.Combine(dir, "_overview.png"), carvedPath);
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
