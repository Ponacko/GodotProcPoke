namespace ProcPoke.Data;

/// <summary>The physical/special/status split (Gen 4+, so Gen 5 uses it fully).</summary>
public enum DamageClass
{
    Status,
    Physical,
    Special,
}

/// <summary>A stat change a move applies, in stages.</summary>
public readonly record struct MoveStatChange(Stat Stat, int Stages);

/// <summary>
/// The structured effect metadata veekun records for a move (hit counts, drain, ailment chance, …).
/// The battle engine's effect-archetype dispatcher reads this; unhandled combinations fall back to
/// plain damage / no-op per GDD §14.
/// </summary>
public sealed record MoveMeta
{
    public required int CategoryId { get; init; }
    public required int AilmentId { get; init; }
    public int? MinHits { get; init; }
    public int? MaxHits { get; init; }
    public int? MinTurns { get; init; }
    public int? MaxTurns { get; init; }
    public int Drain { get; init; }
    public int Healing { get; init; }
    public int CritRateBonus { get; init; }
    public int AilmentChance { get; init; }
    public int FlinchChance { get; init; }
    public int StatChance { get; init; }
    public IReadOnlyList<MoveStatChange> StatChanges { get; init; } = [];
}

/// <summary>
/// A move snapshotted to Gen 5 (B2W2). Power/accuracy/PP/type/priority reflect the <c>move_changelog</c>
/// rewound to this version group, so e.g. Tackle is 50 power / 100 accuracy, not its later value.
/// </summary>
public sealed record Move
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required PokeType Type { get; init; }

    /// <summary>Base power; null for moves that deal no formula damage.</summary>
    public int? Power { get; init; }

    /// <summary>Accuracy percent; null for moves that bypass the accuracy check (never miss).</summary>
    public int? Accuracy { get; init; }

    public required int Pp { get; init; }
    public required int Priority { get; init; }
    public required DamageClass DamageClass { get; init; }

    /// <summary>veekun effect id — the archetype key the battle engine dispatches on.</summary>
    public required int EffectId { get; init; }

    /// <summary>Secondary-effect chance percent, if the effect is probabilistic.</summary>
    public int? EffectChance { get; init; }

    public required MoveMeta Meta { get; init; }
}
