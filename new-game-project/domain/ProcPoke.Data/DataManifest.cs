namespace ProcPoke.Data;

/// <summary>
/// Written alongside the baked data. Records the upstream pin and bake-tool version so a drift test can
/// assert the committed <c>data/</c> was produced from the pin currently in <see cref="DataPin"/>.
/// Deliberately holds no timestamp — the bake must be byte-reproducible from pin + tool version alone.
/// </summary>
public sealed record DataManifest
{
    public required string SourceRepo { get; init; }
    public required string SourceCommitSha { get; init; }
    public required string VersionGroup { get; init; }
    public required int VersionGroupId { get; init; }
    public required int GenerationId { get; init; }
    public required int BakeToolVersion { get; init; }
    public required int SpeciesCount { get; init; }

    /// <summary>Builds the manifest that the current pin implies, for writing or comparison.</summary>
    public static DataManifest FromPin(int speciesCount) => new()
    {
        SourceRepo = DataPin.SourceRepo,
        SourceCommitSha = DataPin.SourceCommitSha,
        VersionGroup = DataPin.VersionGroup,
        VersionGroupId = DataPin.VersionGroupId,
        GenerationId = DataPin.GenerationId,
        BakeToolVersion = DataPin.BakeToolVersion,
        SpeciesCount = speciesCount,
    };
}
