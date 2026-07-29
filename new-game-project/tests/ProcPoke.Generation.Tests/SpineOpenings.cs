using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Locates an area's entry and exit tiles by <em>which neighbour they face</em> rather than by edge.
/// <para>
/// Tests used to find the spine exit with <c>Openings.First(o => o.X == Grid.Width - 1)</c>, which was only
/// ever right because the spine ran east. Now that it wanders, that predicate finds a branch opening — or
/// nothing — so a test written that way checks the wrong tile or throws. Asking the opening plan which
/// opening faces the next critical-path area is correct whichever way the spine turns.
/// </para>
/// </summary>
internal static class SpineOpenings
{
    /// <summary>The tile where <paramref name="area"/> opens onto the critical-path area at
    /// <paramref name="pathIndex"/>, or null if there is no such area or no opening facing it.</summary>
    public static (int X, int Y)? Toward(GeneratedRegion region, Area area, int pathIndex)
    {
        var neighbor = region.Graph.Areas
            .FirstOrDefault(a => a.OnCriticalPath && a.PathIndex == pathIndex);
        if (neighbor is null) return null;

        var opening = region.Openings.EdgesOf(area.Id)
            .FirstOrDefault(o => o.NeighborAreaId == neighbor.Id);
        if (opening is null) return null;

        var grid = region.Carved[area.Id].Grid;
        return opening.TileOn(grid.Width, grid.Height);
    }

    /// <summary>Where the area opens toward the next critical-path area — the gated side when it holds a gate.</summary>
    public static (int X, int Y)? Exit(GeneratedRegion region, Area area)
        => Toward(region, area, area.PathIndex + 1);

    /// <summary>Where the area opens back toward the previous critical-path area.</summary>
    public static (int X, int Y)? Entry(GeneratedRegion region, Area area)
        => Toward(region, area, area.PathIndex - 1);
}
