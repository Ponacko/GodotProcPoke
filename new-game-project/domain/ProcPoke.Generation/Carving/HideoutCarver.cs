using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a villain hideout as a connected 2×2 room complex. The top-right room is reserved for the
/// boss and its item; the other three rooms remain available for grunts and future decorations.
/// </summary>
public static class HideoutCarver
{
    private const int RoomWidth = 6;
    private const int RoomHeight = 4;
    // The fallback entrance is bottom-center (x = 16). Put that entrance in the bottom-left room so the
    // top-right boss room is also the BFS-farthest room, as required by the placement contract.
    private const int LeftX = 13;
    private const int RightX = LeftX + RoomWidth + 1;
    private const int TopY = 2;
    private const int BottomY = TopY + RoomHeight + 1;

    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Wall);
        CarveKit.Border(grid, LogicalTile.Wall);

        var topLeft = (X0: LeftX, Y0: TopY, X1: LeftX + RoomWidth - 1, Y1: TopY + RoomHeight - 1);
        var topRight = (X0: RightX, Y0: TopY, X1: RightX + RoomWidth - 1, Y1: TopY + RoomHeight - 1);
        var bottomLeft = (X0: LeftX, Y0: BottomY, X1: LeftX + RoomWidth - 1, Y1: BottomY + RoomHeight - 1);
        var bottomRight = (X0: RightX, Y0: BottomY, X1: RightX + RoomWidth - 1, Y1: BottomY + RoomHeight - 1);
        var rooms = new[] { topLeft, topRight, bottomLeft, bottomRight };

        foreach (var room in rooms)
            CarveKit.FillRect(grid, room.X0, room.Y0, room.X1, room.Y1, LogicalTile.Ground);

        // One-tile corridors between horizontal and vertical neighbours.
        CarveKit.CarveCorridor(grid, topLeft.X1, CenterY(topLeft), topRight.X0, CenterY(topRight), LogicalTile.Ground);
        CarveKit.CarveCorridor(grid, bottomLeft.X1, CenterY(bottomLeft), bottomRight.X0, CenterY(bottomRight), LogicalTile.Ground);
        CarveKit.CarveCorridor(grid, CenterX(topLeft), topLeft.Y1, CenterX(bottomLeft), bottomLeft.Y0, LogicalTile.Ground);
        CarveKit.CarveCorridor(grid, CenterX(topRight), topRight.Y1, CenterX(bottomRight), bottomRight.Y0, LogicalTile.Ground);

        var plannedEntrance = planned.FirstOrDefault();
        var entranceEdge = plannedEntrance?.Edge ?? EdgeSide.Bottom;
        var entranceOffset = plannedEntrance?.Offset ?? w / 2;
        var spineY = CenterY(bottomLeft);
        var guarded = new HashSet<(int X, int Y)>();
        var opening = CarveKit.OpenSpineEdge(
            grid, guarded, entranceEdge,
            entranceEdge is EdgeSide.Left or EdgeSide.Right
                ? Math.Clamp(entranceOffset, 1, h - 2)
                : Math.Clamp(entranceOffset, 1, w - 2),
            spineY, LogicalTile.Ground);
        var inside = (
            X: Math.Clamp(opening.X, 1, w - 2),
            Y: Math.Clamp(opening.Y, 1, h - 2));
        CarveKit.CarveCorridor(grid, inside.X, inside.Y, CenterX(bottomLeft), CenterY(bottomLeft), LogicalTile.Ground);

        // The boss occupies the top-right room; two of the remaining rooms receive grunt posts.
        grid[CenterX(topRight), CenterY(topRight)] = LogicalTile.TrainerPost;
        grid[CenterX(topRight) + 1, CenterY(topRight)] = LogicalTile.ItemBall;
        grid[CenterX(topLeft), CenterY(topLeft)] = LogicalTile.TrainerPost;
        grid[CenterX(bottomLeft), CenterY(bottomLeft)] = LogicalTile.TrainerPost;

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = [opening] };
    }

    private static int CenterX((int X0, int Y0, int X1, int Y1) room) => (room.X0 + room.X1) / 2;
    private static int CenterY((int X0, int Y0, int X1, int Y1) room) => (room.Y0 + room.Y1) / 2;
}
