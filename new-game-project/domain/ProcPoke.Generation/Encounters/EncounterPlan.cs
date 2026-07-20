namespace ProcPoke.Generation.Encounters;

public enum EncounterMethod { Land, Surf, Fishing }

/// <summary>One slot in a Gen 5 style table. Percent is the fixed slot weight; the species and level band
/// are filled by ticket 6b (empty/zeroed until then).</summary>
public sealed record EncounterSlot(int Percent, int SpeciesId, int MinLevel, int MaxLevel);

/// <summary>One method's table for an area. HiddenAbilityChance is 0 for base tables; only a Special
/// Encounter Overlay (6b) sets it (GDD §6.3 / §3 — overlays are the sole hidden-ability source).</summary>
public sealed record EncounterTable(
    EncounterMethod Method,
    IReadOnlyList<EncounterSlot> Slots,
    double HiddenAbilityChance = 0);

/// <summary>Every encounter table for one area, its base wild level (6a; 6b bands each slot around it), and
/// its optional rare-spawn overlay (6b).</summary>
public sealed record AreaEncounters(int AreaId, IReadOnlyList<EncounterTable> Tables, EncounterTable? Overlay, int BaseLevel);

public sealed record EncounterPlan
{
    public required IReadOnlyDictionary<int, AreaEncounters> ByArea { get; init; }
    public AreaEncounters? Of(int areaId) => ByArea.TryGetValue(areaId, out var e) ? e : null;
}

/// <summary>Canonical Gen 5 slot layouts (GDD §6.3). Percentages sum to 100.</summary>
public static class SlotLayouts
{
    public static readonly IReadOnlyList<int> Land    = [20, 20, 10, 10, 10, 10, 5, 5, 4, 4, 1, 1];
    public static readonly IReadOnlyList<int> Surf    = [60, 30, 5, 4, 1];
    public static readonly IReadOnlyList<int> Fishing = [60, 30, 5, 4, 1]; // B2W2 Super Rod only; per-tier rods deferred
}
