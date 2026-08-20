using ProcPoke.Battle;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// What the battle Bag button offers. The rule is "waste as little as possible": the smallest medicine that
/// still covers the damage, and only a bigger one when nothing does.
/// </summary>
public sealed class BattleBagTests
{
    private static int ItemId(string name)
        => TestData.Data.Items.Values.First(item => item.Name == name).Id;

    private static Bag BagOf(params string[] itemNames)
    {
        var bag = new Bag();
        foreach (var name in itemNames) bag.Add(ItemId(name));
        return bag;
    }

    [Fact]
    public void TheSmallestSufficientPotionWins()
    {
        var bag = BagOf("Potion", "Super Potion", "Hyper Potion", "Max Potion");

        // 18 HP missing: a Potion's 20 covers it with the least waste.
        Assert.Equal(
            ItemId("Potion"),
            BattleBag.BestFor(TestData.Data, bag, hp: 42, maxHp: 60)!.ItemId);

        // 45 missing: the Potion falls short, so the Super Potion's 50 is the tightest fit.
        Assert.Equal(
            ItemId("Super Potion"),
            BattleBag.BestFor(TestData.Data, bag, hp: 15, maxHp: 60)!.ItemId);
    }

    [Fact]
    public void NothingBigEnoughFallsBackToTheBiggestOnHand()
    {
        var bag = BagOf("Potion", "Super Potion");

        var choice = BattleBag.BestFor(TestData.Data, bag, hp: 10, maxHp: 400);

        Assert.Equal(ItemId("Super Potion"), choice!.ItemId);
    }

    [Fact]
    public void AFullHealthPokemonIsOfferedNothingUnlessAStatusNeedsCuring()
    {
        var withRestore = BagOf("Potion", "Full Restore");

        Assert.Null(BattleBag.BestFor(TestData.Data, withRestore, hp: 60, maxHp: 60));
        Assert.Equal(
            ItemId("Full Restore"),
            BattleBag.BestFor(TestData.Data, withRestore, hp: 60, maxHp: 60, BattleStatus.Poison)!.ItemId);

        // A plain Potion cannot cure anything, so a poisoned but healthy Pokémon is offered nothing.
        Assert.Null(BattleBag.BestFor(
            TestData.Data, BagOf("Potion"), hp: 60, maxHp: 60, BattleStatus.Poison));
    }

    [Fact]
    public void RevivesAndNonMedicineAreNeverOfferedInBattle()
    {
        var bag = BagOf("Revive", "Max Revive", "Sacred Ash", "Poké Ball");

        Assert.Empty(BattleBag.UsableFor(TestData.Data, bag, hp: 5, maxHp: 60));
        // A fainted Pokémon is a party operation, not a battle one — nothing is offered at all.
        Assert.Empty(BattleBag.UsableFor(
            TestData.Data, BagOf("Potion", "Revive"), hp: 0, maxHp: 60));
    }

    [Fact]
    public void OnlyWhatIsActuallyInTheBagIsOffered()
    {
        Assert.Null(BattleBag.BestFor(TestData.Data, new Bag(), hp: 5, maxHp: 60));

        var bag = BagOf("Potion");
        Assert.NotNull(BattleBag.BestFor(TestData.Data, bag, hp: 5, maxHp: 60));
        bag.Remove(ItemId("Potion"));
        Assert.Null(BattleBag.BestFor(TestData.Data, bag, hp: 5, maxHp: 60));
    }

    [Fact]
    public void TheStrongestOrdinaryBallIsThrownAndTheMasterBallIsSavedForALegendary()
    {
        var bag = BagOf("Poké Ball", "Great Ball", "Ultra Ball", "Master Ball");

        var ordinary = BattleBag.BestBallFor(TestData.Data, bag, legendary: false)!;
        Assert.Equal("Ultra Ball", ordinary.Ball.Name);
        Assert.False(ordinary.Ball.IsMaster);

        var legendary = BattleBag.BestBallFor(TestData.Data, bag, legendary: true)!;
        Assert.True(legendary.Ball.IsMaster);
        Assert.Equal("Master Ball", legendary.Ball.Name);
    }

    [Fact]
    public void AMasterBallAloneIsStillNotSpentOnAnOrdinaryEncounter()
    {
        var bag = BagOf("Master Ball");

        Assert.Null(BattleBag.BestBallFor(TestData.Data, bag, legendary: false));
        Assert.NotNull(BattleBag.BestBallFor(TestData.Data, bag, legendary: true));
    }

    [Fact]
    public void NoBallInTheBagMeansNoThrow()
    {
        Assert.Null(BattleBag.BestBallFor(TestData.Data, new Bag(), legendary: false));
        Assert.Null(BattleBag.BestBallFor(TestData.Data, new Bag(), legendary: true));
        // A situational ball has no Gen 5 multiplier this battle can honour, so it is not offered.
        Assert.Empty(BattleBag.BallsIn(TestData.Data, BagOf("Net Ball", "Dusk Ball")));
    }

    [Fact]
    public void ABallChoiceConvertsStraightIntoAThrow()
    {
        var choice = BattleBag.BestBallFor(TestData.Data, BagOf("Great Ball"), legendary: false)!;

        var action = choice.ToAction();

        Assert.Equal("Great Ball", action.BallId);
        Assert.Equal(1.5, action.BallMultiplier);
    }

    [Fact]
    public void TheChoiceConvertsStraightIntoABattleAction()
    {
        var choice = BattleBag.BestFor(TestData.Data, BagOf("Potion"), hp: 5, maxHp: 60)!;

        var action = choice.ToAction();

        Assert.Equal(choice.ItemId, action.ItemId);
        Assert.Equal("Potion", action.ItemName);
        Assert.Equal(20, action.Medicine.HpRestoredFor(60));
    }
}
