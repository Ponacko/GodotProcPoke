using ProcPoke.Generation.Encounters;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class WildEncounterSelectorTests
{
    [Fact]
    public void SlotWeightsAndLevelBandAreSelectedDeterministically()
    {
        var plan = Plan(new EncounterTable(EncounterMethod.Land,
            [new EncounterSlot(60, 1, 3, 5), new EncounterSlot(40, 4, 6, 8)]));
        var values = new Queue<int>([60, 2]);

        var encounter = WildEncounterSelector.Select(plan, 7, EncounterMethod.Land, false,
            max => values.Dequeue());

        Assert.Equal(4, encounter.SpeciesId);
        Assert.Equal(8, encounter.Level);
        Assert.False(encounter.UsedOverlay);
        Assert.False(encounter.HiddenAbility);
    }

    [Fact]
    public void OverlaySelectionCarriesItsHiddenAbilityChance()
    {
        var baseTable = new EncounterTable(EncounterMethod.Land, [new EncounterSlot(100, 1, 2, 2)]);
        var overlay = new EncounterTable(EncounterMethod.Land, [new EncounterSlot(100, 25, 7, 9)], 0.5);
        var values = new Queue<int>([0, 1, 4999]);

        var encounter = WildEncounterSelector.Select(
            new EncounterPlan
            {
                ByArea = new Dictionary<int, AreaEncounters>
                {
                    [7] = new AreaEncounters(7, [baseTable], overlay, 5),
                },
            }, 7, EncounterMethod.Land, true, max => values.Dequeue());

        Assert.Equal(25, encounter.SpeciesId);
        Assert.Equal(8, encounter.Level);
        Assert.True(encounter.UsedOverlay);
        Assert.True(encounter.HiddenAbility);
    }

    [Fact]
    public void SelectingAnUnavailableMethodFailsClearly()
    {
        var plan = Plan(new EncounterTable(EncounterMethod.Land,
            [new EncounterSlot(100, 1, 3, 3)]));

        Assert.Throws<ArgumentException>(() => WildEncounterSelector.Select(
            plan, 7, EncounterMethod.Surf, false, _ => 0));
    }

    private static EncounterPlan Plan(EncounterTable table)
        => new()
        {
            ByArea = new Dictionary<int, AreaEncounters>
            {
                [7] = new AreaEncounters(7, [table], null, 5),
            },
        };
}
