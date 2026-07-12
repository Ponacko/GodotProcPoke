namespace ProcPoke.Data;

/// <summary>
/// The Gen 5 single-type effectiveness matrix, as integer percents (0, 50, 100, 200). Indexed by
/// <c>(int)PokeType</c> for both axes. Gen 5 specifics are baked in via <c>type_efficacy_past</c> — most
/// notably Ghost and Dark are ½× against Steel (that resistance was removed in Gen 6).
/// </summary>
public sealed record TypeChart
{
    /// <summary>Row = attacking type, column = defending type; value is a percent multiplier.</summary>
    public required IReadOnlyList<IReadOnlyList<int>> Percents { get; init; }

    /// <summary>Effectiveness of <paramref name="attacker"/> against a single defending type, as a multiplier.</summary>
    public double Effectiveness(PokeType attacker, PokeType defender)
        => Percents[(int)attacker][(int)defender] / 100.0;
}
