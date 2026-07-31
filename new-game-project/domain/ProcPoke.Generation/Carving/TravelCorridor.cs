namespace ProcPoke.Generation.Carving;

/// <summary>
/// Lays the protected travel corridor used by route-like areas. The layout is authored in a
/// <see cref="CarveFrame"/> whose reference border is the exit, so the long leg of the corridor always
/// points toward the way onward; a turn is represented by the short across-axis leg rather than by a
/// horizontal world-space stripe.
/// </summary>
internal static class TravelCorridor
{
    public static int? WorldSouthLedgeRow(
        TileGrid grid, IReadOnlyList<AreaOpening> planned, EdgeSide? entrySide, EdgeSide? exitSide)
    {
        if (planned.Count == 0) return null;
        var reference = exitSide ?? entrySide ?? planned[0].Edge;
        var frame = new CarveFrame(reference, grid.Width, grid.Height);
        var entryOpening = OpeningOn(planned, entrySide) ?? planned[0];
        var exitOpening = OpeningOn(planned, exitSide)
            ?? planned.FirstOrDefault(o => o != entryOpening)
            ?? entryOpening;
        var entry = frame.Unmap(InteriorPoint(entryOpening, grid.Width, grid.Height).X,
            InteriorPoint(entryOpening, grid.Width, grid.Height).Y);
        var exit = frame.Unmap(InteriorPoint(exitOpening, grid.Width, grid.Height).X,
            InteriorPoint(exitOpening, grid.Width, grid.Height).Y);

        // In an exit-horizontal frame the u-leg is east/west; in an exit-vertical frame the v-leg is
        // east/west. A straight vertical passage therefore has no legal world-south ledge row.
        if (reference is EdgeSide.Left or EdgeSide.Right && entry.U != exit.U)
            return frame.Map(entry.U, entry.V).Y;
        if (reference is EdgeSide.Top or EdgeSide.Bottom && entry.V != exit.V)
            return frame.Map(exit.U, entry.V).Y;
        return null;
    }

    public static IReadOnlyList<(int X, int Y)> Carve(
        TileGrid grid,
        IReadOnlyList<AreaOpening> planned,
        EdgeSide? entrySide,
        EdgeSide? exitSide,
        LogicalTile floor,
        ISet<(int X, int Y)> protectedTiles,
        int halfWidth)
    {
        if (planned.Count == 0) return [];

        var reference = exitSide ?? entrySide ?? planned[0].Edge;
        var frame = new CarveFrame(reference, grid.Width, grid.Height);
        var selectedEntry = OpeningOn(planned, entrySide) ?? planned[0];
        var selectedExit = OpeningOn(planned, exitSide)
            ?? planned.FirstOrDefault(o => o != selectedEntry)
            ?? selectedEntry;

        var openings = new List<(int X, int Y)>(planned.Count);
        foreach (var opening in planned)
        {
            var border = opening.TileOn(grid.Width, grid.Height);
            OpenBorder(grid, border, opening.Edge, opening.Offset, floor);
            openings.Add(border);
        }

        var entry = InteriorPoint(selectedEntry, grid.Width, grid.Height);
        var exit = InteriorPoint(selectedExit, grid.Width, grid.Height);
        CarveCanonicalL(grid, frame, entry, exit, floor, protectedTiles, halfWidth);

        // Branches and loop-backs are not part of the critical-path pair, but must still enter the same
        // protected corridor. Connect each one to its nearest corridor tile with a short L in the same
        // frame; this keeps every opening mutually reachable without reopening the whole room.
        foreach (var opening in planned)
        {
            var inner = InteriorPoint(opening, grid.Width, grid.Height);
            if (protectedTiles.Contains(inner)) continue;
            var canonical = frame.Unmap(inner.X, inner.Y);
            var anchor = protectedTiles
                .Select(p => frame.Unmap(p.X, p.Y))
                .OrderBy(p => Math.Abs(p.U - canonical.U) + Math.Abs(p.V - canonical.V))
                .First();
            CarveCanonicalL(grid, frame, inner, frame.Map(anchor.U, anchor.V), floor, protectedTiles, halfWidth: 0);
        }

        return openings;
    }

    private static AreaOpening? OpeningOn(IReadOnlyList<AreaOpening> openings, EdgeSide? side)
        => side is null ? null : openings.FirstOrDefault(o => o.Edge == side.Value);

    private static (int X, int Y) InteriorPoint(AreaOpening opening, int width, int height)
    {
        var border = opening.TileOn(width, height);
        return opening.Edge switch
        {
            EdgeSide.Left => (Math.Min(width - 2, border.X + 1), border.Y),
            EdgeSide.Right => (Math.Max(1, border.X - 1), border.Y),
            EdgeSide.Top => (border.X, Math.Min(height - 2, border.Y + 1)),
            _ => (border.X, Math.Max(1, border.Y - 1)),
        };
    }

    private static void OpenBorder(
        TileGrid grid, (int X, int Y) border, EdgeSide edge, int offset, LogicalTile floor)
    {
        if (edge is EdgeSide.Left or EdgeSide.Right)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var y = offset + dy;
                if (grid.InBounds(border.X, y)) grid[border.X, y] = floor;
            }
        }

        grid[border.X, border.Y] = LogicalTile.Warp;
    }

    private static void CarveCanonicalL(
        TileGrid grid,
        CarveFrame frame,
        (int X, int Y) from,
        (int X, int Y) to,
        LogicalTile floor,
        ISet<(int X, int Y)> protectedTiles,
        int halfWidth)
    {
        var a = frame.Unmap(from.X, from.Y);
        var b = frame.Unmap(to.X, to.Y);

        for (var u = a.U; u <= b.U; u++)
            WriteBand(frame, grid, u, a.V, halfWidth, floor, protectedTiles, acrossU: false);
        for (var u = a.U; u >= b.U; u--)
            WriteBand(frame, grid, u, a.V, halfWidth, floor, protectedTiles, acrossU: false);

        var step = a.V <= b.V ? 1 : -1;
        for (var v = a.V;; v += step)
        {
            WriteBand(frame, grid, b.U, v, halfWidth, floor, protectedTiles, acrossU: true);
            if (v == b.V) break;
        }
    }

    private static void WriteBand(
        CarveFrame frame,
        TileGrid grid,
        int u,
        int v,
        int halfWidth,
        LogicalTile floor,
        ISet<(int X, int Y)> protectedTiles,
        bool acrossU)
    {
        for (var delta = -halfWidth; delta <= halfWidth; delta++)
        {
            var uu = acrossU ? u + delta : u;
            var vv = acrossU ? v : v + delta;
            if (!frame.InBounds(uu, vv)) continue;
            var point = frame.Map(uu, vv);
            if (point.X < 1 || point.X > grid.Width - 2 || point.Y < 1 || point.Y > grid.Height - 2)
                continue;
            grid[point.X, point.Y] = floor;
            protectedTiles.Add(point);
        }
    }
}
