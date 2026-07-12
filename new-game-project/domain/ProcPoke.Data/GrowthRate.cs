namespace ProcPoke.Data;

/// <summary>
/// A growth-rate curve: cumulative experience required to reach each level 1–100.
/// <see cref="CumulativeExperience"/> is indexed by level (index 0 is a placeholder 0; index 1 = level 1 = 0).
/// </summary>
public sealed record GrowthRate
{
    public required string Name { get; init; }

    /// <summary>Length 101; <c>[level]</c> is total experience needed to have reached that level.</summary>
    public required IReadOnlyList<int> CumulativeExperience { get; init; }
}
