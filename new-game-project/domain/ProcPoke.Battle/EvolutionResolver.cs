using ProcPoke.Data;

namespace ProcPoke.Battle;

/// <summary>State and context used when checking one possible evolution.</summary>
public sealed record EvolutionContext
{
    public required int SpeciesId { get; init; }
    public required int Level { get; init; }
    public EvolutionTrigger Trigger { get; init; } = EvolutionTrigger.LevelUp;
    public int? ItemId { get; init; }
    public int? HeldItemId { get; init; }
    public int Happiness { get; init; }
    public int Beauty { get; init; }
    public string? TimeOfDay { get; init; }
    public int? GenderId { get; init; }
    public int Attack { get; init; }
    public int Defense { get; init; }
    public IReadOnlySet<int> KnownMoveIds { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> KnownMoveTypeIds { get; init; } = new HashSet<int>();
    public int? PartySpeciesId { get; init; }
    public int? PartyTypeId { get; init; }
    public int? LocationId { get; init; }
    public bool NearSpecialRock { get; init; }
    public bool OverworldRain { get; init; }
    public bool TurnedUpsideDown { get; init; }
}

public sealed record EvolutionResult(int FromSpeciesId, int ToSpeciesId, EvolutionRule Rule);

/// <summary>Deterministically resolves the first satisfied baked evolution rule.</summary>
public static class EvolutionResolver
{
    public static EvolutionResult? TryResolve(GameData data, EvolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(context);
        if (!data.Species.ContainsKey(context.SpeciesId))
            throw new ArgumentException($"Unknown species {context.SpeciesId}.", nameof(context));
        if (context.Level is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(context), "Level must be in [1, 100].");
        if (context.Attack < 0 || context.Defense < 0)
            throw new ArgumentOutOfRangeException(nameof(context), "Physical stats cannot be negative.");

        var rule = data.Evolutions
            .Where(candidate => candidate.FromSpeciesId == context.SpeciesId
                && candidate.Trigger == context.Trigger)
            .OrderBy(candidate => candidate.ToSpeciesId)
            .FirstOrDefault(candidate => Satisfied(candidate, context));
        return rule is null ? null : new EvolutionResult(context.SpeciesId, rule.ToSpeciesId, rule);
    }

    private static bool Satisfied(EvolutionRule rule, EvolutionContext context)
    {
        if (rule.MinLevel is int minLevel && context.Level < minLevel) return false;
        if (rule.Trigger == EvolutionTrigger.UseItem && context.ItemId != rule.TriggerItemId) return false;
        if (rule.HeldItemId is int heldItem && context.HeldItemId != heldItem) return false;
        if (rule.MinHappiness is int happiness && context.Happiness < happiness) return false;
        if (rule.MinBeauty is int beauty && context.Beauty < beauty) return false;
        if (rule.TimeOfDay is string time
            && !string.Equals(time, context.TimeOfDay, StringComparison.OrdinalIgnoreCase)) return false;
        if (rule.GenderId is int gender && context.GenderId != gender) return false;
        if (rule.KnownMoveId is int move && !context.KnownMoveIds.Contains(move)) return false;
        if (rule.KnownMoveTypeId is int moveType && !context.KnownMoveTypeIds.Contains(moveType)) return false;
        if (rule.RelativePhysicalStats is int relative
            && Math.Sign(context.Attack - context.Defense) != relative) return false;
        if (rule.PartySpeciesId is int partySpecies && context.PartySpeciesId != partySpecies) return false;
        if (rule.PartyTypeId is int partyType && context.PartyTypeId != partyType) return false;
        if (rule.LocationId is int location && context.LocationId != location) return false;
        if (rule.NeedsSpecialRock && !context.NearSpecialRock) return false;
        if (rule.NeedsOverworldRain && !context.OverworldRain) return false;
        if (rule.TurnUpsideDown && !context.TurnedUpsideDown) return false;
        return true;
    }
}
