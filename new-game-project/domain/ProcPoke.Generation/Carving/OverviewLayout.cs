using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>One area's placement in the 2-D overview grid (ticket 2b).</summary>
public sealed record OverviewCell
{
    public required int AreaId { get; init; }
    public required int Col { get; init; }
    public required int Row { get; init; }
}

/// <summary>
/// Assigns every area an integer grid cell so <c>MapImage.SaveOverview</c> can lay the region out as one
/// spatially coherent map instead of a top-to-bottom stack (ticket 2b). The critical path runs left→right,
/// one column per path index, row 0. Each off-spine area hangs off its anchor (the critical-path area
/// <see cref="GatingGenerator.OffSpineAnchors"/> says it connects to, matching the gating pass's own rule):
/// one row above the anchor's column if that cell is free, else one row below, else one row further out.
/// This is deliberately not force-directed or edge-butted — a readable grid is enough for the go/no-go
/// review.
/// </summary>
public static class OverviewLayout
{
    public static IReadOnlyList<OverviewCell> Plan(RegionGraph graph)
    {
        var cells = new Dictionary<int, OverviewCell>();
        var occupied = new HashSet<(int Col, int Row)>();

        foreach (var area in graph.CriticalPath)
        {
            cells[area.Id] = new OverviewCell { AreaId = area.Id, Col = area.PathIndex, Row = 0 };
            occupied.Add((area.PathIndex, 0));
        }

        var anchors = GatingGenerator.OffSpineAnchors(graph);
        foreach (var area in graph.OffSpineAreas.OrderBy(a => a.Id))
        {
            // Every off-spine area is created (topology pass) with a direct edge to a critical-path area,
            // so it always has an anchor; a miss here is a topology-invariant break, not routine input.
            var col = anchors[area.Id];
            var row = FreeRow(occupied, col);
            occupied.Add((col, row));
            cells[area.Id] = new OverviewCell { AreaId = area.Id, Col = col, Row = row };
        }

        return graph.Areas.Select(a => cells[a.Id]).ToList();
    }

    /// <summary>Above the anchor if free, else below, else further out on alternating sides.</summary>
    private static int FreeRow(HashSet<(int Col, int Row)> occupied, int col)
    {
        if (!occupied.Contains((col, -1))) return -1;
        if (!occupied.Contains((col, 1))) return 1;

        var distance = 2;
        while (true)
        {
            if (!occupied.Contains((col, -distance))) return -distance;
            if (!occupied.Contains((col, distance))) return distance;
            distance++;
        }
    }
}
