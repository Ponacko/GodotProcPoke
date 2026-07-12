namespace ProcPoke.Data;

/// <summary>The learnset file's per-species shape: a species id and its B2W2 learnset entries.</summary>
public sealed record SpeciesLearnset
{
    public required int SpeciesId { get; init; }
    public required IReadOnlyList<LearnsetEntry> Entries { get; init; }
}

/// <summary>
/// The whole baked dataset, in memory. Produced by <see cref="GameDataLoader"/> from committed
/// <c>data/</c>. Everything is indexed by the same id scheme the sprites and cries use.
/// </summary>
public sealed class GameData
{
    public required IReadOnlyDictionary<int, PokemonSpecies> Species { get; init; }
    public required IReadOnlyDictionary<int, Move> Moves { get; init; }
    public required IReadOnlyDictionary<int, IReadOnlyList<LearnsetEntry>> Learnsets { get; init; }
    public required TypeChart TypeChart { get; init; }
    public required IReadOnlyList<EvolutionRule> Evolutions { get; init; }
    public required IReadOnlyDictionary<int, Item> Items { get; init; }
    public required IReadOnlyList<Nature> Natures { get; init; }
    public required IReadOnlyDictionary<string, GrowthRate> GrowthRates { get; init; }
    public required IReadOnlyList<string> NameBlocklist { get; init; }
    public required DataManifest Manifest { get; init; }
}
