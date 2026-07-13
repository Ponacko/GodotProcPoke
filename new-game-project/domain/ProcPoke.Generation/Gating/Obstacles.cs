namespace ProcPoke.Generation.Gating;

/// <summary>The distinct obstacle classes from the §4.3 master table. One gate removes exactly one class.</summary>
public enum ObstacleClass
{
    CutTree,
    SurfWater,
    Boulder,
    CrackedRock,
    DarkCave,
    Waterfall,
    BikeRoad,
    DeepWater,
    Whirlpool,
    SleepingPokemon,
    Guardian,
    SealedTower,
    SeaCrossing,
    Fog,
    RockWall,
    InvisiblePokemon,
    BikeTerrain,
    SandstormDesert,
}

/// <summary>A move-key (permanent HM, occupies a moveslot, badge-gated) or an item-key (bag item, no badge).</summary>
public enum KeyKind { Hm, KeyItem }

/// <summary>
/// A row of the obstacle/key table (§4.3): what removes the obstacle, how likely it is to be drawn, when
/// it typically appears (as a fraction of critical-path progression), and — for terrain-bound obstacles —
/// the Biome Requirement it will emit for the later biome pass (ADR-0004).
/// </summary>
public sealed record ObstacleDef(
    ObstacleClass Class,
    string KeyName,
    KeyKind KeyKind,
    double Weight,
    double Timing,
    string? TerrainTag)
{
    /// <summary>100%-chance classics are guaranteed budget slots before any weighted draw.</summary>
    public bool Guaranteed => Weight >= 100.0;
    public bool IsHm => KeyKind == KeyKind.Hm;
}

/// <summary>
/// The master obstacle table, carried over from the design brief (§4.3). Weights are relative selection
/// odds (100 = guaranteed); timings are representative progression fractions.
/// </summary>
public static class ObstacleTable
{
    public static readonly IReadOnlyList<ObstacleDef> All =
    [
        new(ObstacleClass.CutTree,          "Cut",          KeyKind.Hm,      100, 0.12, null),
        new(ObstacleClass.SurfWater,        "Surf",         KeyKind.Hm,      100, 0.42, "water"),
        new(ObstacleClass.Boulder,          "Strength",     KeyKind.Hm,      100, 0.38, null),
        new(ObstacleClass.CrackedRock,      "Rock Smash",   KeyKind.Hm,       80, 0.22, null),
        new(ObstacleClass.DarkCave,         "Flash",        KeyKind.Hm,       80, 0.25, null),
        new(ObstacleClass.Waterfall,        "Waterfall",    KeyKind.Hm,       80, 0.82, "water"),
        new(ObstacleClass.BikeRoad,         "Bicycle",      KeyKind.KeyItem,  80, 0.30, null),
        new(ObstacleClass.SeaCrossing,      "Ship Ticket",  KeyKind.KeyItem,  60, 0.50, "sea"),
        new(ObstacleClass.DeepWater,        "Dive",         KeyKind.Hm,       40, 0.80, "deep-water"),
        new(ObstacleClass.Whirlpool,        "Whirlpool",    KeyKind.Hm,       40, 0.62, "water"),
        new(ObstacleClass.SleepingPokemon,  "Poké Flute",   KeyKind.KeyItem,  40, 0.50, null),
        new(ObstacleClass.Guardian,         "Squirtbottle", KeyKind.KeyItem,  40, 0.45, null),
        new(ObstacleClass.SealedTower,      "Relic Pair",   KeyKind.KeyItem,  40, 0.55, null),
        new(ObstacleClass.Fog,              "Defog",        KeyKind.Hm,       20, 0.35, null),
        new(ObstacleClass.RockWall,         "Rock Climb",   KeyKind.Hm,       20, 0.70, null),
        new(ObstacleClass.InvisiblePokemon, "Devon Scope",  KeyKind.KeyItem,  20, 0.40, null),
        new(ObstacleClass.BikeTerrain,      "Bike Upgrade", KeyKind.KeyItem,  20, 0.50, null),
        new(ObstacleClass.SandstormDesert,  "Go-Goggles",   KeyKind.KeyItem,  20, 0.35, "desert"),
    ];

    /// <summary>Fly: guaranteed in every seed, never a key, badge-gated around mid-game (§4.3 rule 9).</summary>
    public const string FlyKeyName = "Fly";
    public const double FlyTiming = 0.40;
}
