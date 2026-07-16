using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Identity;

/// <summary>One region-wide naming motif (GDD §8) — colors, flora, minerals, or weather/light.</summary>
public enum NamingMotif
{
    Colors,
    Flora,
    Minerals,
    WeatherLight,
}

/// <summary>
/// Curated word-part pools per motif (GDD §8). City names blend a <see cref="Roots"/> entry with a
/// settlement suffix; dungeon names blend a root and a <see cref="Blends"/> entry with the archetype's
/// biome-aware form noun.
/// </summary>
public static class NameParts
{
    public static readonly IReadOnlyList<string> SettlementSuffixes = ["burgh", "port", "vale", "ton"];

    public static readonly IReadOnlyDictionary<NamingMotif, (IReadOnlyList<string> Roots, IReadOnlyList<string> Blends)> Pools =
        new Dictionary<NamingMotif, (IReadOnlyList<string>, IReadOnlyList<string>)>
        {
            [NamingMotif.Colors] = (
                ["Ember", "Azure", "Crimson", "Violet", "Amber", "Ivory"],
                ["dusk", "glow", "shade", "bloom", "veil", "gleam"]),
            [NamingMotif.Flora] = (
                ["Willow", "Thorn", "Moss", "Bramble", "Fern", "Petal"],
                ["grove", "root", "vine", "wood", "glen", "briar"]),
            [NamingMotif.Minerals] = (
                ["Quartz", "Obsidian", "Copper", "Granite", "Opal", "Basalt"],
                ["shard", "vein", "cavern", "ridge", "spire", "hollow"]),
            [NamingMotif.WeatherLight] = (
                ["Storm", "Mist", "Dawn", "Frost", "Gale", "Lumen"],
                ["drift", "haze", "shimmer", "reach", "veil", "flicker"]),
        };

    /// <summary>The biome-aware noun a dungeon name ends in, keyed by its archetype.</summary>
    public static string DungeonForm(AreaArchetype archetype) => archetype switch
    {
        AreaArchetype.Forest => "Forest",
        AreaArchetype.StandardCave or AreaArchetype.DeepCave => "Cave",
        AreaArchetype.MountainPath => "Pass",
        AreaArchetype.VillainHideout => "Hideout",
        AreaArchetype.Tower => "Tower",
        AreaArchetype.VictoryRoad => "Road",
        AreaArchetype.League => "League",
        _ => throw new ArgumentOutOfRangeException(nameof(archetype), archetype, "not a dungeon archetype"),
    };
}
