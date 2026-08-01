using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;
using ProcPoke.MapGen;
using ProcPoke.Overworld;
using System.Collections.Concurrent;
using System.Text;

// ProcPoke.MapGen — the headless generation harness (DEVELOPMENT-PLAN Phase 2).
// Prints the topology of one or more generated regions as text, and (--png) renders per-area maps plus the
// seam-collapsed world canvas (ticket 10).
//
//   dotnet run --project tools/ProcPoke.MapGen -- [seed] [--badges N] [--count K]
//   dotnet run --project tools/ProcPoke.MapGen -- --smoke [--smoke-seeds 2,42,777]

var seed = ParseULong(Arg(0), 1);
var badges = ParseInt(Flag("--badges"), 8);
var count = ParseInt(Flag("--count"), 1);
var carve = args.Contains("--carve");
var png = args.Contains("--png");

var data = GameDataLoader.Load(FindDataDir());

if (args.Contains("--smoke"))
{
    var smokeSeeds = ParseSeeds(Flag("--smoke-seeds"), [2, 42, 777]);
    var smokeOutput = Flag("--smoke-out") ?? Path.Combine(".cache", "mapgen", "phase3-exit");
    return RunSmoke(smokeSeeds, badges, smokeOutput, data);
}

var packetIndex = Array.IndexOf(args, "--packet");
if (packetIndex >= 0)
{
    var requestedSeeds = packetIndex + 1 < args.Length && !args[packetIndex + 1].StartsWith('-')
        ? ParseInt(args[packetIndex + 1], 10_000)
        : 10_000;
    var packetOutput = Flag("--packet-out") ?? Path.Combine(".cache", "mapgen", "packet");
    return RunPacket(Math.Max(1, requestedSeeds), packetOutput, data);
}

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
            MapImage.SaveWorld(Path.Combine(dir, "_world.png"), region.World);
            Console.WriteLine($"png: wrote {region.Graph.Areas.Count} area images + world canvas → {dir}");
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

static IReadOnlyList<ulong> ParseSeeds(string? value, IReadOnlyList<ulong> fallback)
{
    if (string.IsNullOrWhiteSpace(value)) return fallback;
    var seeds = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(token => ulong.TryParse(token, out var seed) ? (ulong?)seed : null)
        .Where(seed => seed is not null)
        .Select(seed => seed!.Value)
        .Distinct()
        .ToArray();
    return seeds.Length == 0 ? fallback : seeds;
}

int RunSmoke(IReadOnlyList<ulong> smokeSeeds, int badgeCount, string output, GameData smokeData)
{
    Directory.CreateDirectory(output);
    var results = new List<PlayabilitySmokeResult>();

    foreach (var seed in smokeSeeds)
    {
        try
        {
            var region = RegionGenerator.Generate(
                new GenerationSettings { Seed = seed, BadgeCount = badgeCount }, smokeData);
            var result = PlayabilitySmoke.Run(region);
            results.Add(result);
            Console.WriteLine($"smoke: seed {seed} {(result.Passed ? "PASS" : "FAIL")} " +
                $"areas {result.AreasVisited} seamless {result.SeamlessConnections} " +
                $"warp {result.WarpConnections} failures {result.Failures.Count}");
            foreach (var failure in result.Failures) Console.WriteLine($"  {failure}");
        }
        catch (Exception exception)
        {
            var failure = new SmokeFailure(
                "generation", -1, default, Facing.South,
                $"{exception.GetType().Name}: {exception.Message}");
            results.Add(new PlayabilitySmokeResult(seed, -1, -1, 0, 0, 0, false, false, false, false,
                [failure]));
            Console.WriteLine($"smoke: seed {seed} FAIL {failure}");
        }
    }

    var hasSeamless = results.Any(result => result.SeamlessConnections > 0);
    var hasWarp = results.Any(result => result.WarpConnections > 0);
    var mechanicallyPassed = smokeSeeds.Count >= 3 && results.Count == smokeSeeds.Count
        && results.All(result => result.Passed)
        && hasSeamless && hasWarp
        && results.Any(result => result.ExercisedLedge)
        && results.Any(result => result.ExercisedNpc)
        && results.Any(result => result.ExercisedItem)
        && results.Any(result => result.ExercisedGate);

    var report = new StringBuilder()
        .AppendLine("# Phase 3 Overworld exit review")
        .AppendLine()
        .AppendLine($"Generated: {DateTimeOffset.UtcNow:O}")
        .AppendLine($"Settings: badges={badgeCount}; seeds={string.Join(", ", smokeSeeds)}")
        .AppendLine()
        .AppendLine("## Mechanical smoke")
        .AppendLine()
        .AppendLine(mechanicallyPassed
            ? "PASS — every requested seed reached League and exercised the required overworld seams."
            : "FAIL — one or more requested seeds or required seams did not pass.")
        .AppendLine()
        .AppendLine("| Seed | Areas | Seamless | Warp | Ledge | NPC | Item | Gate | Result |")
        .AppendLine("|---:|---:|---:|---:|:---:|:---:|:---:|:---:|:---|");
    foreach (var result in results)
    {
        report.AppendLine($"| {result.Seed} | {result.AreasVisited} | {result.SeamlessConnections} | " +
            $"{result.WarpConnections} | {Mark(result.ExercisedLedge)} | {Mark(result.ExercisedNpc)} | " +
            $"{Mark(result.ExercisedItem)} | {Mark(result.ExercisedGate)} | " +
            $"{(result.Passed ? "PASS" : "FAIL")} |");
    }

    var failures = results.SelectMany(result => result.Failures.Select(failure => (result.Seed, failure)))
        .ToArray();
    report.AppendLine().AppendLine("## Diagnostics").AppendLine();
    if (failures.Length == 0)
        report.AppendLine("No smoke diagnostics.").AppendLine();
    else
    {
        report.AppendLine("~~~text");
        foreach (var (seed, failure) in failures) report.AppendLine($"seed {seed}: {failure}");
        report.AppendLine("~~~").AppendLine();
    }

    report.AppendLine("## Exit review").AppendLine()
        .AppendLine($"Mechanical playability: **{(mechanicallyPassed ? "PASS" : "FAIL")}**.")
        .AppendLine("Human visual review: **PENDING** — inspect the generated region renders for readable " +
            "trainer sightlines, ledge asymmetry, item nooks, and hand-crafted spatial rhythm.")
        .AppendLine()
        .AppendLine("### Remaining gaps").AppendLine()
        .AppendLine("- The smoke packet validates engine-independent movement, transitions, and interactions; it does " +
            "not replace a human run in the Godot editor.")
        .AppendLine("- Final art, battle wiring, and save serialization remain outside Phase 3.")
        .AppendLine();

    var reportPath = Path.Combine(output, "PHASE-3-EXIT-REVIEW.md");
    File.WriteAllText(reportPath, report.ToString());
    Console.WriteLine($"smoke: wrote {reportPath}");
    return mechanicallyPassed ? 0 : 1;
}

static string Mark(bool value) => value ? "yes" : "no";

int RunPacket(int sweepSeeds, string output, GameData packetData)
{
    Directory.CreateDirectory(output);
    var failures = new ConcurrentBag<string>();
    var badges = new[] { 4, 8, 12 };
    var checkedRegions = 0;
    var progressLock = new object();

    Parallel.For(1, sweepSeeds + 1, new ParallelOptions
    {
        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
    }, seedValue =>
    {
        foreach (var badgeCount in badges)
        {
            var seed = (ulong)seedValue;
            try
            {
                var region = RegionGenerator.Generate(
                    new GenerationSettings { Seed = seed, BadgeCount = badgeCount }, packetData);
                var errors = Invariants.CheckAll(region, packetData);
                foreach (var error in errors)
                    failures.Add($"seed {seed}/{badgeCount}: {error}");
            }
            catch (Exception ex)
            {
                failures.Add($"seed {seed}/{badgeCount}: {ex.GetType().Name}: {ex.Message}");
            }
            Interlocked.Increment(ref checkedRegions);
        }
        if (seedValue % 100 == 0)
            lock (progressLock)
                Console.WriteLine($"packet: swept through seed {seedValue:N0}/{sweepSeeds:N0} ({Volatile.Read(ref checkedRegions):N0} regions)");
    });

    var showcaseSeeds = new ulong[] { 2, 7, 42, 99, 123, 500, 777, 1234, 4242, 9001 };
    var showcaseMaps = new List<(Area Area, CarvedArea Carved)>();
    foreach (var seed in showcaseSeeds)
    foreach (var badgeCount in badges)
    {
        var region = RegionGenerator.Generate(
            new GenerationSettings { Seed = seed, BadgeCount = badgeCount }, packetData);
        var dir = Path.Combine(output, "showcase", $"seed-{seed}", $"badges-{badgeCount}");
        Directory.CreateDirectory(dir);
        foreach (var area in region.Graph.Areas)
        {
            var label = area.OnCriticalPath ? $"path-{area.PathIndex:D2}" : $"branch-{area.Id}";
            MapImage.SaveArea(Path.Combine(dir, $"{label}-{area.Archetype}.png"), region.Carved[area.Id]);
            showcaseMaps.Add((area, region.Carved[area.Id]));
        }
        MapImage.SaveWorld(Path.Combine(dir, "_world.png"), region.World);
        MapImage.SaveOverview(Path.Combine(dir, "_overview.png"), region.Graph,
            region.Carved.Values.ToList(), region.Openings);
    }

    var distinct = CarverSignatures.DistinctByArchetype(showcaseMaps);
    var report = new StringBuilder()
        .AppendLine("# Phase 2 sign-off packet")
        .AppendLine()
        .AppendLine($"Generated: {DateTimeOffset.UtcNow:O}")
        .AppendLine($"Sweep: {sweepSeeds:N0} seeds × badges {{4, 8, 12}} ({checkedRegions:N0} regions)")
        .AppendLine($"Invariant failures: {failures.Count}")
        .AppendLine()
        .AppendLine("## Mechanical sweep")
        .AppendLine()
        .AppendLine(failures.Count == 0 ? "PASS — no invariant failures." : "FAIL — see failures below.")
        .AppendLine();
    if (failures.Count > 0)
    {
        report.AppendLine("```text");
        foreach (var failure in failures.OrderBy(f => f).Take(200)) report.AppendLine(failure);
        if (failures.Count > 200) report.AppendLine($"... {failures.Count - 200} more failures omitted");
        report.AppendLine("```").AppendLine();
    }

    report.AppendLine("## Showcase renders").AppendLine()
        .AppendLine("Each showcase directory contains per-area PNGs, `_overview.png`, and the seam-collapsed `_world.png`.")
        .AppendLine();
    report.AppendLine("## Carver distinctness").AppendLine()
        .AppendLine("Computed from shared `CarverSignatures` fingerprints across the showcase corpus.").AppendLine()
        .AppendLine("| Archetype | Distinct signatures |")
        .AppendLine("|---|---|");
    foreach (var pair in distinct.OrderBy(pair => pair.Key))
        report.AppendLine($"| {pair.Key} | {(pair.Value ? "yes" : "no")} |");
    report.AppendLine()
        .AppendLine("## Human visual verdict").AppendLine()
        .AppendLine("PENDING — visual review required for hand-crafted readability, trainer sightlines, ledge asymmetry, and item nooks.")
        .AppendLine("If the review fails, record the gap list and the ADR-0001 decision here.").AppendLine();

    var reportPath = Path.Combine(output, "PACKET.md");
    File.WriteAllText(reportPath, report.ToString());
    Console.WriteLine($"packet: wrote {reportPath}");
    Console.WriteLine($"packet: {checkedRegions:N0} regions, {failures.Count} invariant failures");
    return failures.Count == 0 ? 0 : 1;
}

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
