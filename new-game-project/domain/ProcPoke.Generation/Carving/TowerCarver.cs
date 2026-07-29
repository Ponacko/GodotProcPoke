using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a tower as stacked floor bands. Full-width wall rows with one alternating stair gap force a
/// zigzag climb while keeping the whole area in one ordinary tile-grid reachability graph.
/// </summary>
public static class TowerCarver
{
    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Wall);
        CarveKit.Border(grid, LogicalTile.Wall);
        for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++) grid[x, y] = LogicalTile.Ground;

        var floors = 3 + rng.NextInt(2);
        var top = 1;
        var bandHeights = floors == 4 ? new[] { 3, 3, 3, 2 } : new[] { 4, 4, 4 };
        var bands = new List<(int Y0, int Y1)>();
        var separators = new List<(int Y, int GapX)>();
        for (var i = 0; i < floors; i++)
        {
            var y1 = top + bandHeights[i] - 1;
            bands.Add((top, y1));
            top = y1 + 1;
            if (i == floors - 1) continue;

            var separatorY = top++;
            var fromBottom = floors - 2 - i;
            var gapX = fromBottom % 2 == 0 ? w - 3 : 2;
            for (var x = 1; x < w - 1; x++) grid[x, separatorY] = LogicalTile.Wall;
            grid[gapX, separatorY] = LogicalTile.Ground;
            separators.Add((separatorY, gapX));
        }

        var plannedEntrance = planned.FirstOrDefault();
        var edge = plannedEntrance?.Edge ?? EdgeSide.Bottom;
        var offset = plannedEntrance?.Offset ?? w / 2;
        var bottomBand = bands[^1];
        var spineY = (bottomBand.Y0 + bottomBand.Y1) / 2;
        var opening = AddEntrance(grid, edge, offset, spineY, separators, bands[0], bottomBand);

        var post = (X: w / 2, Y: bands[0].Y0 + (bands[0].Y1 - bands[0].Y0) / 2);
        grid[post.X, post.Y] = LogicalTile.TrainerPost;
        var item = (X: post.X + 1, Y: post.Y);
        grid[item.X, item.Y] = LogicalTile.ItemBall;

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = [opening] };
    }

    private static (int X, int Y) AddEntrance(
        TileGrid grid, EdgeSide edge, int offset, int spineY,
        IReadOnlyList<(int Y, int GapX)> separators,
        (int Y0, int Y1) topBand, (int Y0, int Y1) bottomBand)
    {
        var (w, h) = (grid.Width, grid.Height);
        var safeOffset = edge is EdgeSide.Left or EdgeSide.Right
            ? Math.Clamp(offset, 1, h - 2)
            : Math.Clamp(offset, 1, w - 2);

        if (edge != EdgeSide.Top)
        {
            var guarded = new HashSet<(int X, int Y)>();
            var opening = CarveKit.OpenSpineEdge(grid, guarded, edge, safeOffset, spineY, LogicalTile.Ground);
            return opening;
        }

        // A top entrance must descend through the prescribed gaps rather than punch extra holes in the
        // separator rows. The edge tile is still exactly where OpeningPlan placed it.
        var topOpening = (X: safeOffset, Y: 0);
        grid[topOpening.X, topOpening.Y] = LogicalTile.Warp;
        var currentX = safeOffset;
        var currentY = topBand.Y0;
        foreach (var separator in separators)
        {
            CarveKit.CarveCorridor(grid, currentX, currentY, separator.GapX, currentY, LogicalTile.Ground);
            grid[separator.GapX, separator.Y] = LogicalTile.Ground;
            currentX = separator.GapX;
            currentY = separator.Y + 1;
        }
        CarveKit.CarveCorridor(grid, currentX, currentY, w / 2, spineY, LogicalTile.Ground);
        return topOpening;
    }
}
