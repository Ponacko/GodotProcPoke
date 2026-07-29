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
/// Reports every area's cell so <c>MapImage.SaveOverview</c> and the in-game viewer can lay the region out
/// as one spatially coherent map (ticket 2b).
/// <para>
/// This used to *invent* the layout after the fact — critical path along row 0, one column per path index,
/// branches stacked above and below their anchor. That is what made every region read as one long corridor
/// running east, and it silently drew connections between areas many columns apart, because nothing tied an
/// edge in the graph to a shared border. The layout now belongs to the topology pass
/// (<see cref="Area.Cell"/>), which places areas on a wandering walk and refuses to connect cells that do
/// not touch; this class just reads it out.
/// </para>
/// </summary>
public static class OverviewLayout
{
    public static IReadOnlyList<OverviewCell> Plan(RegionGraph graph)
        => graph.Areas
            .Select(a => new OverviewCell { AreaId = a.Id, Col = a.Cell.Col, Row = a.Cell.Row })
            .ToList();
}
