using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>The origin of one area's uniform lattice cell in the composed world canvas.</summary>
public readonly record struct WorldOrigin(int X, int Y);

/// <summary>
/// The composed world map and the placement facts needed by debug renderers and later overworld code.
/// Coordinates are normalized so the minimum topology column and row map to zero.
/// </summary>
public sealed record WorldCanvasMap
{
    public required TileGrid Grid { get; init; }
    public required IReadOnlyDictionary<int, WorldOrigin> Origins { get; init; }
    public required int CellWidth { get; init; }
    public required int CellHeight { get; init; }
    public required int Columns { get; init; }
    public required int Rows { get; init; }
    public required IReadOnlySet<(int X, int Y)> GateTiles { get; init; }

    public WorldOrigin OriginOf(int areaId)
        => Origins.TryGetValue(areaId, out var origin)
            ? origin
            : throw new KeyNotFoundException($"world canvas has no origin for area {areaId}");
}

/// <summary>
/// Composes carved area maps into the topology's lattice. The pass adds no new random choices: geography
/// comes from the already-generated biome and tile maps, so the world canvas cannot perturb generation.
/// </summary>
public static class WorldCanvas
{
    public static WorldCanvasMap Compose(
        RegionGraph graph, BiomeMap biomes, IReadOnlyDictionary<int, CarvedArea> carved, OpeningPlan openings)
    {
        if (graph.Areas.Any(area => !carved.ContainsKey(area.Id)))
            throw new InvalidOperationException("world canvas cannot compose a region with missing carved areas");

        var cells = OverviewLayout.Plan(graph).ToDictionary(cell => cell.AreaId);
        var minCol = cells.Values.Min(cell => cell.Col);
        var maxCol = cells.Values.Max(cell => cell.Col);
        var minRow = cells.Values.Min(cell => cell.Row);
        var maxRow = cells.Values.Max(cell => cell.Row);
        var cellWidth = carved.Values.Max(area => area.Grid.Width);
        var cellHeight = carved.Values.Max(area => area.Grid.Height);
        var columns = maxCol - minCol + 1;
        var rows = maxRow - minRow + 1;
        var grid = new TileGrid(columns * cellWidth, rows * cellHeight);
        var origins = new Dictionary<int, WorldOrigin>(graph.Areas.Count);
        var worldGateTiles = new HashSet<(int X, int Y)>();
        var occupied = new HashSet<(int X, int Y)>();

        FillCells(grid, graph, cells, biomes, minCol, minRow, cellWidth, cellHeight);

        foreach (var area in graph.Areas.OrderBy(area => area.Id))
        {
            var cell = cells[area.Id];
            var origin = new WorldOrigin((cell.Col - minCol) * cellWidth, (cell.Row - minRow) * cellHeight);
            origins.Add(area.Id, origin);
            CopyArea(grid, carved[area.Id].Grid, origin);
            for (var y = 0; y < carved[area.Id].Grid.Height; y++)
                for (var x = 0; x < carved[area.Id].Grid.Width; x++)
                    occupied.Add((origin.X + x, origin.Y + y));
            foreach (var (x, y) in carved[area.Id].GateTiles)
                worldGateTiles.Add((origin.X + x, origin.Y + y));
        }

        var seamWarps = CollapseSeams(grid, graph, origins, carved, openings, cellWidth, cellHeight);
        SealCellBoundaries(grid, seamWarps, occupied, cellWidth, cellHeight);

        return new WorldCanvasMap
        {
            Grid = grid,
            Origins = origins,
            CellWidth = cellWidth,
            CellHeight = cellHeight,
            Columns = columns,
            Rows = rows,
            GateTiles = worldGateTiles,
        };
    }

    private static void FillCells(
        TileGrid grid, RegionGraph graph, IReadOnlyDictionary<int, OverviewCell> cells, BiomeMap biomes,
        int minCol, int minRow, int cellWidth, int cellHeight)
    {
        var areas = graph.Areas.ToList();
        var byCell = cells.ToDictionary(pair => (pair.Value.Col, pair.Value.Row), pair => pair.Key);
        var maxCol = cells.Values.Max(cell => cell.Col);
        var maxRow = cells.Values.Max(cell => cell.Row);

        for (var row = minRow; row <= maxRow; row++)
        for (var col = minCol; col <= maxCol; col++)
        {
            var areaId = byCell.TryGetValue((col, row), out var occupied)
                ? occupied
                : NearestArea(areas, col, row, cells).Id;
            var filler = FillerFor(biomes.Of(areaId));
            var x0 = (col - minCol) * cellWidth;
            var y0 = (row - minRow) * cellHeight;
            for (var y = y0; y < y0 + cellHeight; y++)
                for (var x = x0; x < x0 + cellWidth; x++)
                    grid[x, y] = filler;
        }
    }

    private static Area NearestArea(
        IReadOnlyList<Area> areas, int col, int row, IReadOnlyDictionary<int, OverviewCell> cells)
        => areas
            .OrderBy(area => Math.Abs(cells[area.Id].Col - col) + Math.Abs(cells[area.Id].Row - row))
            .ThenBy(area => area.Id)
            .First();

    private static LogicalTile FillerFor(Biome biome) => biome switch
    {
        Biome.Forest or Biome.Grassland => LogicalTile.Tree,
        Biome.Mountain or Biome.Cave or Biome.Urban => LogicalTile.Wall,
        Biome.Desert => LogicalTile.Sand,
        Biome.Water => LogicalTile.Water,
        _ => LogicalTile.Wall,
    };

    private static void CopyArea(TileGrid destination, TileGrid source, WorldOrigin origin)
    {
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                destination[origin.X + x, origin.Y + y] = source[x, y];
    }

    private static IReadOnlySet<(int X, int Y)> CollapseSeams(
        TileGrid world, RegionGraph graph,
        IReadOnlyDictionary<int, WorldOrigin> origins, IReadOnlyDictionary<int, CarvedArea> carved,
        OpeningPlan openings, int cellWidth, int cellHeight)
    {
        var seamWarps = new HashSet<(int X, int Y)>();
        foreach (var connection in graph.Connections
                     .OrderBy(connection => Math.Min(connection.AreaA, connection.AreaB))
                     .ThenBy(connection => Math.Max(connection.AreaA, connection.AreaB)))
        {
            var a = graph[connection.AreaA];
            var b = graph[connection.AreaB];
            var openingA = openings.EdgesOf(a.Id).FirstOrDefault(opening => opening.NeighborAreaId == b.Id);
            var openingB = openings.EdgesOf(b.Id).FirstOrDefault(opening => opening.NeighborAreaId == a.Id);
            if (openingA is null || openingB is null)
                throw new InvalidOperationException(
                    $"connection {a.Id}↔{b.Id} has no paired world-canvas opening");

            var localA = openingA.TileOn(carved[a.Id].Grid.Width, carved[a.Id].Grid.Height);
            var localB = openingB.TileOn(carved[b.Id].Grid.Width, carved[b.Id].Grid.Height);
            if (carved[a.Id].Grid[localA.X, localA.Y] != LogicalTile.Warp ||
                carved[b.Id].Grid[localB.X, localB.Y] != LogicalTile.Warp)
                // Some enclosed carvers intentionally expose only their entrance even when topology later
                // gives the area a second Warp connection. There is no honest seam to collapse in that case;
                // leave the cell boundary filled, exactly as the contract requires for an unmatched opening.
                continue;

            var originA = origins[a.Id];
            var originB = origins[b.Id];
            var globalA = (X: originA.X + localA.X, Y: originA.Y + localA.Y);
            var globalB = (X: originB.X + localB.X, Y: originB.Y + localB.Y);
            ConnectToCellBoundary(world, globalA, BoundaryFor(a, b, originA, cellWidth, cellHeight, openingA.Offset));
            ConnectToCellBoundary(world, globalB, BoundaryFor(b, a, originB, cellWidth, cellHeight, openingB.Offset));
            seamWarps.Add(BoundaryFor(a, b, originA, cellWidth, cellHeight, openingA.Offset));
            seamWarps.Add(BoundaryFor(b, a, originB, cellWidth, cellHeight, openingB.Offset));
        }

        return seamWarps;
    }

    private static void SealCellBoundaries(
        TileGrid world, IReadOnlySet<(int X, int Y)> seamWarps, IReadOnlySet<(int X, int Y)> occupied,
        int cellWidth, int cellHeight)
    {
        for (var y = 0; y < world.Height; y++)
        for (var x = 0; x < world.Width; x++)
        {
            var onBoundary = x % cellWidth == 0 || x % cellWidth == cellWidth - 1
                || y % cellHeight == 0 || y % cellHeight == cellHeight - 1;
            if (onBoundary && !seamWarps.Contains((x, y))
                && (!occupied.Contains((x, y)) || world[x, y] == LogicalTile.Warp))
                world[x, y] = LogicalTile.Wall;
        }
    }

    private static (int X, int Y) BoundaryFor(
        Area area, Area neighbor, WorldOrigin origin, int cellWidth, int cellHeight, int offset)
    {
        var heading = area.Cell.HeadingTo(neighbor.Cell);
        return heading switch
        {
            Heading.East => (origin.X + cellWidth - 1, origin.Y + offset),
            Heading.West => (origin.X, origin.Y + offset),
            Heading.South => (origin.X + offset, origin.Y + cellHeight - 1),
            _ => (origin.X + offset, origin.Y),
        };
    }

    private static void ConnectToCellBoundary(
        TileGrid world, (int X, int Y) from, (int X, int Y) boundary)
    {
        var x = from.X;
        var y = from.Y;
        while ((x, y) != boundary)
        {
            if (x != boundary.X) x += Math.Sign(boundary.X - x);
            if (y != boundary.Y) y += Math.Sign(boundary.Y - y);
            if ((x, y) != boundary) world[x, y] = LogicalTile.Ground;
        }

        world[boundary.X, boundary.Y] = LogicalTile.Warp;
    }
}
