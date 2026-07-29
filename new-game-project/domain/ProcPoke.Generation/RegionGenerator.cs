using ProcPoke.Data;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Bosses;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Encounters;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Identity;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Rivals;
using ProcPoke.Generation.Npcs;
using ProcPoke.Generation.Topology;
using ProcPoke.Generation.Trainers;

namespace ProcPoke.Generation;

/// <summary>A generated region so far: graph, gating, biomes, the edge-aligned opening plan carvers
/// consume, the carved tile map per area (keyed by area id), names, gym/E4/Champion typing, and the
/// starter triangle. Grows as later passes (dex, …) land.</summary>
public sealed record GeneratedRegion(
    RegionGraph Graph, GatingPlan Gating, BiomeMap Biomes, OpeningPlan Openings,
    IReadOnlyDictionary<int, CarvedArea> Carved, GenerationSettings Settings,
    RegionNames Names, RegionIdentity Identity, StarterPlan Starters, DexPlan Dex, SpecialSpecies Special,
    EncounterPlan Encounters, TrainerPlan Trainers, BossPlan Bosses, RivalPlan Rivals, NpcPlan Npcs)
{
    public RivalPlan Rival => Rivals;
}

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
            identity = IdentityPass.Generate(identity, names, streams);
            var starters = StarterSelector.Generate(data, settings, streams);
            var dex = DexSelector.Generate(graph, biomes, starters, data, settings, streams);
            var special = SpecialSpeciesPass.Generate(graph, dex, data, settings, streams);
            var encounters = EncounterPass.Generate(graph, biomes, gating, dex, identity, settings, data, streams);
            // Carving is the last pass (ADR-0004: puzzle before terrain) — the region owns its carved maps
            // so later population passes can read TrainerPost/ItemBall tile counts. Each area draws only
            // from its own "carve/<id>" stream (ADR-0005), so output is independent of pass order.
            var carved = graph.Areas.ToDictionary(a => a.Id, a => AreaCarver.Carve(
                a, biomes.Of(a.Id), streams.Stream("carve", a.Id), openings,
                AreaCarver.GateOnExitOf(a, gating), GateGeometry.SpineExitSideOf(graph, a)));
            var trainers = TrainerPass.Generate(graph, carved, biomes, identity, dex, data, streams);
            var bosses = BossPass.Generate(graph, identity, dex, starters, settings, data, streams);
            var rivals = RivalPass.Generate(identity, dex, starters, settings, data, streams);
            var npcs = NpcPass.Generate(graph, gating, names, identity, streams);
            return new GeneratedRegion(
                graph, gating, biomes, openings, carved, settings, names, identity, starters, dex, special,
                encounters, trainers, bosses, rivals, npcs);
        }

        throw new InvalidOperationException(
            $"generation could not produce a solvable region for seed {settings.Seed} (generator bug).");
    }

    /// <summary>Attempt 0 uses the seed unchanged (the deterministic normal path); later attempts perturb it.</summary>
    private static ulong SubSeed(ulong seed, int attempt)
        => attempt == 0 ? seed : unchecked(seed * 6364136223846793005UL + (ulong)attempt * 1442695040888963407UL);
}
