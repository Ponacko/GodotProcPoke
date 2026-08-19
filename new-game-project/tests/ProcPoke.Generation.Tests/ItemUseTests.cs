using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Field bag use: medicine off the Gen 5 table, and evolution items resolved out of the baked rules. An
/// item that does nothing must also cost nothing — the bag is the player's money in another form.
/// </summary>
public sealed class ItemUseTests
{
    private static OverworldSession NewSession()
    {
        var region = RegionGenerator.Generate(
            new GenerationSettings { Seed = 42, BadgeCount = 8 }, TestData.Data);
        return new OverworldSession(region, NewGameIdentity.Create(CharacterChoice.CharacterA, "Player"));
    }

    private static int ItemId(string name)
        => TestData.Data.Items.Values.First(item => item.Name == name).Id;

    private static int MaxHpOf(BattleProgressionState member)
        => BattlePokemonFactory.MaxHpOf(TestData.Data, member.SpeciesId, member.Level, member.EffortValues);

    [Fact]
    public void PotionRestoresItsFixedAmountAndIsSpent()
    {
        var session = NewSession();
        var member = BattleProgressionFactory.AtLevel(TestData.Data, 1, 30);
        var maxHp = MaxHpOf(member);
        session.Party.Add(member with { CurrentHp = 1 });
        var potion = ItemId("Potion");
        session.Interactions.Bag.Add(potion, 2);

        var result = session.UseItem(TestData.Data, potion, 0);

        Assert.Equal(ItemUseKind.HpRestored, result.Kind);
        Assert.Equal(Math.Min(maxHp, 21), session.Party.Members[0].CurrentHp);
        Assert.Equal(1, session.Interactions.Bag.Count(potion));
    }

    [Fact]
    public void HealingNeverOverfillsAndNeverSpendsWhenItCannotHelp()
    {
        var session = NewSession();
        var member = BattleProgressionFactory.AtLevel(TestData.Data, 1, 30);
        var maxHp = MaxHpOf(member);
        session.Party.Add(member with { CurrentHp = maxHp - 1 });
        var hyper = ItemId("Hyper Potion");
        session.Interactions.Bag.Add(hyper, 2);

        Assert.Equal(ItemUseKind.HpRestored, session.UseItem(TestData.Data, hyper, 0).Kind);
        Assert.Equal(maxHp, session.Party.Members[0].CurrentHp);
        Assert.Equal(1, session.Interactions.Bag.Count(hyper));

        // Already full: nothing happens, and the second potion is still there.
        Assert.Equal(ItemUseKind.NoEffect, session.UseItem(TestData.Data, hyper, 0).Kind);
        Assert.Equal(1, session.Interactions.Bag.Count(hyper));
    }

    [Fact]
    public void PotionsDoNotWorkOnAFaintedPokemonButRevivesDo()
    {
        var session = NewSession();
        var member = BattleProgressionFactory.AtLevel(TestData.Data, 1, 30);
        var maxHp = MaxHpOf(member);
        session.Party.Add(member with { CurrentHp = 0 });
        var potion = ItemId("Potion");
        var revive = ItemId("Revive");
        session.Interactions.Bag.Add(potion);
        session.Interactions.Bag.Add(revive, 2);

        Assert.Equal(ItemUseKind.NoEffect, session.UseItem(TestData.Data, potion, 0).Kind);
        Assert.Equal(1, session.Interactions.Bag.Count(potion));
        Assert.Equal(0, session.Party.Members[0].CurrentHp);

        Assert.Equal(ItemUseKind.Revived, session.UseItem(TestData.Data, revive, 0).Kind);
        Assert.Equal(maxHp / 2, session.Party.Members[0].CurrentHp);

        // A Pokémon on its feet has no use for a second Revive.
        Assert.Equal(ItemUseKind.NoEffect, session.UseItem(TestData.Data, revive, 0).Kind);
        Assert.Equal(1, session.Interactions.Bag.Count(revive));
    }

    [Fact]
    public void SacredAshRevivesTheWholePartyAtOnce()
    {
        var session = NewSession();
        for (var i = 0; i < 3; i++)
        {
            var member = BattleProgressionFactory.AtLevel(TestData.Data, 1, 20);
            session.Party.Add(member with { CurrentHp = i == 2 ? 5 : 0 });
        }
        var ash = ItemId("Sacred Ash");
        session.Interactions.Bag.Add(ash, 2);

        var result = session.UseItem(TestData.Data, ash, 0);

        Assert.Equal(ItemUseKind.Revived, result.Kind);
        Assert.All(session.Party.Members.Take(2), member => Assert.Equal(MaxHpOf(member), member.CurrentHp));
        Assert.Equal(5, session.Party.Members[2].CurrentHp); // untouched: it had not fainted
        Assert.Equal(1, session.Interactions.Bag.Count(ash));

        Assert.Equal(ItemUseKind.NoEffect, session.UseItem(TestData.Data, ash, 0).Kind);
        Assert.Equal(1, session.Interactions.Bag.Count(ash));
    }

    [Fact]
    public void AnEvolutionStoneEvolvesTheSpeciesItsRuleNamesAndNoOther()
    {
        // Pick a real stone rule out of the baked data rather than naming a species here.
        var rule = TestData.Data.Evolutions.First(candidate =>
            candidate.Trigger == EvolutionTrigger.UseItem
            && candidate.TriggerItemId is int itemId
            && TestData.Data.Items.ContainsKey(itemId)
            && candidate.HeldItemId is null
            && candidate.GenderId is null
            && candidate.MinLevel is null);
        var stone = rule.TriggerItemId!.Value;

        var session = NewSession();
        var subject = BattleProgressionFactory.AtLevel(TestData.Data, rule.FromSpeciesId, 25);
        session.Party.Add(subject with { CurrentHp = 3, Nickname = "Rocky" });
        session.Interactions.Bag.Add(stone, 2);

        var result = session.UseItem(TestData.Data, stone, 0);

        Assert.Equal(ItemUseKind.Evolved, result.Kind);
        Assert.Equal(rule.ToSpeciesId, result.SpeciesId);
        var evolved = session.Party.Members[0];
        Assert.Equal(rule.ToSpeciesId, evolved.SpeciesId);
        Assert.Equal(subject.Level, evolved.Level);
        Assert.Equal(subject.MoveIds, evolved.MoveIds);
        Assert.Equal("Rocky", evolved.Nickname);
        // Current HP rides the max-HP change rather than resetting.
        Assert.Equal(3 + (MaxHpOf(evolved) - MaxHpOf(subject)), evolved.CurrentHp);
        Assert.Equal(1, session.Interactions.Bag.Count(stone));

        // The same stone on something it has no rule for is a no-op, and stays in the bag.
        var wrongSpecies = TestData.Data.Species.Keys
            .First(id => !TestData.Data.Evolutions.Any(candidate =>
                candidate.FromSpeciesId == id && candidate.TriggerItemId == stone));
        session.Party.Add(BattleProgressionFactory.AtLevel(TestData.Data, wrongSpecies, 25));
        Assert.Equal(ItemUseKind.NoEffect, session.UseItem(TestData.Data, stone, 1).Kind);
        Assert.Equal(1, session.Interactions.Bag.Count(stone));
    }

    [Fact]
    public void TheLinkCableStandsInForATradeEvolution()
    {
        // A trade evolution with no held-item recipe: the cable alone finishes it (CONTEXT.md).
        var rule = TestData.Data.Evolutions.First(candidate =>
            candidate.Trigger == EvolutionTrigger.LinkCable && candidate.HeldItemId is null);
        var cable = LinkCable.IdIn(TestData.Data);

        var session = NewSession();
        session.Party.Add(BattleProgressionFactory.AtLevel(TestData.Data, rule.FromSpeciesId, 30));
        session.Interactions.Bag.Add(cable);

        var result = session.UseItem(TestData.Data, cable, 0);

        Assert.Equal(ItemUseKind.Evolved, result.Kind);
        Assert.Equal(rule.ToSpeciesId, session.Party.Members[0].SpeciesId);
        Assert.False(session.Interactions.Bag.Has(cable));
    }

    /// <summary>A trade recipe that also wants a held item cannot be satisfied while party members hold
    /// nothing — it must fail closed, not evolve for free.</summary>
    [Fact]
    public void TheLinkCableRefusesHeldItemRecipes()
    {
        var rule = TestData.Data.Evolutions.First(candidate =>
            candidate.Trigger == EvolutionTrigger.LinkCable && candidate.HeldItemId is not null);
        var cable = LinkCable.IdIn(TestData.Data);

        var session = NewSession();
        session.Party.Add(BattleProgressionFactory.AtLevel(TestData.Data, rule.FromSpeciesId, 30));
        session.Interactions.Bag.Add(cable);

        var result = session.UseItem(TestData.Data, cable, 0);

        Assert.Equal(ItemUseKind.NoEffect, result.Kind);
        Assert.Equal(rule.FromSpeciesId, session.Party.Members[0].SpeciesId);
        Assert.True(session.Interactions.Bag.Has(cable));
    }

    [Fact]
    public void ItemsOutsideTheBagOrOutsideTheRulesAreNotUsable()
    {
        var session = NewSession();
        session.Party.Add(BattleProgressionFactory.AtLevel(TestData.Data, 1, 20));
        var potion = ItemId("Potion");

        Assert.Equal(ItemUseKind.NotUsable, session.UseItem(TestData.Data, potion, 0).Kind);

        // A held item is a real baked item with no field use at all.
        var heldItem = TestData.Data.Items.Values.First(item => item.Category == "held-items").Id;
        session.Interactions.Bag.Add(heldItem);
        Assert.Equal(ItemUseKind.NotUsable, session.UseItem(TestData.Data, heldItem, 0).Kind);
        Assert.True(session.Interactions.Bag.Has(heldItem));
    }

    [Fact]
    public void MedicineFractionsFollowGen5Rounding()
    {
        // Revive gives half, floored: 45 HP → 22.
        Assert.Equal(22, MedicineCatalog.For("Revive")!.HpRestoredFor(45));
        Assert.Equal(1, MedicineCatalog.For("Revive")!.HpRestoredFor(1));
        Assert.Equal(45, MedicineCatalog.For("Max Revive")!.HpRestoredFor(45));
        Assert.Equal(20, MedicineCatalog.For("Potion")!.HpRestoredFor(999));
        // Later-generation medicine has no Gen 5 rule, so the table does not claim one.
        Assert.Null(MedicineCatalog.For("Sweet Heart"));
    }
}
