namespace ProcPoke.Generation.Biomes;

/// <summary>
/// The terrain family of an area. Drives tile art (via the Phase 3 Tile Realizer) and biases encounter
/// tables. Assigned by the biome pass after gates (ADR-0004), honoring the terrain-bound obstacles'
/// Biome Requirements as fixed points.
/// </summary>
public enum Biome
{
    Grassland,
    Forest,
    Cave,
    Mountain,
    Water,
    Desert,
    Urban,
}

/// <summary>The biome assigned to each area. One entry per area in the region.</summary>
public sealed record BiomeMap
{
    public required IReadOnlyDictionary<int, Biome> ByArea { get; init; }

    public Biome Of(int areaId) => ByArea[areaId];
}
