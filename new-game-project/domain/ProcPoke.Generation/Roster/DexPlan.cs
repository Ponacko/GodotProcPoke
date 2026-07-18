namespace ProcPoke.Generation.Roster;

/// <summary>One regional-dex slot: its number (1-based, up to DexSize; legendaries append DexSize+1..+4 in
/// 4c) and the national species id that fills it.</summary>
public sealed record DexEntry(int Number, int SpeciesId);

/// <summary>
/// The regional dex (GDD §5.1/§5.2): <see cref="GenerationSettings.DexSize"/> species (fewer only when the
/// roster cap can't supply that many) numbered in first-availability order, with #1–9 the three starter
/// lines. Evolution families are kept whole and consecutive. The four legendaries are appended later (4c).
/// </summary>
public sealed record DexPlan
{
    /// <summary>Every dex slot, ascending by <see cref="DexEntry.Number"/> from 1 to DexSize.</summary>
    public required IReadOnlyList<DexEntry> Entries { get; init; }

    /// <summary>The area whose slot holds the two fossil families; 4c places the fossil pickups here.</summary>
    public required int FossilAreaId { get; init; }

    /// <summary>Every member species of the two reserved fossil families (family order, members in family
    /// order) — the pool 6b excludes from wild tables and 4c reconstructs the two families from.</summary>
    public required IReadOnlyList<int> FossilFamilySpecies { get; init; }

    /// <summary>areaId → the species first available there (in dex order), including the start town's
    /// starter lines. Consumed by the encounter (6) and trainer (7a) passes.</summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<int>> SpeciesByArea { get; init; }
}
