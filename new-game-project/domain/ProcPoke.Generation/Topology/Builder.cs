using ProcPoke.Generation.Rng;

namespace ProcPoke.Generation.Topology;

/// <summary>
/// Mutable scratch state for <see cref="TopologyGenerator"/>. Accumulates areas and connections during
/// growth, then freezes into an immutable <see cref="RegionGraph"/>.
/// </summary>
internal sealed class Builder(int badgeCount)
{
    private readonly List<AreaDraft> _areas = [];
    private readonly List<Connection> _connections = [];
    private readonly Dictionary<int, int> _degree = [];
    private int _nextId;

    private sealed class AreaDraft
    {
        public required int Id { get; init; }
        public required AreaArchetype Archetype { get; init; }
        public required SizeClass Size { get; init; }
        public bool OnPath { get; set; }
        public int PathIndex { get; set; } = -1;
    }

    /// <summary>Adds an off-spine area (call <see cref="MarkOnPath"/> to promote it to the critical path).</summary>
    public int Add(AreaArchetype archetype, SizeClass size)
    {
        var id = _nextId++;
        _areas.Add(new AreaDraft { Id = id, Archetype = archetype, Size = size });
        _degree[id] = 0;
        return id;
    }

    public void MarkOnPath(int id, int pathIndex)
    {
        var a = Draft(id);
        a.OnPath = true;
        a.PathIndex = pathIndex;
    }

    public AreaArchetype ArchetypeOf(int id) => Draft(id).Archetype;
    public int PathIndexOf(int id) => Draft(id).PathIndex;

    public void Connect(int a, int b, bool isLoopBack = false)
    {
        var kind = Draft(a).Archetype.IsDungeon() || Draft(b).Archetype.IsDungeon()
            ? ConnectionKind.Warp
            : ConnectionKind.Seamless;
        _connections.Add(new Connection { AreaA = a, AreaB = b, Kind = kind, IsLoopBack = isLoopBack });
        _degree[a]++;
        _degree[b]++;
    }

    /// <summary>A random off-spine dead-end (exactly one connection), or null if none is free.</summary>
    public int? OffSpineWithSingleConnection(Pcg32 rng)
    {
        var candidates = _areas.Where(a => !a.OnPath && _degree[a.Id] == 1).Select(a => a.Id).ToList();
        return candidates.Count == 0 ? null : rng.Pick(candidates);
    }

    /// <summary>The critical-path index of the spine area a dead-end hangs off (its anchor).</summary>
    public int AnchorPathIndex(int offSpineId)
    {
        var connection = _connections.First(c => c.Touches(offSpineId));
        return Draft(connection.Other(offSpineId)).PathIndex;
    }

    public RegionGraph Build(int startId, int leagueId)
    {
        var areas = _areas
            .Select(d => new Area
            {
                Id = d.Id,
                Archetype = d.Archetype,
                Size = d.Size,
                OnCriticalPath = d.OnPath,
                PathIndex = d.PathIndex,
            })
            .ToList();
        return new RegionGraph(areas, _connections, startId, leagueId, badgeCount);
    }

    private AreaDraft Draft(int id) => _areas[id]; // ids are dense and assigned in order
}
