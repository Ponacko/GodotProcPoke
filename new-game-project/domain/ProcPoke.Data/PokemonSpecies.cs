namespace ProcPoke.Data;

/// <summary>One ability a species can be born with, resolved to its Gen 5 identity.</summary>
public sealed record AbilitySlot(int Id, string Name);

/// <summary>
/// A base-form species (1–649), snapshotted to Gen 5. Form variants are dropped at bake; the form
/// system is Post-MVP (GDD §12). Values reflect B2W2, with the <c>_past</c> tables applied where a
/// stat, type, or ability differed before Gen 6.
/// </summary>
public sealed record PokemonSpecies
{
    public required int Id { get; init; }
    public required string Name { get; init; }

    /// <summary>One or two types, in slot order.</summary>
    public required IReadOnlyList<PokeType> Types { get; init; }

    public required StatBlock BaseStats { get; init; }

    /// <summary>Ability slot 1 — every species has one.</summary>
    public required AbilitySlot Ability1 { get; init; }

    /// <summary>Ability slot 2, if the species has a second regular ability.</summary>
    public AbilitySlot? Ability2 { get; init; }

    /// <summary>
    /// The Dream World / Hidden Grotto ability. Baked but flagged: gameplay assigns it only via a
    /// Special Encounter Overlay (~50% there), never on an ordinary encounter (CONTEXT.md).
    /// </summary>
    public AbilitySlot? HiddenAbility { get; init; }

    public required int CatchRate { get; init; }

    /// <summary>Base experience yield (the B2W2 scaled formula consumes this).</summary>
    public required int BaseExperience { get; init; }

    /// <summary>Effort values awarded on defeat.</summary>
    public required StatBlock EvYield { get; init; }

    /// <summary>Growth-rate identifier, keyed to <see cref="GrowthRate"/>.</summary>
    public required string GrowthRate { get; init; }

    /// <summary>veekun gender rate: -1 genderless, else eighths that are female (0 = all male, 8 = all female).</summary>
    public required int GenderRate { get; init; }

    public bool IsLegendary { get; init; }
    public bool IsMythical { get; init; }
}
