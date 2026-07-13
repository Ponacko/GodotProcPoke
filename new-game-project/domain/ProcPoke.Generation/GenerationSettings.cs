namespace ProcPoke.Generation;

/// <summary>
/// The player-set knobs from the Generation Screen (GDD §4.1). Together with
/// <see cref="GeneratorVersion"/> these form the Seed String; the same settings + version always
/// reproduce the same region.
/// </summary>
public sealed record GenerationSettings
{
    /// <summary>Master seed for all randomness.</summary>
    public required ulong Seed { get; init; }

    /// <summary>Regional dex size — number of available Pokémon (default 150; +4 starters/fossils added later).</summary>
    public int DexSize { get; init; } = 150;

    /// <summary>Highest generation species may be drawn from (Roster Cap, 1–5; default 5).</summary>
    public int RosterCap { get; init; } = 5;

    /// <summary>Number of gym cities / badges (default 8).</summary>
    public int BadgeCount { get; init; } = 8;

    public GenerationSettings Validate()
    {
        if (BadgeCount is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(BadgeCount));
        if (RosterCap is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(RosterCap));
        if (DexSize < 16) throw new ArgumentOutOfRangeException(nameof(DexSize));
        return this;
    }
}
