namespace ProcPoke.Data;

/// <summary>
/// A value for each of the six stats. Used for base stats and EV yields.
/// </summary>
public readonly record struct StatBlock(
    int Hp,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed)
{
    public int this[Stat stat] => stat switch
    {
        Stat.Hp => Hp,
        Stat.Attack => Attack,
        Stat.Defense => Defense,
        Stat.SpecialAttack => SpecialAttack,
        Stat.SpecialDefense => SpecialDefense,
        Stat.Speed => Speed,
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };
}
