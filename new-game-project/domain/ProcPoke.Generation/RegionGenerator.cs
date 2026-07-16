using ProcPoke.Data;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Identity;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation;

/// <summary>A generated region so far: graph, gating, biomes, the edge-aligned opening plan carvers
/// consume, names, gym/E4/Champion typing, and the starter triangle. Grows as later passes (dex, …)
/// land.</summary>
public sealed record GeneratedRegion(
    RegionGraph Graph, GatingPlan Gating, BiomeMap Biomes, OpeningPlan Openings, GenerationSettings Settings,
    RegionNames Names, RegionIdentity Identity, StarterPlan Starters);

/// <summary>
/// Runs the generation pipeline in ADR-0004 order and enforces ADR-0002: construction guarantees a
/// solvable region, but if the independent validator ever disagrees (a generator bug), it silently
/// re-rolls an internal sub-seed rather than ever handing the player a broken region.
/// </summary>
public static class RegionGenerator
{
    private const int MaxAttempts = 8;

    public static GeneratedRegion Generate(GenerationSettings settings, GameData data)
    {
        settings.Validate();

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var streams = new RngStreams(SubSeed(settings.Seed, attempt));
            var graph = TopologyGenerator.Generate(settings, streams);
            var gating = GatingGenerator.Generate(graph, settings, streams);

            if (!GatingValidator.IsSolvable(graph, gating))
                continue;

            var biomes = BiomePass.Generate(graph, gating, streams);
            var openings = OpeningAligner.Plan(graph);
            var names = NamingPass.Generate(graph, data.NameBlocklist, streams);
            var identity = GymTypingPass.Generate(graph, biomes, streams);
            var starters = StarterSelector.Generate(data, settings, streams);
            return new GeneratedRegion(graph, gating, biomes, openings, settings, names, identity, starters);
        }

        throw new InvalidOperationException(
            $"generation could not produce a solvable region for seed {settings.Seed} (generator bug).");
    }

    /// <summary>Attempt 0 uses the seed unchanged (the deterministic normal path); later attempts perturb it.</summary>
    private static ulong SubSeed(ulong seed, int attempt)
        => attempt == 0 ? seed : unchecked(seed * 6364136223846793005UL + (ulong)attempt * 1442695040888963407UL);
}
