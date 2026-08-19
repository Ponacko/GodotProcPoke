using ProcPoke.Data;

namespace ProcPoke.Battle;

public enum MedicineEffect
{
    /// <summary>Restores HP to a Pokémon that has not fainted.</summary>
    RestoreHp,

    /// <summary>Brings one fainted Pokémon back.</summary>
    Revive,

    /// <summary>Brings every fainted party member back at full HP (Sacred Ash).</summary>
    RevivePartyFully,
}

/// <summary>
/// What one medicine restores: either a flat amount (<paramref name="FixedHp"/>, e.g. a Potion's 20) or a
/// share of the Pokémon's own maximum (<paramref name="MaxHpFraction"/>, e.g. Revive's half). Exactly one
/// is set; both null means full.
/// </summary>
public sealed record Medicine(MedicineEffect Effect, int? FixedHp = null, double? MaxHpFraction = null)
{
    /// <summary>HP this medicine restores to a Pokémon with <paramref name="maxHp"/> maximum. Fractions
    /// floor, per Gen 5 — Revive on a 45 HP Pokémon gives 22, not 23.</summary>
    public int HpRestoredFor(int maxHp)
        => FixedHp ?? Math.Max(1, (int)(maxHp * (MaxHpFraction ?? 1.0)));
}

/// <summary>
/// The Gen 5 medicine table (the Canonical Ruleset), keyed by baked item name — the bake carries an item's
/// cost and category but not its effect, and hard-coding ids would break the moment the data pin moves.
/// Items from later generations are absent on purpose: the bake includes their medicine, and a Gen 5 game
/// has no rule for it. Status cures are absent too, because a party member carries no status condition
/// outside battle to cure — they become meaningful when in-battle bag use lands.
/// </summary>
public static class MedicineCatalog
{
    private static readonly Dictionary<string, Medicine> ByName = new(StringComparer.Ordinal)
    {
        ["Potion"] = new(MedicineEffect.RestoreHp, FixedHp: 20),
        ["Super Potion"] = new(MedicineEffect.RestoreHp, FixedHp: 50),
        ["Hyper Potion"] = new(MedicineEffect.RestoreHp, FixedHp: 200),
        ["Max Potion"] = new(MedicineEffect.RestoreHp),
        ["Full Restore"] = new(MedicineEffect.RestoreHp),
        ["Fresh Water"] = new(MedicineEffect.RestoreHp, FixedHp: 50),
        ["Soda Pop"] = new(MedicineEffect.RestoreHp, FixedHp: 60),
        ["Lemonade"] = new(MedicineEffect.RestoreHp, FixedHp: 80),
        ["Moomoo Milk"] = new(MedicineEffect.RestoreHp, FixedHp: 100),
        ["Berry Juice"] = new(MedicineEffect.RestoreHp, FixedHp: 20),
        ["Energy Powder"] = new(MedicineEffect.RestoreHp, FixedHp: 50),
        ["Energy Root"] = new(MedicineEffect.RestoreHp, FixedHp: 200),
        ["Revive"] = new(MedicineEffect.Revive, MaxHpFraction: 0.5),
        ["Max Revive"] = new(MedicineEffect.Revive),
        ["Revival Herb"] = new(MedicineEffect.Revive),
        ["Sacred Ash"] = new(MedicineEffect.RevivePartyFully),
    };

    public static Medicine? For(GameData data, int itemId)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.Items.TryGetValue(itemId, out var item) ? For(item.Name) : null;
    }

    public static Medicine? For(string itemName) => ByName.GetValueOrDefault(itemName);
}
