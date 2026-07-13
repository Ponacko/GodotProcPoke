namespace ProcPoke.Generation.Topology;

/// <summary>A per-archetype footprint tier (§4.2 Size Class). A bigger region gets more areas, never bigger ones.</summary>
public enum SizeClass { Small, Medium, Large }

/// <summary>
/// A node in the Region Graph: one route, town, or Special Location. Carries only structural facts;
/// tiles, gates, biomes, and names are attached by later passes.
/// </summary>
public sealed record Area
{
    public required int Id { get; init; }
    public required AreaArchetype Archetype { get; init; }
    public required SizeClass Size { get; init; }

    /// <summary>True if the area lies on the mandatory Route 1 → … → League critical path (the Spine).</summary>
    public required bool OnCriticalPath { get; init; }

    /// <summary>Position along the critical path (0 = start), or -1 for off-spine areas.</summary>
    public required int PathIndex { get; init; }

    public bool IsPlain => Archetype.IsPlain();
    public bool IsDungeon => Archetype.IsDungeon();
}
