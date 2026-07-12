namespace ProcPoke.Data;

/// <summary>
/// A bag item. The baked subset covers what MVP mechanics reference: balls, medicine, machines
/// (TMs/HMs), evolution items, and every held item any baked evolution needs.
/// </summary>
public sealed record Item
{
    public required int Id { get; init; }
    public required string Name { get; init; }

    /// <summary>veekun category identifier (e.g. "standard-balls", "evolution", "healing").</summary>
    public required string Category { get; init; }

    public required int Cost { get; init; }
    public int? FlingPower { get; init; }
}
