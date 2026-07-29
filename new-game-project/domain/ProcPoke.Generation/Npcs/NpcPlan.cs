namespace ProcPoke.Generation.Npcs;

/// <summary>The role of a graph-level NPC post. Tile placement is deferred to ticket 8b.</summary>
public enum NpcKind
{
    Hint,
    GymGuide,
    CenterGossip,
    Sign,
    Flavor,
}

/// <summary>One generated NPC or sign, before it is realized on a carved map.</summary>
public sealed record NpcPost(NpcKind Kind, string Text);

/// <summary>All NPC posts grouped by area. Every graph area has an entry, including empty ones.</summary>
public sealed record NpcPlan
{
    public required IReadOnlyDictionary<int, IReadOnlyList<NpcPost>> ByArea { get; init; }

    public IReadOnlyList<NpcPost> Of(int areaId)
        => ByArea.TryGetValue(areaId, out var posts) ? posts : [];
}
