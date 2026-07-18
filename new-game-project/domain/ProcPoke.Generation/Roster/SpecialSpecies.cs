namespace ProcPoke.Generation.Roster;

/// <summary>A fossil pickup: the family-root species revived from a fossil, and the area it is found in
/// (always <see cref="DexPlan.FossilAreaId"/>). The fossil family already occupies dex slots (4a-2).</summary>
public sealed record FossilPlacement(int SpeciesId, int AreaId);

/// <summary>A legendary static encounter: its species, the dungeon it waits in, its fixed level, and the
/// dex number appended after the regional dex (DexSize+1..+4).</summary>
public sealed record LegendaryPlacement(int SpeciesId, int AreaId, int Level, int DexNumber);

/// <summary>
/// The optional special species (GDD §5.4/§5.5): two fossil pickups seated at the fossil area, and four
/// legendary static encounters placed in destination dungeons. Legendaries never gate progress.
/// </summary>
public sealed record SpecialSpecies
{
    public required IReadOnlyList<FossilPlacement> Fossils { get; init; }
    public required IReadOnlyList<LegendaryPlacement> Legendaries { get; init; }
}
