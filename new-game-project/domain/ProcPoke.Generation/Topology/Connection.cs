namespace ProcPoke.Generation.Topology;

/// <summary>How two areas join: a seamless outdoor scroll (Map Connection) or a warp fade (interiors/dungeons).</summary>
public enum ConnectionKind { Seamless, Warp }

/// <summary>
/// An undirected edge between two areas. <see cref="IsLoopBack"/> marks a shortcut that closes a cycle —
/// typically only traversable once a later key is obtained (§4.2). Gates are attached to connections by
/// the later gate pass; topology only lays the edges.
/// </summary>
public sealed record Connection
{
    public required int AreaA { get; init; }
    public required int AreaB { get; init; }
    public required ConnectionKind Kind { get; init; }
    public bool IsLoopBack { get; init; }

    public bool Touches(int areaId) => AreaA == areaId || AreaB == areaId;
    public int Other(int areaId) => areaId == AreaA ? AreaB : AreaA;
}
