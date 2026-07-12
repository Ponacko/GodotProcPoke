namespace ProcPoke.Data;

/// <summary>
/// One evolution rule, after the bake-time Link Cable transform. Trade-triggered evolutions have their
/// trigger rewritten to <see cref="EvolutionTrigger.LinkCable"/> (with <see cref="WasTrade"/> set) so
/// they are completable without trading; any original held-item requirement is preserved in
/// <see cref="HeldItemId"/> — the species must hold that item when the cable is used (CONTEXT.md).
/// </summary>
public enum EvolutionTrigger
{
    LevelUp,
    UseItem,
    Shed,
    Other,
    LinkCable,
}

public sealed record EvolutionRule
{
    public required int FromSpeciesId { get; init; }
    public required int ToSpeciesId { get; init; }
    public required EvolutionTrigger Trigger { get; init; }

    /// <summary>True when this rule was a trade evolution before the Link Cable transform.</summary>
    public bool WasTrade { get; init; }

    public int? MinLevel { get; init; }
    public int? TriggerItemId { get; init; }
    public int? HeldItemId { get; init; }
    public string? TimeOfDay { get; init; }
    public int? KnownMoveId { get; init; }
    public int? KnownMoveTypeId { get; init; }
    public int? MinHappiness { get; init; }
    public int? MinBeauty { get; init; }

    /// <summary>1 = female-only, 2 = male-only (veekun gender_id); null = any.</summary>
    public int? GenderId { get; init; }

    /// <summary>Tyrogue split: 1 atk&gt;def, -1 atk&lt;def, 0 atk==def.</summary>
    public int? RelativePhysicalStats { get; init; }

    public int? PartySpeciesId { get; init; }
    public int? PartyTypeId { get; init; }
    public int? LocationId { get; init; }

    /// <summary>Level-up beside a mossy/icy rock (Leafeon, Glaceon in Gen 5 — a location-based trigger).</summary>
    public bool NeedsSpecialRock { get; init; }

    public bool NeedsOverworldRain { get; init; }
    public bool TurnUpsideDown { get; init; }
}
