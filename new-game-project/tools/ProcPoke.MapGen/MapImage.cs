using ProcPoke.Generation.Carving;

namespace ProcPoke.MapGen;

/// <summary>Renders carved tile grids to PNG images: one per area, plus a stacked critical-path overview.</summary>
internal static class MapImage
{
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
        _ => (0, 0, 0),
    };

    public static void SaveArea(string path, CarvedArea area, int scale = 8)
    {
        var (w, h, rgb) = Render(area.Grid, scale);
        Png.WriteRgb(path, w, h, rgb);
    }

    /// <summary>Stacks areas vertically into one image, top to bottom, on a dark background with gaps.</summary>
    public static void SaveOverview(string path, IReadOnlyList<CarvedArea> areas, int scale = 6)
    {
        const int gap = 8;
        var maxW = areas.Max(a => a.Grid.Width) * scale;
        var totalH = areas.Sum(a => a.Grid.Height * scale + gap) + gap;
        var canvas = new byte[maxW * totalH * 3];
        for (var i = 0; i < canvas.Length; i += 3) { canvas[i] = 24; canvas[i + 1] = 24; canvas[i + 2] = 28; }

        var y0 = gap;
        foreach (var area in areas)
        {
            var (w, h, rgb) = Render(area.Grid, scale);
            for (var y = 0; y < h; y++)
                Array.Copy(rgb, y * w * 3, canvas, ((y0 + y) * maxW + 0) * 3, w * 3);
            y0 += h + gap;
        }

        Png.WriteRgb(path, maxW, totalH, canvas);
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
