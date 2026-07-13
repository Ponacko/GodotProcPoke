namespace ProcPoke.Generation.Topology;

/// <summary>
/// What kind of place an area is. Routes and towns are the "plain" backbone (§4.2's ≤2-consecutive rule
/// counts these); the rest are Special Locations (§4.4). This is the MVP subset of archetypes; the wider
/// taxonomy (Ruins, Ship, Facility, …) is Phase 6 variety work.
/// </summary>
public enum AreaArchetype
{
    // Plain backbone
    StartTown,
    Town,
    Route,

    // Transit dungeons (walked through on the critical path)
    Forest,
    StandardCave,
    MountainPath,

    // Destination dungeons (visited for a reward, left the way you came)
    VillainHideout,
    Tower,
    DeepCave,

    // Endgame
    VictoryRoad,
    League,
}

public static class AreaArchetypeExtensions
{
    /// <summary>Routes and towns — the backbone areas the pacing rule limits to runs of two.</summary>
    public static bool IsPlain(this AreaArchetype a)
        => a is AreaArchetype.StartTown or AreaArchetype.Town or AreaArchetype.Route;

    /// <summary>Special Locations that connect by warp fade rather than seamless scrolling.</summary>
    public static bool IsDungeon(this AreaArchetype a) => a switch
    {
        AreaArchetype.Forest or AreaArchetype.StandardCave or AreaArchetype.MountainPath
            or AreaArchetype.VillainHideout or AreaArchetype.Tower or AreaArchetype.DeepCave
            or AreaArchetype.VictoryRoad => true,
        _ => false,
    };
}
