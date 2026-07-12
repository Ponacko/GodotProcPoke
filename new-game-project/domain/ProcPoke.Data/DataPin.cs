namespace ProcPoke.Data;

/// <summary>
/// The single source of truth for what upstream snapshot the committed <c>data/</c> was baked from.
/// Both the bake tool (writer) and the drift test (reader) reference these constants, so a stale or
/// re-pinned bake cannot slip through unnoticed: <c>manifest.json</c> must echo these values exactly.
/// </summary>
public static class DataPin
{
    /// <summary>Upstream repository the CSV dump is fetched from.</summary>
    public const string SourceRepo = "PokeAPI/pokeapi";

    /// <summary>Pinned commit the CSVs are downloaded at — determinism starts here.</summary>
    public const string SourceCommitSha = "d638fe7791214a8d3c3282e2a3113eea7cfef288";

    /// <summary>The version group every value is snapshotted to (Gen 5, Black 2 / White 2).</summary>
    public const string VersionGroup = "black-2-white-2";

    /// <summary>veekun's numeric id for <see cref="VersionGroup"/>.</summary>
    public const int VersionGroupId = 14;

    /// <summary>Sort order of <see cref="VersionGroupId"/> — used to resolve historical changelogs.</summary>
    public const int VersionGroupOrder = 16;

    /// <summary>The generation whose mechanics the snapshot targets.</summary>
    public const int GenerationId = 5;

    /// <summary>Highest species id kept (Gen 1–5: Bulbasaur … Genesect).</summary>
    public const int MaxSpeciesId = 649;

    /// <summary>
    /// Bumped whenever the bake transforms change in a way that alters output. Committed data whose
    /// manifest reports a different version is stale and must be re-baked.
    /// </summary>
    public const int BakeToolVersion = 1;
}
