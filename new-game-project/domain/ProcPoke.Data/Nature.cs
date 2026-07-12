namespace ProcPoke.Data;

/// <summary>
/// A nature. Neutral natures (Hardy, Docile, …) leave both stat fields null; the rest raise one stat
/// 10% and lower another 10%.
/// </summary>
public sealed record Nature
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public Stat? IncreasedStat { get; init; }
    public Stat? DecreasedStat { get; init; }
}
