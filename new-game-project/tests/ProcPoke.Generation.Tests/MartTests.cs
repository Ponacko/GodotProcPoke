using ProcPoke.Generation;
using ProcPoke.Generation.Topology;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class MartTests
{
    private static GeneratedRegion Generate(ulong seed = 42)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);

    [Fact]
    public void CatalogNeverOffersZeroCostItemsAndWidensWithFraction()
    {
        var early = MartCatalog.For(TestData.Data, 0.0);
        var late = MartCatalog.For(TestData.Data, 1.0);

        Assert.NotEmpty(early);
        Assert.NotEmpty(late);
        Assert.All(early.Concat(late), itemId => Assert.True(TestData.Data.Items[itemId].Cost > 0));
        Assert.All(early, itemId => Assert.True(TestData.Data.Items[itemId].Cost <= 200));
        Assert.All(late, itemId => Assert.True(TestData.Data.Items[itemId].Cost <= 3000));

        // Late-game stock is a superset of categories the early catalog carries at all (standard-balls,
        // healing), never narrower — the fraction only ever adds categories/price headroom.
        var earlyCategories = early.Select(itemId => TestData.Data.Items[itemId].Category).ToHashSet();
        var lateCategories = late.Select(itemId => TestData.Data.Items[itemId].Category).ToHashSet();
        Assert.Subset(lateCategories, earlyCategories.Concat(lateCategories).ToHashSet());
        Assert.Contains("standard-balls", lateCategories);
        Assert.Contains("healing", lateCategories);
    }

    [Fact]
    public void AvailabilityFractionIsMonotoneAlongTheCriticalPathAndBounded()
    {
        var region = Generate();
        var towns = region.Graph.CriticalPath
            .Where(area => area.Archetype is AreaArchetype.StartTown or AreaArchetype.Town)
            .OrderBy(area => area.PathIndex)
            .ToArray();
        Assert.True(towns.Length >= 2);

        var fractions = towns.Select(town => MartAvailability.FractionOf(region.Graph, town.Id)).ToArray();
        Assert.All(fractions, f => Assert.InRange(f, 0.0, 1.0));
        for (var i = 1; i < fractions.Length; i++)
            Assert.True(fractions[i] > fractions[i - 1], "later towns must have a strictly larger fraction");
    }

    [Fact]
    public void BuyingWithNoMoneyNeverSpendsOrGrantsAnything()
    {
        var region = Generate();
        var session = new OverworldSession(
            region, NewGameIdentity.Create(CharacterChoice.CharacterA, "Player"));
        var town = region.Graph.Areas.First(area => area.Archetype == AreaArchetype.StartTown);

        var result = session.BuyFromMart(TestData.Data, town.Id);

        Assert.Equal(MartPurchaseKind.NothingAffordable, result.Kind);
        Assert.Null(result.ItemId);
        Assert.Equal(0, session.Money);
        Assert.Empty(session.Interactions.Bag.Items);
    }

    [Fact]
    public void BuyingWithEnoughMoneySpendsExactlyTheCostAndFillsTheBagWithTheCostliestAffordableItem()
    {
        var region = Generate();
        var session = new OverworldSession(
            region, NewGameIdentity.Create(CharacterChoice.CharacterA, "Player"));
        var town = region.Graph.Areas.First(area => area.Archetype == AreaArchetype.StartTown);
        session.CompleteTrainerBattle(new GeneratedTrainerBattleTransitionRequest(
            "t1", "Rich Trainer", new PlayerState(new GridPosition(0, 0)), [], 100_000));

        var stock = MartCatalog.For(TestData.Data, MartAvailability.FractionOf(region.Graph, town.Id));
        var expectedItemId = stock.OrderByDescending(id => TestData.Data.Items[id].Cost).First();
        var expectedCost = TestData.Data.Items[expectedItemId].Cost;
        var moneyBefore = session.Money;

        var result = session.BuyFromMart(TestData.Data, town.Id);

        Assert.Equal(MartPurchaseKind.Bought, result.Kind);
        Assert.Equal(expectedItemId, result.ItemId);
        Assert.Equal(expectedCost, result.Cost);
        Assert.Equal(moneyBefore - expectedCost, session.Money);
        Assert.True(session.Interactions.Bag.Has(expectedItemId));
    }
}
