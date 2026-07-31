using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;

namespace ProcPoke.MapGen;

/// <summary>Renders carved tile grids to PNG images: one per area, plus a spatially stitched region overview.</summary>
internal static class MapImage
{
    private static readonly (byte R, byte G, byte B) BackgroundColor = (24, 24, 28);
    private static readonly (byte R, byte G, byte B) ConnectorColor = (245, 245, 235);

    private static (byte R, byte G, byte B) Color(LogicalTile t) => t switch
    {
        LogicalTile.Ground => (210, 200, 150),
        LogicalTile.TallGrass => (90, 170, 80),
        LogicalTile.Tree => (30, 100, 40),
        LogicalTile.Wall => (110, 100, 95),
        LogicalTile.Water => (70, 130, 200),
        LogicalTile.Sand => (225, 205, 140),
        LogicalTile.Ledge => (170, 140, 90),
        LogicalTile.Boulder => (120, 110, 100),
        LogicalTile.CutTree => (60, 140, 60),
        LogicalTile.GateObstacle => (200, 40, 40),
        LogicalTile.Warp => (240, 220, 60),
        LogicalTile.TrainerPost => (220, 60, 180),
        LogicalTile.ItemBall => (240, 150, 40),
        LogicalTile.NpcPost => (80, 220, 220),
        _ => (0, 0, 0),
    };

    public static void SaveArea(string path, CarvedArea area, int scale = 8)
    {
        var (w, h, rgb) = Render(area.Grid, scale);
        Png.WriteRgb(path, w, h, rgb);
    }

    /// <summary>Renders the fixed-cell, seam-collapsed world canvas as one landmass image.</summary>
    public static void SaveWorld(string path, WorldCanvasMap world, int scale = 4)
    {
        var (w, h, rgb) = Render(world.Grid, scale);
        Png.WriteRgb(path, w, h, rgb);
    }

    /// <summary>
    /// Lays every area (critical path + off-spine) into 2-D grid cells per <see cref="OverviewLayout"/> and
    /// blits each carved footprint into a canvas sized from each column's widest and each row's tallest
    /// area, so the overview reads as one spatially coherent map (ticket 2b) rather than a stack. Draws a
    /// thin connector between each connection's pair of openings (or area centers, for Warp connections,
    /// which have no aligned opening).
    /// </summary>
    public static void SaveOverview(
        string path, RegionGraph graph, IReadOnlyList<CarvedArea> areas, OpeningPlan openings, int scale = 6)
    {
        const int gap = 12;
        var areasById = areas.ToDictionary(a => a.AreaId);
        var layout = OverviewLayout.Plan(graph).ToDictionary(c => c.AreaId);

        var cols = layout.Values.Select(c => c.Col).Distinct().OrderBy(c => c).ToList();
        var rows = layout.Values.Select(c => c.Row).Distinct().OrderBy(r => r).ToList();

        int ColWidth(int col) => layout.Values.Where(c => c.Col == col).Max(c => areasById[c.AreaId].Grid.Width) * scale;
        int RowHeight(int row) => layout.Values.Where(c => c.Row == row).Max(c => areasById[c.AreaId].Grid.Height) * scale;

        var colX = new Dictionary<int, int>();
        var x = gap;
        foreach (var c in cols) { colX[c] = x; x += ColWidth(c) + gap; }

        var rowY = new Dictionary<int, int>();
        var y = gap;
        foreach (var r in rows) { rowY[r] = y; y += RowHeight(r) + gap; }

        var totalW = x;
        var totalH = y;
        var canvas = new byte[totalW * totalH * 3];
        for (var i = 0; i < canvas.Length; i += 3)
        {
            canvas[i] = BackgroundColor.R;
            canvas[i + 1] = BackgroundColor.G;
            canvas[i + 2] = BackgroundColor.B;
        }

        var origin = new Dictionary<int, (int X, int Y)>();
        foreach (var area in areas)
        {
            var cell = layout[area.AreaId];
            var (w, h, rgb) = Render(area.Grid, scale);
            var ox = colX[cell.Col] + (ColWidth(cell.Col) - w) / 2;
            var oy = rowY[cell.Row] + (RowHeight(cell.Row) - h) / 2;
            origin[area.AreaId] = (ox, oy);
            for (var ty = 0; ty < h; ty++)
                Array.Copy(rgb, ty * w * 3, canvas, ((oy + ty) * totalW + ox) * 3, w * 3);
        }

        foreach (var c in graph.Connections)
        {
            var pointA = ConnectorPoint(c.AreaA, c.AreaB, areasById, origin, openings, scale);
            var pointB = ConnectorPoint(c.AreaB, c.AreaA, areasById, origin, openings, scale);
            DrawLine(canvas, totalW, totalH, pointA.X, pointA.Y, pointB.X, pointB.Y, ConnectorColor);
        }

        Png.WriteRgb(path, totalW, totalH, canvas);
    }

    /// <summary>The pixel a connector should touch on <paramref name="areaId"/>'s side of its connection to
    /// <paramref name="neighborId"/>: the aligned opening tile if one was planned (Seamless), else the
    /// area's rendered center (Warp — no aligned opening to point at).</summary>
    private static (int X, int Y) ConnectorPoint(
        int areaId, int neighborId,
        IReadOnlyDictionary<int, CarvedArea> areasById,
        IReadOnlyDictionary<int, (int X, int Y)> origin,
        OpeningPlan openings,
        int scale)
    {
        var area = areasById[areaId];
        var (ox, oy) = origin[areaId];
        var opening = openings.OpeningsOf(areaId).FirstOrDefault(o => o.NeighborAreaId == neighborId);
        if (opening is not null)
        {
            var tile = opening.TileOn(area.Grid.Width, area.Grid.Height);
            return (ox + tile.X * scale + scale / 2, oy + tile.Y * scale + scale / 2);
        }

        return (ox + area.Grid.Width * scale / 2, oy + area.Grid.Height * scale / 2);
    }

    private static void DrawLine(byte[] canvas, int canvasW, int canvasH, int x0, int y0, int x1, int y1, (byte R, byte G, byte B) color)
    {
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;

        while (true)
        {
            Plot(canvas, canvasW, canvasH, x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void Plot(byte[] canvas, int canvasW, int canvasH, int x, int y, (byte R, byte G, byte B) color)
    {
        if (x < 0 || y < 0 || x >= canvasW || y >= canvasH) return;
        var idx = (y * canvasW + x) * 3;
        canvas[idx] = color.R;
        canvas[idx + 1] = color.G;
        canvas[idx + 2] = color.B;
    }

    private static (int W, int H, byte[] Rgb) Render(TileGrid grid, int scale)
    {
        var w = grid.Width * scale;
        var h = grid.Height * scale;
        var rgb = new byte[w * h * 3];
        for (var ty = 0; ty < grid.Height; ty++)
            for (var tx = 0; tx < grid.Width; tx++)
            {
                var (r, g, b) = Color(grid[tx, ty]);
                for (var py = 0; py < scale; py++)
                    for (var px = 0; px < scale; px++)
                    {
                        var idx = ((ty * scale + py) * w + (tx * scale + px)) * 3;
                        rgb[idx] = r; rgb[idx + 1] = g; rgb[idx + 2] = b;
                    }
            }
        return (w, h, rgb);
    }
}
