using ProcPoke.Battle;
using ProcPoke.Data;

namespace ProcPoke.Overworld;

public enum ItemUseKind
{
    /// <summary>HP restored on a Pokémon that had lost some.</summary>
    HpRestored,

    /// <summary>A fainted Pokémon brought back.</summary>
    Revived,

    /// <summary>Evolved by the item itself — an evolution stone, or the Link Cable standing in for a trade.</summary>
    Evolved,

    /// <summary>A usable item with nothing to do right now (a Potion at full HP, a stone on the wrong
    /// species). Nothing is consumed.</summary>
    NoEffect,

    /// <summary>Not something the player can use from the bag at all, or not in the bag.</summary>
    NotUsable,
}

public sealed record ItemUseResult(ItemUseKind Kind, string Message, int? SpeciesId = null)
{
    /// <summary>Whether the item was spent and the party changed.</summary>
    public bool Changed => Kind is ItemUseKind.HpRestored or ItemUseKind.Revived or ItemUseKind.Evolved;
}

/// <summary>
/// Using one bag item on one party member. Medicine comes from <see cref="MedicineCatalog"/>; evolution
/// items are resolved straight out of the baked evolution rules, so every stone the data knows about works
/// without a table here.
/// </summary>
public static class ItemUse
{
    /// <summary>
    /// Applies <paramref name="itemId"/> to <paramref name="member"/>, or returns null when the item does
    /// nothing for it. The caller owns the bag and the party — this only computes the new member state.
    /// </summary>
    public static (BattleProgressionState Member, ItemUseResult Result)? TryApply(
        GameData data, BattleProgressionState member, int itemId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(member);
        var name = data.Items.TryGetValue(itemId, out var item) ? item.Name : null;
        if (name is null) return null;

        if (MedicineCatalog.For(name) is { } medicine) return ApplyMedicine(data, member, medicine, name);
        return ApplyEvolutionItem(data, member, itemId, name);
    }

    private static (BattleProgressionState, ItemUseResult)? ApplyMedicine(
        GameData data, BattleProgressionState member, Medicine medicine, string itemName)
    {
        var maxHp = BattlePokemonFactory.MaxHpOf(data, member.SpeciesId, member.Level, member.EffortValues);
        var displayName = Nicknames.DisplayName(data, member);
        var fainted = member.CurrentHp <= 0;

        switch (medicine.Effect)
        {
            case MedicineEffect.RestoreHp when fainted:
                return (member, new ItemUseResult(
                    ItemUseKind.NoEffect, $"{itemName} won't help a Pokémon that has fainted."));
            case MedicineEffect.RestoreHp when member.CurrentHp >= maxHp:
                return (member, new ItemUseResult(
                    ItemUseKind.NoEffect, $"{displayName} is already at full health."));
            case MedicineEffect.RestoreHp:
            {
                var restored = Math.Min(maxHp - member.CurrentHp, medicine.HpRestoredFor(maxHp));
                return (member with { CurrentHp = member.CurrentHp + restored }, new ItemUseResult(
                    ItemUseKind.HpRestored, $"{displayName} recovered {restored} HP."));
            }

            case MedicineEffect.Revive when !fainted:
                return (member, new ItemUseResult(
                    ItemUseKind.NoEffect, $"{displayName} has not fainted."));
            case MedicineEffect.Revive:
            {
                var revivedHp = Math.Min(maxHp, medicine.HpRestoredFor(maxHp));
                return (member with { CurrentHp = revivedHp }, new ItemUseResult(
                    ItemUseKind.Revived, $"{displayName} is back on its feet with {revivedHp} HP."));
            }

            // Sacred Ash works on the whole party at once, so the session — which owns the party — applies
            // it rather than this per-member path.
            case MedicineEffect.RevivePartyFully:
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// An evolution stone, or the Link Cable (CONTEXT.md's stand-in for trading). Both go through the baked
    /// rules, so nothing is hard-coded per species. Two classes of rule can never fire yet, and both fail
    /// closed rather than guessing: recipes needing a held item (Onix + Metal Coat), because a party member
    /// holds nothing, and gender-gated stones (Kirlia + Dawn Stone), because a party member has no gender.
    /// </summary>
    private static (BattleProgressionState, ItemUseResult)? ApplyEvolutionItem(
        GameData data, BattleProgressionState member, int itemId, string itemName)
    {
        var isLinkCable = itemName == LinkCable.ItemName;
        var trigger = isLinkCable ? EvolutionTrigger.LinkCable : EvolutionTrigger.UseItem;
        if (!isLinkCable && !data.Evolutions.Any(
                rule => rule.Trigger == EvolutionTrigger.UseItem && rule.TriggerItemId == itemId))
            return null;

        var context = new EvolutionContext
        {
            SpeciesId = member.SpeciesId,
            Level = member.Level,
            Trigger = trigger,
            ItemId = itemId,
            KnownMoveIds = member.MoveIds.ToHashSet(),
        };
        var displayName = Nicknames.DisplayName(data, member);
        if (EvolutionResolver.TryResolve(data, context) is not { } evolution)
            return (member, new ItemUseResult(
                ItemUseKind.NoEffect, $"{itemName} had no effect on {displayName}."));

        var evolved = Evolve(data, member, evolution.ToSpeciesId);
        return (evolved, new ItemUseResult(
            ItemUseKind.Evolved,
            $"{displayName} evolved into {data.Species[evolution.ToSpeciesId].Name}!",
            evolution.ToSpeciesId));
    }

    /// <summary>Keeps everything the Pokémon earned — level, experience, EVs, moves, its name — and carries
    /// current HP up by however much the new form's maximum grew, the way mainline does.</summary>
    public static BattleProgressionState Evolve(
        GameData data, BattleProgressionState member, int toSpeciesId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(member);
        var oldMax = BattlePokemonFactory.MaxHpOf(data, member.SpeciesId, member.Level, member.EffortValues);
        var newMax = BattlePokemonFactory.MaxHpOf(data, toSpeciesId, member.Level, member.EffortValues);
        var hp = member.CurrentHp <= 0
            ? 0
            : Math.Clamp(member.CurrentHp + (newMax - oldMax), 1, newMax);
        return member with { SpeciesId = toSpeciesId, CurrentHp = hp };
    }
}
