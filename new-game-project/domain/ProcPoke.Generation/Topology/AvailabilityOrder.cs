namespace ProcPoke.Generation.Topology;

/// <summary>
/// Canonical "first-availability" ordering of every area (GDD §5.2): the critical path in PathIndex order,
/// with each off-spine area inserted right after the critical-path area it hangs off. This is the order the
/// dex numbers by, encounters level by, and trainers scale by — one definition so every pass agrees.
/// </summary>
public static class AvailabilityOrder
{
    /// <param name="offSpineAnchors">areaId → the min PathIndex among its on-critical-path neighbours,
    /// from GatingGenerator.OffSpineAnchors(graph). Passed in to keep Topology free of a Gating reference.</param>
    /// <returns>Every area id exactly once.</returns>
    public static IReadOnlyList<int> Of(RegionGraph graph, IReadOnlyDictionary<int, int> offSpineAnchors)
    {
        var order = new List<int>();
        foreach (var area in graph.CriticalPath) // already sorted by PathIndex
        {
            order.Add(area.Id);
            // Every off-spine area anchored to this position, ascending id. (Each off-spine area has exactly
            // one anchor — the topology pass gives every branch a direct edge to the critical path.)
            foreach (var offSpine in offSpineAnchors.Where(kv => kv.Value == area.PathIndex)
                         .Select(kv => kv.Key).OrderBy(id => id))
                order.Add(offSpine);
        }
        return order;
    }
}
