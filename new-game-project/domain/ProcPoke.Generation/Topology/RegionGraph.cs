namespace ProcPoke.Generation.Topology;

/// <summary>
/// The abstract structure of a region: areas as nodes, connections as edges (GDD §4.2). Produced by the
/// topology pass and consumed by every later pass; exists before — and independently of — any tile map.
/// Immutable once built.
/// </summary>
public sealed class RegionGraph
{
    private readonly Dictionary<int, Area> _byId;
    private readonly Dictionary<int, List<Connection>> _incident;

    public IReadOnlyList<Area> Areas { get; }
    public IReadOnlyList<Connection> Connections { get; }
    public int StartAreaId { get; }
    public int LeagueAreaId { get; }
    public int BadgeCount { get; }

    public RegionGraph(
        IReadOnlyList<Area> areas,
        IReadOnlyList<Connection> connections,
        int startAreaId,
        int leagueAreaId,
        int badgeCount)
    {
        Areas = areas;
        Connections = connections;
        StartAreaId = startAreaId;
        LeagueAreaId = leagueAreaId;
        BadgeCount = badgeCount;

        _byId = areas.ToDictionary(a => a.Id);
        _incident = areas.ToDictionary(a => a.Id, _ => new List<Connection>());
        foreach (var c in connections)
        {
            _incident[c.AreaA].Add(c);
            _incident[c.AreaB].Add(c);
        }
    }

    public Area this[int areaId] => _byId[areaId];

    public IReadOnlyList<Connection> ConnectionsOf(int areaId) => _incident[areaId];

    public IEnumerable<Area> Neighbors(int areaId)
        => _incident[areaId].Select(c => _byId[c.Other(areaId)]);

    /// <summary>The critical-path areas in order (start → League).</summary>
    public IReadOnlyList<Area> CriticalPath
        => Areas.Where(a => a.OnCriticalPath).OrderBy(a => a.PathIndex).ToList();

    /// <summary>Areas not on the Spine (branches, detours, destination dungeons).</summary>
    public IReadOnlyList<Area> OffSpineAreas
        => Areas.Where(a => !a.OnCriticalPath).ToList();

    /// <summary>True if every area is reachable from the start ignoring gates (pure structural connectivity).</summary>
    public bool IsConnected()
    {
        var seen = new HashSet<int> { StartAreaId };
        var stack = new Stack<int>();
        stack.Push(StartAreaId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var c in _incident[current])
            {
                var next = c.Other(current);
                if (seen.Add(next)) stack.Push(next);
            }
        }
        return seen.Count == Areas.Count;
    }
}
