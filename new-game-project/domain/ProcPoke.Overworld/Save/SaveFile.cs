using ProcPoke.Battle;
using ProcPoke.Generation;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Bosses;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Encounters;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Identity;
using ProcPoke.Generation.Npcs;
using ProcPoke.Generation.Rivals;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Services;
using ProcPoke.Generation.Topology;
using ProcPoke.Generation.Trainers;

namespace ProcPoke.Overworld.Save;

/// <summary>
/// A whole generated region, as stored. Deliberately the fully generated article rather than a seed
/// (GDD §11): determinism is promised per generator version only, so a save that stored a Seed String and
/// regenerated on load would quietly turn into a different region the first time the generator changed.
///
/// The two members of <see cref="GeneratedRegion"/> that are pure functions of the rest are not stored —
/// the opening plan and the world canvas are recomputed by <see cref="ToRegion"/>, which keeps the biggest
/// derived structure in the game out of every save file.
/// </summary>
public sealed record RegionSnapshot
{
    public required RegionGraph Graph { get; init; }
    public required GatingPlan Gating { get; init; }
    public required BiomeMap Biomes { get; init; }
    public required IReadOnlyDictionary<int, CarvedArea> Carved { get; init; }
    public required GenerationSettings Settings { get; init; }
    public required RegionNames Names { get; init; }
    public required RegionIdentity Identity { get; init; }
    public required StarterPlan Starters { get; init; }
    public required DexPlan Dex { get; init; }
    public required SpecialSpecies Special { get; init; }
    public required EncounterPlan Encounters { get; init; }
    public required TrainerPlan Trainers { get; init; }
    public required BossPlan Bosses { get; init; }
    public required RivalPlan Rivals { get; init; }
    public required NpcPlan Npcs { get; init; }
    public required ServicePlan Services { get; init; }

    public static RegionSnapshot From(GeneratedRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return new RegionSnapshot
        {
            Graph = region.Graph,
            Gating = region.Gating,
            Biomes = region.Biomes,
            Carved = region.Carved,
            Settings = region.Settings,
            Names = region.Names,
            Identity = region.Identity,
            Starters = region.Starters,
            Dex = region.Dex,
            Special = region.Special,
            Encounters = region.Encounters,
            Trainers = region.Trainers,
            Bosses = region.Bosses,
            Rivals = region.Rivals,
            Npcs = region.Npcs,
            Services = region.Services,
        };
    }

    public GeneratedRegion ToRegion()
    {
        var openings = OpeningAligner.Plan(Graph);
        var world = WorldCanvas.Compose(Graph, Biomes, Carved, openings);
        return new GeneratedRegion(
            Graph, Gating, Biomes, openings, Carved, Settings, Names, Identity, Starters, Dex, Special,
            Encounters, Trainers, Bosses, Rivals, Npcs, Services, world);
    }
}

/// <summary>The interaction state a save carries: what has been picked up, opened, and learned.</summary>
public sealed record InteractionSnapshot
{
    public required IReadOnlyList<string> CollectedItemIds { get; init; }
    public required IReadOnlyList<int> UnlockedGateIds { get; init; }
    public required IReadOnlyList<string> Keys { get; init; }
    public required IReadOnlyDictionary<int, int> Bag { get; init; }
    public bool DebugUnlock { get; init; }
}

/// <summary>Where the player is standing, so a save resumes exactly there (save-anywhere, GDD §11).</summary>
public sealed record LocationSnapshot
{
    public required int AreaId { get; init; }
    public required PlayerState Player { get; init; }

    /// <summary>The building the player is inside, or null out on the map. Stored as the door's coordinates
    /// because that is what identifies a building entrance; the interior itself is a fixed template.</summary>
    public (int X, int Y)? InsideBuildingDoor { get; init; }
}

/// <summary>
/// Everything the player has done. The generated region says what the world is; this says what has
/// happened to it.
/// </summary>
public sealed record SessionSnapshot
{
    public required PlayerIdentity Player { get; init; }

    /// <summary>Which generator built this region. Set when the region was generated and carried through
    /// every later save, so a re-save after a version bump does not relabel where the region came from.
    /// Older saves, written before this field existed, read as null and fall back to the file's stamp.</summary>
    public int? RegionGeneratorVersion { get; init; }
    public required LocationSnapshot Location { get; init; }
    public required int LastPokemonCenterAreaId { get; init; }
    public required IReadOnlyList<int> VisitedAreaIds { get; init; }
    public required int Badges { get; init; }
    public required int Money { get; init; }
    public required int MinutesSinceMidnight { get; init; }
    public bool HasFly { get; init; }
    public int? StarterCornerIndex { get; init; }
    public required IReadOnlyList<RivalBeat> DefeatedRivalBeats { get; init; }
    public required IReadOnlyList<string> DefeatedTrainerIds { get; init; }
    public required IReadOnlyList<int> DefeatedGymAreaIds { get; init; }
    public required IReadOnlyList<int> DefeatedLeagueStages { get; init; }
    public required IReadOnlyList<int> LeagueRunStages { get; init; }
    public bool CreditsReached { get; init; }
    public required IReadOnlyList<int> CapturedLegendaries { get; init; }
    public required IReadOnlyList<int> DormantLegendaries { get; init; }
    public required IReadOnlyList<int> CollectedFossils { get; init; }
    public required IReadOnlyList<FossilPickup> PendingFossilRevivals { get; init; }
    public required IReadOnlyList<BattleProgressionState> Party { get; init; }
    public required IReadOnlyList<BattleProgressionState> Stored { get; init; }
    public required IReadOnlyList<int> PokedexSeen { get; init; }
    public required IReadOnlyList<int> PokedexCaught { get; init; }
    public required InteractionSnapshot Interactions { get; init; }
}

/// <summary>
/// One save slot's contents. <see cref="SecondRegion"/> and <see cref="LevelCapBadges"/> are the Phase 7
/// provisions the development plan reserves (GDD §15): a second region and its per-badge level cap have a
/// place in the schema from the start, so region 1 never has to be regenerated to make room for them.
/// </summary>
public sealed record SaveFile
{
    /// <summary>Bumped only when this schema changes shape — distinct from
    /// <see cref="GeneratorVersion"/>, which says which generator built the region inside.</summary>
    public const int CurrentFormatVersion = 1;

    public required int FormatVersion { get; init; }

    /// <summary>The generator that built <see cref="Region"/> — not necessarily the one running now, since
    /// a save keeps the fully generated region and outlives generator bumps (GDD §11).</summary>
    public required int GeneratorVersion { get; init; }
    public required string SeedString { get; init; }

    /// <summary>Wall-clock stamp, supplied by the caller — the domain never reads the clock itself.</summary>
    public required DateTimeOffset SavedAt { get; init; }

    public required RegionSnapshot Region { get; init; }
    public required SessionSnapshot Session { get; init; }

    /// <summary>Reserved for Phase 7: region 2's own generated region, once it exists.</summary>
    public RegionSnapshot? SecondRegion { get; init; }

    /// <summary>Reserved for Phase 7: region 2's current per-badge Level Cap (CONTEXT.md), null in
    /// region 1 where no cap applies.</summary>
    public int? LevelCapBadges { get; init; }
}

/// <summary>What a slot listing shows without loading the whole save.</summary>
public sealed record SaveSlotSummary(
    int Slot,
    string PlayerName,
    int Badges,
    int PartyCount,
    int PokedexCaught,
    bool CreditsReached,
    string SeedString,
    int GeneratorVersion,
    DateTimeOffset SavedAt);
