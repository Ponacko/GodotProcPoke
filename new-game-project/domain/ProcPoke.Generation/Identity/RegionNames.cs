namespace ProcPoke.Generation.Identity;

/// <summary>Per-seed location names (GDD §8): every city and dungeon gets a generated name; routes keep
/// <c>Route N</c> in critical-path order.</summary>
public sealed record RegionNames
{
    public required NamingMotif Motif { get; init; }
    public required IReadOnlyDictionary<int, string> ByArea { get; init; }

    public string Of(int areaId) => ByArea[areaId];
}
