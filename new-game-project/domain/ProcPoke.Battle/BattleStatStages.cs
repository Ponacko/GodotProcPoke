using ProcPoke.Data;

namespace ProcPoke.Battle;

/// <summary>
/// Per-combatant Gen 5 temporary stat stages. They reset when the combatant leaves battle and
/// are kept separately from permanent stats so the immutable state makes every turn replayable.
/// </summary>
public sealed record BattleStatStages
{
    public int Attack { get; init; }
    public int Defense { get; init; }
    public int SpecialAttack { get; init; }
    public int SpecialDefense { get; init; }
    public int Speed { get; init; }
    public int Accuracy { get; init; }
    public int Evasion { get; init; }

    public int Of(BattleStat stat) => stat switch
    {
        BattleStat.Attack => Attack,
        BattleStat.Defense => Defense,
        BattleStat.SpecialAttack => SpecialAttack,
        BattleStat.SpecialDefense => SpecialDefense,
        BattleStat.Speed => Speed,
        BattleStat.Accuracy => Accuracy,
        BattleStat.Evasion => Evasion,
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
    };

    public BattleStatStages With(BattleStat stat, int stage) => stat switch
    {
        BattleStat.Attack => this with { Attack = stage },
        BattleStat.Defense => this with { Defense = stage },
        BattleStat.SpecialAttack => this with { SpecialAttack = stage },
        BattleStat.SpecialDefense => this with { SpecialDefense = stage },
        BattleStat.Speed => this with { Speed = stage },
        BattleStat.Accuracy => this with { Accuracy = stage },
        BattleStat.Evasion => this with { Evasion = stage },
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
    };
}
