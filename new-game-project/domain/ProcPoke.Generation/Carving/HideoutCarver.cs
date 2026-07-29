using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a villain hideout as a connected 2×2 room complex. The room diagonally opposite the entrance is
/// reserved for the boss and its item — being two doorways away, it is also the farthest room from the door,
/// which is the placement contract the loot depends on. The other three rooms hold grunt posts.
/// <para>
/// Laid out in the entrance's <see cref="CarveFrame"/>, so "near pair, far pair, boss diagonally opposite"
/// holds whichever border the entrance is on. The old fixed grid coordinates only worked for the bottom
/// entrance they were tuned for: entered from a side, the corridor from the door to the first room cut
/// through the walls between rooms and merged the complex into fewer than four.
/// </para>
/// </summary>
public static class HideoutCarver
{
    /// <summary>Room extent along the entrance border.</summary>
    private const int RoomAcross = 6;

    /// <summary>Room extent running away from the entrance border.</summary>
    private const int RoomDeep = 4;

    private readonly record struct Room(int U0, int V0, int U1, int V1)
    {
        public int CenterU => (U0 + U1) / 2;
        public int CenterV => (V0 + V1) / 2;
    }

    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var grid = new TileGrid(w, h, LogicalTile.Wall);
        CarveKit.Border(grid, LogicalTile.Wall);

        var entrance = planned.FirstOrDefault();
        var f = new CarveFrame(entrance?.Edge ?? EdgeSide.Bottom, w, h);

        // Two rooms across the border, two deep. Between the border and the near pair sit two lines: the lane
        // at u = U-2 that the entrance corridor runs along, and a wall at u = U-3 pierced by a single doorway.
        // The wall is what makes the boss room strictly the farthest — with the lane running directly against
        // the rooms it touched both of the near pair, and the loot was two steps from the door by either side.
        var acrossSpan = 2 * RoomAcross + 1;
        var v0 = Math.Max(1, (f.V - acrossSpan) / 2);
        var vFar = v0 + RoomAcross + 1;
        var laneU = f.U - 2;
        var doorU = f.U - 3;
        var nearU1 = doorU - 1;
        var nearU0 = nearU1 - RoomDeep + 1;
        var farU1 = nearU0 - 2;
        var farU0 = farU1 - RoomDeep + 1;

        var nearNear = new Room(nearU0, v0, nearU1, v0 + RoomAcross - 1);       // the entrance room
        var nearFar = new Room(nearU0, vFar, nearU1, vFar + RoomAcross - 1);
        var farNear = new Room(farU0, v0, farU1, v0 + RoomAcross - 1);
        var boss = new Room(farU0, vFar, farU1, vFar + RoomAcross - 1);         // two doorways from the door

        foreach (var room in new[] { nearNear, nearFar, farNear, boss })
            f.Fill(grid, room.U0, room.V0, room.U1, room.V1, LogicalTile.Ground);

        // One-tile doorways between neighbouring rooms.
        Corridor(grid, f, nearNear.CenterU, nearNear.V1, nearNear.CenterU, nearFar.V0);
        Corridor(grid, f, farNear.CenterU, farNear.V1, farNear.CenterU, boss.V0);
        Corridor(grid, f, nearNear.U0, nearNear.CenterV, farNear.U1, farNear.CenterV);
        Corridor(grid, f, nearFar.U0, nearFar.CenterV, boss.U1, boss.CenterV);

        // The entrance sits on the reference border, so its offset already is a canonical v. It runs along the
        // lane to the one doorway, and that doorway is the complex's only way in.
        var entranceV = f.InteriorV(entrance?.Offset ?? f.V / 2);
        var opening = f.Map(f.U - 1, entranceV);
        grid[opening.X, opening.Y] = LogicalTile.Warp;
        for (var v = Math.Min(entranceV, nearNear.CenterV); v <= Math.Max(entranceV, nearNear.CenterV); v++)
            f.Write(grid, laneU, v, LogicalTile.Ground);
        f.Write(grid, doorU, nearNear.CenterV, LogicalTile.Ground);

        f.Write(grid, boss.CenterU, boss.CenterV, LogicalTile.TrainerPost);
        f.Write(grid, boss.CenterU, boss.CenterV + 1, LogicalTile.ItemBall);
        f.Write(grid, farNear.CenterU, farNear.CenterV, LogicalTile.TrainerPost);
        f.Write(grid, nearNear.CenterU, nearNear.CenterV, LogicalTile.TrainerPost);

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = [opening] };
    }

    /// <summary>An L-shaped one-tile canonical corridor: along v first, then along u.</summary>
    private static void Corridor(TileGrid grid, CarveFrame f, int u0, int v0, int u1, int v1)
    {
        for (var v = Math.Min(v0, v1); v <= Math.Max(v0, v1); v++)
            if (f.Read(grid, u0, v) == LogicalTile.Wall) f.Write(grid, u0, v, LogicalTile.Ground);
        for (var u = Math.Min(u0, u1); u <= Math.Max(u0, u1); u++)
            if (f.Read(grid, u, v1) == LogicalTile.Wall) f.Write(grid, u, v1, LogicalTile.Ground);
    }
}
