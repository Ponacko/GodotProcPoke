using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a settlement with an open central street, edge-aligned connections, and deterministic varied
/// building bands. Buildings face the street through one Warp door each, keeping every service reachable.
/// </summary>
public static class TownCarver
{
    private readonly record struct Building(int X, int Y, int Width, int Height);

    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Ground);
        var midY = h / 2;

        CarveKit.Border(grid, LogicalTile.Tree);

        var openings = new List<(int X, int Y)>();
        var buildingWalls = new HashSet<(int X, int Y)>();
        var buildingEntrances = new List<BuildingEntrance>();

        // Spine-first (ADR-0001). Only borders facing a neighbour are opened — a town on a corner of the
        // spine has two of its four sides against nothing, and punching those would open the street onto
        // blank space. A left/right warp is safe unguarded — the bands never reach column 1 or w-2 — but a
        // top/bottom warp lands on any interior column, so it needs a protected run into the street that the
        // bands then refuse to build over. Punching the border tile alone leaves a connection opening into
        // the back of a house, stranding it from the whole town.
        var guarded = new HashSet<(int X, int Y)>();
        foreach (var o in planned)
        {
            var tile = o.TileOn(w, h);
            grid[tile.X, tile.Y] = LogicalTile.Warp;
            openings.Add(tile);
            if (o.Edge is EdgeSide.Left or EdgeSide.Right) continue;
            CarveKit.ConnectSpineColumn(
                grid, guarded, tile.X, o.Edge == EdgeSide.Top ? 1 : h - 2, midY, LogicalTile.Ground);
        }

        // Two clear street rows keep the small StartTown footprint viable while leaving the central
        // spine row exactly where the original fixed-band carver placed it.
        var topY = 2;
        var bottomY = midY + 2;
        PlaceBand(grid, rng, topY, Math.Max(0, midY - topY - 1), doorOnBottom: true, gymFirst: true,
            openings: openings, buildingWalls: buildingWalls, buildingEntrances: buildingEntrances, guarded: guarded);
        PlaceBand(grid, rng, bottomY, Math.Max(0, h - 2 - bottomY + 1), doorOnBottom: false, gymFirst: false,
            openings: openings, buildingWalls: buildingWalls, buildingEntrances: buildingEntrances, guarded: guarded);

        return new CarvedArea
        {
            AreaId = area.Id,
            Grid = grid,
            Openings = openings,
            BuildingWallTiles = buildingWalls,
            BuildingEntrances = buildingEntrances,
        };
    }

    private static void PlaceBand(
        TileGrid grid, Pcg32 rng, int y0, int availableHeight, bool doorOnBottom, bool gymFirst,
        List<(int X, int Y)> openings, HashSet<(int X, int Y)> buildingWalls,
        List<BuildingEntrance> buildingEntrances,
        IReadOnlySet<(int X, int Y)> guarded)
    {
        if (availableHeight < 3) return;

        var count = 2 + rng.NextInt(3);
        var widths = Enumerable.Range(0, count).Select(_ => 3 + rng.NextInt(3)).ToArray();
        var heights = Enumerable.Range(0, count)
            .Select(_ => Math.Min(3 + rng.NextInt(2), availableHeight)).ToArray();
        if (gymFirst) widths[0] = widths.Max();
        FitWidths(widths, grid.Width - 4);

        var buildings = TryPlace(widths, heights, y0, rng, grid.Width);
        var placedIndex = 0;
        for (var buildingIndex = 0; buildingIndex < buildings.Count; buildingIndex++)
        {
            var building = buildings[buildingIndex];
            // The connection outranks the street furniture: a plot straddling an opening's approach column
            // is simply left empty, which reads as the road out of town rather than a sealed warp.
            if (Covers(building, guarded)) continue;

            CarveKit.FillRect(grid, building.X, building.Y, building.X + building.Width - 1,
                building.Y + building.Height - 1, LogicalTile.Wall);
            for (var y = building.Y; y < building.Y + building.Height; y++)
                for (var x = building.X; x < building.X + building.Width; x++) buildingWalls.Add((x, y));
            var doorX = building.X + building.Width / 2;
            var doorY = doorOnBottom ? building.Y + building.Height - 1 : building.Y;
            grid[doorX, doorY] = LogicalTile.Warp;
            buildingWalls.Remove((doorX, doorY));
            openings.Add((doorX, doorY));
            buildingEntrances.Add(new BuildingEntrance
            {
                Kind = KindFor(gymFirst, placedIndex++),
                X = doorX,
                Y = doorY,
                StreetEdge = doorOnBottom ? EdgeSide.Bottom : EdgeSide.Top,
            });
        }

        // A guarded approach can legitimately reject every candidate after the first one. Services are part
        // of the town contract, so reserve a compact second plot for the Mart rather than silently turning a
        // StartTown into a town with no shop.
        if (!gymFirst && !buildingEntrances.Any(entrance => entrance.Kind == BuildingKind.Mart))
            TryPlaceRequiredMart(grid, y0, availableHeight, openings, buildingWalls, buildingEntrances, guarded);
    }

    private static void TryPlaceRequiredMart(
        TileGrid grid, int y0, int availableHeight, List<(int X, int Y)> openings,
        HashSet<(int X, int Y)> buildingWalls, List<BuildingEntrance> buildingEntrances,
        IReadOnlySet<(int X, int Y)> guarded)
    {
        const int width = 3;
        const int height = 3;
        if (availableHeight < height) return;

        for (var x = 2; x <= grid.Width - width - 2; x++)
        {
            var covered = Enumerable.Range(x, width)
                .SelectMany(px => Enumerable.Range(y0, height).Select(py => (X: px, Y: py)));
            var cells = covered.ToArray();
            if (cells.Any(cell => guarded.Contains(cell) || buildingWalls.Contains(cell))) continue;

            foreach (var cell in cells)
            {
                grid[cell.X, cell.Y] = LogicalTile.Wall;
                buildingWalls.Add(cell);
            }

            var doorX = x + width / 2;
            var doorY = y0;
            grid[doorX, doorY] = LogicalTile.Warp;
            buildingWalls.Remove((doorX, doorY));
            openings.Add((doorX, doorY));
            buildingEntrances.Add(new BuildingEntrance
            {
                Kind = BuildingKind.Mart,
                X = doorX,
                Y = doorY,
                StreetEdge = EdgeSide.Top,
            });
            return;
        }
    }

    private static BuildingKind KindFor(bool gymFirst, int index)
        => gymFirst && index == 0
            ? BuildingKind.Gym
            : !gymFirst && index == 0
                ? BuildingKind.Center
                : !gymFirst && index == 1
                    ? BuildingKind.Mart
                    : BuildingKind.House;

    private static bool Covers(Building building, IReadOnlySet<(int X, int Y)> guarded)
    {
        for (var y = building.Y; y < building.Y + building.Height; y++)
            for (var x = building.X; x < building.X + building.Width; x++)
                if (guarded.Contains((x, y))) return true;
        return false;
    }

    private static void FitWidths(int[] widths, int usableWidth)
    {
        while (widths.Sum() + widths.Length - 1 > usableWidth)
        {
            var index = Enumerable.Range(0, widths.Length)
                .OrderByDescending(i => widths[i])
                .FirstOrDefault(i => widths[i] > 3);
            if (widths[index] <= 3) break;
            widths[index]--;
        }
    }

    private static IReadOnlyList<Building> TryPlace(
        IReadOnlyList<int> widths, IReadOnlyList<int> heights, int y0, Pcg32 rng, int gridWidth)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var x = 2 + rng.NextInt(3);
            var buildings = new List<Building>();
            for (var i = 0; i < widths.Count; i++)
            {
                var building = new Building(x, y0, widths[i], heights[i]);
                if (building.X + building.Width > gridWidth - 2) break;
                buildings.Add(building);
                x += building.Width + 1 + rng.NextInt(3);
            }
            if (buildings.Count == widths.Count) return buildings;
        }

        // The compact fallback is deterministic and always fits after FitWidths; random retries still
        // provide the intended per-seed layout variation on the normal path.
        var fallback = new List<Building>();
        var compactX = 2;
        for (var i = 0; i < widths.Count; i++)
        {
            fallback.Add(new Building(compactX, y0, widths[i], heights[i]));
            compactX += widths[i] + 1;
        }
        return fallback;
    }
}
