using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves the League as a linear E4 gauntlet: four full-height chambers followed by a throne room.
/// The separating walls leave one doorway on the spine row each, so the Champion cannot be reached by
/// walking around the Elite Four rooms.
/// </summary>
public static class LeagueCarver
{
    private const int ChamberWidth = 6;
    private const int ThroneWidth = 8;
    private const int GapWidth = 1;
    private const int Margin = 2;

    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Wall);
        CarveKit.Border(grid, LogicalTile.Wall);

        var chambers = new List<(int X0, int X1)>();
        var x = Margin;
        for (var i = 0; i < 4; i++)
        {
            chambers.Add((x, x + ChamberWidth - 1));
            x += ChamberWidth + GapWidth;
        }

        var throne = (X0: x, X1: x + ThroneWidth - 1);
        if (throne.X1 > w - Margin - 1)
            throw new InvalidOperationException("League requires the large carving footprint");

        var usableTop = 1;
        var usableBottom = h - 2;
        foreach (var chamber in chambers)
            CarveKit.FillRect(grid, chamber.X0, usableTop, chamber.X1, usableBottom, LogicalTile.Ground);
        CarveKit.FillRect(grid, throne.X0, usableTop, throne.X1, usableBottom, LogicalTile.Ground);

        var plannedEntrance = planned.FirstOrDefault(o => o.Edge == EdgeSide.Left);
        var entranceOffset = plannedEntrance?.Offset ?? h / 2;
        // Keep the internal spine away from the one-tile outer frame. The edge opening remains at its
        // planned coordinate; OpenSpineEdge links it vertically to this safe chamber row when needed.
        // Leave one wall row above and below the spine even when a planned branch sits on a separator;
        // that keeps every separator's doorway recognisable by its north/south wall signature.
        var spineY = Math.Clamp(entranceOffset, 3, h - 4);
        var guarded = new HashSet<(int X, int Y)>();
        var openings = new List<(int X, int Y)>();
        var separatorXs = chambers.Select(chamber => chamber.X1 + 1).ToHashSet();

        AddOpening(grid, guarded, openings, EdgeSide.Left, entranceOffset, spineY);
        CarveKit.CarveCorridor(grid, 1, spineY, chambers[0].X0, spineY, LogicalTile.Ground);

        // Four separator columns remain walls except for their one-wide spine doors. A door's north and
        // south neighbours therefore stay walls, making the gauntlet structure mechanically checkable.
        foreach (var chamber in chambers)
        {
            var separatorX = chamber.X1 + 1;
            grid[separatorX, spineY] = LogicalTile.Ground;
        }
        CarveKit.CarveCorridor(grid, chambers[^1].X1, spineY, throne.X0, spineY, LogicalTile.Ground);

        // League is normally reached by a Warp from Victory Road, but a loop-back or direct seamless
        // connection can still add planned openings. Preserve every planned edge and connect it to the
        // internal spine without creating a second route around the four separator doors.
        foreach (var opening in planned)
        {
            var tile = opening.TileOn(w, h);
            if (openings.Contains(tile)) continue;
            if (opening.Edge is EdgeSide.Top or EdgeSide.Bottom && separatorXs.Contains(opening.Offset))
                AddSeparatorBranch(grid, openings, opening.Edge, opening.Offset, spineY, chambers);
            else
                AddOpening(grid, guarded, openings, opening.Edge, opening.Offset, spineY);
        }
        // A planned right-edge opening is not part of the normal League entrance, so bridge the final
        // interior column to the throne room just as the left entrance is bridged to chamber one.
        CarveKit.CarveCorridor(grid, throne.X1, spineY, w - 2, spineY, LogicalTile.Ground);

        // Stamp posts last: a top/bottom opening can share a column with a chamber centre, and its
        // protected stub must not erase the boss marker.
        var posts = chambers
            .Select(chamber => (X: (chamber.X0 + chamber.X1) / 2, Y: spineY))
            .Append((X: (throne.X0 + throne.X1) / 2, Y: spineY));
        foreach (var post in posts) grid[post.X, post.Y] = LogicalTile.TrainerPost;

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = openings };
    }

    private static void AddOpening(
        TileGrid grid, HashSet<(int X, int Y)> guarded, List<(int X, int Y)> openings,
        EdgeSide edge, int offset, int spineY)
    {
        var safeOffset = edge is EdgeSide.Left or EdgeSide.Right
            ? Math.Clamp(offset, 1, grid.Height - 2)
            : Math.Clamp(offset, 1, grid.Width - 2);
        var tile = CarveKit.OpenSpineEdge(grid, guarded, edge, safeOffset, spineY, LogicalTile.Ground);
        if (!openings.Contains(tile)) openings.Add(tile);
    }

    private static void AddSeparatorBranch(
        TileGrid grid, List<(int X, int Y)> openings, EdgeSide edge, int offset, int spineY,
        IReadOnlyList<(int X0, int X1)> chambers)
    {
        var edgeY = edge == EdgeSide.Top ? 0 : grid.Height - 1;
        var innerY = edge == EdgeSide.Top ? 1 : grid.Height - 2;
        var tile = (X: Math.Clamp(offset, 1, grid.Width - 2), Y: edgeY);
        grid[tile.X, tile.Y] = LogicalTile.Warp;
        grid[tile.X, innerY] = LogicalTile.Ground;

        var chamber = chambers.Single(chamber => chamber.X1 + 1 == tile.X);
        // Enter the chamber from the separator's edge row, then climb inside the chamber to the spine.
        CarveKit.CarveCorridor(grid, tile.X, innerY, chamber.X1, innerY, LogicalTile.Ground);
        CarveKit.CarveCorridor(grid, chamber.X1, innerY, chamber.X1, spineY, LogicalTile.Ground);
        openings.Add(tile);
    }
}
