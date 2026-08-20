using ProcPoke.Data;

namespace ProcPoke.Battle;

/// <summary>One kind of Poké Ball: its catch multiplier, and whether it is the guaranteed one.</summary>
public sealed record Ball(string Name, double Multiplier, bool IsMaster = false);

/// <summary>
/// The Gen 5 ball multipliers, keyed by baked item name — the same reason the medicine table is by name:
/// the bake carries an item's cost and category, never its effect.
///
/// Only the unconditional balls are listed. Gen 5's situational balls (Net, Dive, Nest, Repeat, Timer,
/// Quick, Dusk) multiply on facts a single-active battle does not carry yet — the target's types, the turn
/// count, the encounter method, the time of day — so they are absent rather than modelled at a wrong flat
/// rate, and a bag full of them offers nothing until those conditions exist to check.
/// </summary>
public static class BallCatalog
{
    private static readonly Dictionary<string, Ball> ByName = new(StringComparer.Ordinal)
    {
        ["Poké Ball"] = new("Poké Ball", 1.0),
        ["Great Ball"] = new("Great Ball", 1.5),
        ["Ultra Ball"] = new("Ultra Ball", 2.0),
        // The Master Ball never fails. The catch formula has no "always" branch, so it gets a multiplier no
        // catch rate can survive rather than a special case threaded through the simulation.
        ["Master Ball"] = new("Master Ball", 255.0, IsMaster: true),
        ["Premier Ball"] = new("Premier Ball", 1.0),
        ["Luxury Ball"] = new("Luxury Ball", 1.0),
        ["Heal Ball"] = new("Heal Ball", 1.0),
        ["Cherish Ball"] = new("Cherish Ball", 1.0),
        ["Dream Ball"] = new("Dream Ball", 1.0),
    };

    public static Ball? For(GameData data, int itemId)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.Items.TryGetValue(itemId, out var item) ? For(item.Name) : null;
    }

    public static Ball? For(string itemName) => ByName.GetValueOrDefault(itemName);
}
