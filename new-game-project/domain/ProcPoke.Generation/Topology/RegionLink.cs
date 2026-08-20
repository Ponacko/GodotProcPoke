namespace ProcPoke.Generation.Topology;

/// <summary>Where a second region could attach: an area with a free lattice cell beside it, and that
/// cell.</summary>
public sealed record RegionLinkSlot(int AreaId, GridCell Cell, Heading Heading);

/// <summary>
/// The reserved external-connection slot (GDD §11, §15): every region keeps one free lattice cell beside a
/// real area, so the Phase 7 Region Link has somewhere to attach without region 1 being regenerated.
///
/// It is a check rather than a reservation, because the lattice already guarantees it — a region occupies a
/// sparse set of cells, so an area with a free neighbour always exists. Stating and testing that is what
/// turns the provision from a hope into a promise; <see cref="Invariants"/> fails a region that breaks it.
/// </summary>
public static class RegionLink
{
    /// <summary>
    /// The slot a Region Link would use, or null if the lattice somehow left none. A city is preferred —
    /// a coastal city becomes the port for the ship crossing (§15), and an inland one is where a border
    /// pass starts — and ties break by critical-path position so the choice is deterministic.
    /// </summary>
    public static RegionLinkSlot? SlotFor(RegionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var occupied = graph.Areas.Select(area => area.Cell).ToHashSet();

        var candidates = graph.Areas
            .OrderByDescending(area => IsCity(area))
            .ThenBy(area => area.PathIndex < 0 ? int.MaxValue : area.PathIndex)
            .ThenBy(area => area.Id);

        foreach (var area in candidates)
        foreach (var heading in GridCell.AllHeadings)
        {
            var cell = area.Cell.Step(heading);
            if (!occupied.Contains(cell)) return new RegionLinkSlot(area.Id, cell, heading);
        }
        return null;
    }

    private static bool IsCity(Area area)
        => area.Archetype is AreaArchetype.StartTown or AreaArchetype.Town;
}
