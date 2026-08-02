namespace ProcPoke.Generation.Encounters;

public sealed record WildEncounter(
    int AreaId,
    EncounterMethod Method,
    int SpeciesId,
    int Level,
    bool UsedOverlay,
    bool HiddenAbility);

/// <summary>
/// Selects one runtime wild encounter from generated tables. The random callback is deliberately tiny:
/// generation stays engine-independent, while Overworld/Battle can provide their own replayable source.
/// </summary>
public static class WildEncounterSelector
{
    public static WildEncounter Select(
        EncounterPlan plan,
        int areaId,
        EncounterMethod method,
        bool useOverlay,
        Func<int, int> nextInt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(nextInt);
        var area = plan.Of(areaId)
            ?? throw new ArgumentException($"Area {areaId} has no encounter plan.", nameof(areaId));
        var table = useOverlay
            ? area.Overlay is { Method: var overlayMethod } overlay && overlayMethod == method
                ? overlay
                : throw new ArgumentException($"Area {areaId} has no {method} overlay.", nameof(method))
            : area.Tables.FirstOrDefault(candidate => candidate.Method == method)
                ?? throw new ArgumentException($"Area {areaId} has no {method} table.", nameof(method));
        if (table.Slots.Count == 0 || table.Slots.Sum(slot => slot.Percent) != 100)
            throw new InvalidOperationException($"Encounter table {areaId}/{method} must contain weighted slots summing to 100.");

        var slotRoll = nextInt(100);
        if (slotRoll is < 0 or >= 100)
            throw new InvalidOperationException($"Encounter RNG returned {slotRoll} for a 0..99 roll.");
        var cumulative = 0;
        EncounterSlot? selected = null;
        foreach (var slot in table.Slots)
        {
            cumulative += slot.Percent;
            if (slotRoll < cumulative)
            {
                selected = slot;
                break;
            }
        }
        if (selected is null)
            throw new InvalidOperationException($"Encounter table {areaId}/{method} did not select a slot.");

        var levelRange = selected.MaxLevel - selected.MinLevel + 1;
        if (levelRange <= 0) throw new InvalidOperationException("Encounter slot has an invalid level range.");
        var level = selected.MinLevel + nextInt(levelRange);
        var hiddenAbility = table.HiddenAbilityChance > 0
            && nextInt(10000) < table.HiddenAbilityChance * 10000;
        return new WildEncounter(areaId, method, selected.SpeciesId, level, useOverlay, hiddenAbility);
    }
}
