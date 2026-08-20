using ProcPoke.Battle;
using ProcPoke.Data;

namespace ProcPoke.Overworld;

/// <summary>One bag item picked out for a battle turn, ready to hand to the simulation.</summary>
public sealed record BattleItemChoice(int ItemId, string ItemName, Medicine Medicine)
{
    public UseItemAction ToAction() => new(ItemId, ItemName, Medicine);
}

/// <summary>One ball out of the bag, ready to throw.</summary>
public sealed record BattleBallChoice(int ItemId, Ball Ball)
{
    public ThrowBallAction ToAction() => new(Ball.Name, Ball.Multiplier);
}

/// <summary>
/// Which medicine or ball to reach for mid-battle. A battle bag is narrower than the field bag: reviving
/// needs a party to revive from, which a single-active battle has not got, so only HP (and Full Restore's
/// status cure) is on offer here.
/// </summary>
public static class BattleBag
{
    /// <summary>Everything in the bag that would do something for a Pokémon in this state, cheapest
    /// effect first — so a caller with no item menu takes the head of the list.</summary>
    public static IReadOnlyList<BattleItemChoice> UsableFor(
        GameData data, Bag bag, int hp, int maxHp, BattleStatus status = BattleStatus.None)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(bag);
        if (maxHp <= 0) throw new ArgumentOutOfRangeException(nameof(maxHp));
        if (hp <= 0) return []; // a fainted Pokémon needs a Revive, and that is a party operation

        var missing = maxHp - hp;
        return bag.Items.Keys
            .Select(itemId => (itemId, item: data.Items.GetValueOrDefault(itemId)))
            .Where(pair => pair.item is not null)
            .Select(pair => (pair.itemId, pair.item, medicine: MedicineCatalog.For(pair.item!.Name)))
            .Where(pair => pair.medicine is { Effect: MedicineEffect.RestoreHp }
                && (missing > 0 || (pair.medicine.CuresStatus && status != BattleStatus.None)))
            .Select(pair => new BattleItemChoice(pair.itemId, pair.item!.Name, pair.medicine!))
            // Waste as little as possible: prefer the smallest medicine that still covers what is missing,
            // and only then the biggest that does not — a Full Restore is not for a scratch.
            .OrderBy(choice => Overshoot(choice, missing, maxHp))
            .ThenBy(choice => data.Items[choice.ItemId].Cost)
            .ThenBy(choice => choice.ItemId)
            .ToArray();
    }

    /// <summary>The best single choice, or null when nothing in the bag would help.</summary>
    public static BattleItemChoice? BestFor(
        GameData data, Bag bag, int hp, int maxHp, BattleStatus status = BattleStatus.None)
        => UsableFor(data, bag, hp, maxHp, status).FirstOrDefault();

    /// <summary>Every ball in the bag, strongest first.</summary>
    public static IReadOnlyList<BattleBallChoice> BallsIn(GameData data, Bag bag)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(bag);
        return bag.Items.Keys
            .Select(itemId => (itemId, ball: BallCatalog.For(data, itemId)))
            .Where(pair => pair.ball is not null)
            .Select(pair => new BattleBallChoice(pair.itemId, pair.ball!))
            .OrderByDescending(choice => choice.Ball.Multiplier)
            .ThenBy(choice => choice.ItemId)
            .ToArray();
    }

    /// <summary>
    /// The ball to throw, or null when the bag holds none. The strongest ordinary ball wins, and the Master
    /// Ball is held back for a legendary — a one-tap throw that spent it on a wild Rattata would be a worse
    /// bug than not offering it at all.
    /// </summary>
    public static BattleBallChoice? BestBallFor(GameData data, Bag bag, bool legendary)
    {
        var balls = BallsIn(data, bag);
        var ordinary = balls.FirstOrDefault(choice => !choice.Ball.IsMaster);
        if (legendary) return balls.FirstOrDefault() ?? ordinary;
        return ordinary;
    }

    /// <summary>Sort key: items that cover the missing HP rank by how much they overheal, and items that
    /// fall short rank behind all of them, biggest first.</summary>
    private static int Overshoot(BattleItemChoice choice, int missing, int maxHp)
    {
        var restored = choice.Medicine.HpRestoredFor(maxHp);
        return restored >= missing
            ? restored - missing
            : int.MaxValue / 2 + (missing - restored);
    }
}
