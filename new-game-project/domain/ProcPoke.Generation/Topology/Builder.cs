using ProcPoke.Generation.Rng;

namespace ProcPoke.Generation.Topology;

/// <summary>
/// Mutable scratch state for <see cref="TopologyGenerator"/>. Accumulates areas and connections during
/// growth, then freezes into an immutable <see cref="RegionGraph"/>.
/// <para>
/// Owns the lattice occupancy map, and refuses to connect areas whose cells are not adjacent — the
/// invariant that keeps the region embeddable in 2-D. Callers pick cells through
/// <see cref="FreeCellsAdjacentTo"/> rather than inventing coordinates.
/// </para>
/// </summary>
internal sealed class Builder(int badgeCount)
{
    private readonly List<AreaDraft> _areas = [];
    private readonly List<Connection> _connections = [];
    private readonly Dictionary<int, int> _degree = [];
    private readonly Dictionary<GridCell, int> _occupants = [];
    private readonly HashSet<(int, int)> _connected = [];
    private int _nextId;

    private sealed class AreaDraft
    {
        public required int Id { get; init; }
        public required AreaArchetype Archetype { get; init; }
        public required SizeClass Size { get; init; }
        public required GridCell Cell { get; init; }
        public bool OnPath { get; set; }
        public int PathIndex { get; set; } = -1;
    }

    /// <summary>Adds an off-spine area on a free cell (call <see cref="MarkOnPath"/> to promote it).</summary>
    public int Add(AreaArchetype archetype, SizeClass size, GridCell cell)
    {
        if (_occupants.ContainsKey(cell))
            throw new InvalidOperationException($"cell {cell} is already occupied by area {_occupants[cell]}");

        var id = _nextId++;
        _areas.Add(new AreaDraft { Id = id, Archetype = archetype, Size = size, Cell = cell });
        _degree[id] = 0;
        _occupants[cell] = id;
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
    public GridCell CellOf(int id) => Draft(id).Cell;
    public bool IsFree(GridCell cell) => !_occupants.ContainsKey(cell);
    public bool AreConnected(int a, int b) => _connected.Contains(Key(a, b));

    /// <summary>Cells with no area on them that border <paramref name="areaId"/> — where a branch could go.</summary>
    public IEnumerable<GridCell> FreeCellsAdjacentTo(int areaId)
        => Draft(areaId).Cell.Neighbors().Where(IsFree);

    /// <summary>Areas already placed on cells bordering <paramref name="cell"/>.</summary>
    public IEnumerable<int> AreasAdjacentTo(GridCell cell)
        => cell.Neighbors().Where(_occupants.ContainsKey).Select(n => _occupants[n]);

    /// <summary>
    /// Joins two areas. They must sit on adjacent cells: a connection is a shared border, so anything else
    /// would be a graph edge no layout can draw. Connection kind follows ADR-0001 — a dungeon on either end
    /// means a Warp (a cave mouth or door), otherwise the border is walked across seamlessly.
    /// </summary>
    public void Connect(int a, int b, bool isLoopBack = false)
    {
        var (cellA, cellB) = (Draft(a).Cell, Draft(b).Cell);
        if (!cellA.IsAdjacentTo(cellB))
            throw new InvalidOperationException(
                $"areas {a} {cellA} and {b} {cellB} are not adjacent — connection would not be embeddable");
        if (!_connected.Add(Key(a, b)))
            return; // already joined; a loop-back must find a different pair

        var kind = Draft(a).Archetype.IsDungeon() || Draft(b).Archetype.IsDungeon()
            ? ConnectionKind.Warp
            : ConnectionKind.Seamless;
        _connections.Add(new Connection { AreaA = a, AreaB = b, Kind = kind, IsLoopBack = isLoopBack });
        _degree[a]++;
        _degree[b]++;
    }

    public int DegreeOf(int id) => _degree[id];

    /// <summary>Every area added so far, in creation order.</summary>
    public IEnumerable<int> AllIds => _areas.Select(a => a.Id);

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
                Cell = d.Cell,
            })
            .ToList();
        return new RegionGraph(areas, _connections, startId, leagueId, badgeCount);
    }

    private static (int, int) Key(int a, int b) => (Math.Min(a, b), Math.Max(a, b));

    private AreaDraft Draft(int id) => _areas[id]; // ids are dense and assigned in order
}
