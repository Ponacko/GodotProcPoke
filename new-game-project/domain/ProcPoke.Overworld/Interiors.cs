using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Overworld;

public enum InteriorKind
{
    Center,
    Mart,
    House,
    Gym,
    Cave,
    Tower,
    Hideout,
}

public enum InteriorTransitionKind
{
    Enter,
    Exit,
}

/// <summary>A fixed, reusable interior view. It is a TileGrid so the normal realizer and movement seams apply.</summary>
public sealed record InteriorTemplate
{
    public required InteriorKind Kind { get; init; }
    public required Biome Biome { get; init; }
    public required TileGrid Grid { get; init; }
    public required GridPosition Door { get; init; }
    public required GridPosition Arrival { get; init; }
}

public sealed record InteriorTransition(
    InteriorTransitionKind Kind,
    InteriorKind InteriorKind,
    int OverworldAreaId,
    GridPosition OverworldPosition,
    GridPosition InteriorPosition,
    Facing ArrivalFacing,
    InteriorTemplate Template);

/// <summary>Fixed templates for services and reusable generated-location interiors.</summary>
public static class InteriorTemplateCatalog
{
    public static InteriorTemplate For(BuildingKind kind) => kind switch
    {
        BuildingKind.Center => Build(InteriorKind.Center, Biome.Urban, 11, 8),
        BuildingKind.Mart => Build(InteriorKind.Mart, Biome.Urban, 9, 7),
        BuildingKind.Gym => Build(InteriorKind.Gym, Biome.Urban, 13, 9),
        _ => Build(InteriorKind.House, Biome.Urban, 7, 6),
    };

    public static InteriorTemplate For(AreaArchetype archetype) => archetype switch
    {
        AreaArchetype.Tower => Build(InteriorKind.Tower, Biome.Mountain, 11, 13),
        AreaArchetype.VillainHideout => Build(InteriorKind.Hideout, Biome.Urban, 15, 11),
        AreaArchetype.StandardCave or AreaArchetype.MountainPath or AreaArchetype.DeepCave
            or AreaArchetype.VictoryRoad => Build(InteriorKind.Cave, Biome.Cave, 13, 9),
        _ => throw new ArgumentOutOfRangeException(nameof(archetype), archetype,
            "the archetype has no interior template"),
    };

    private static InteriorTemplate Build(InteriorKind kind, Biome biome, int width, int height)
    {
        var grid = new TileGrid(width, height, LogicalTile.Wall);
        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
            grid[x, y] = LogicalTile.Ground;

        var door = new GridPosition(width / 2, height - 1);
        var arrival = new GridPosition(width / 2, height - 2);
        grid[door.X, door.Y] = LogicalTile.Warp;
        StampDecorations(grid, kind, width, height);
        return new InteriorTemplate { Kind = kind, Biome = biome, Grid = grid, Door = door, Arrival = arrival };
    }

    private static void StampDecorations(TileGrid grid, InteriorKind kind, int width, int height)
    {
        var center = (X: width / 2, Y: Math.Max(2, height / 3));
        switch (kind)
        {
            case InteriorKind.Center:
                grid[center.X, center.Y] = LogicalTile.NpcPost;
                grid[center.X, center.Y + 1] = LogicalTile.ItemBall;
                break;
            case InteriorKind.Mart:
                grid[center.X, center.Y] = LogicalTile.NpcPost;
                grid[Math.Max(1, center.X - 2), center.Y] = LogicalTile.ItemBall;
                break;
            case InteriorKind.Gym:
            case InteriorKind.Hideout:
                grid[center.X, center.Y] = LogicalTile.TrainerPost;
                grid[Math.Max(1, center.X - 2), center.Y + 2] = LogicalTile.TrainerPost;
                grid[Math.Min(width - 2, center.X + 2), center.Y + 2] = LogicalTile.TrainerPost;
                break;
            case InteriorKind.House:
                grid[center.X, center.Y] = LogicalTile.NpcPost;
                break;
            case InteriorKind.Cave:
            case InteriorKind.Tower:
                grid[center.X, center.Y] = LogicalTile.ItemBall;
                break;
        }
    }
}

/// <summary>Maps generated town doors into fixed interiors without adding movement special cases.</summary>
public static class InteriorTransitionResolver
{
    public static InteriorTransition? Enter(
        CarvedArea overworld,
        BuildingEntrance entrance,
        GridPosition position,
        Facing facing)
    {
        var streetFacing = FacingFor(entrance.StreetEdge);
        if (position != new GridPosition(entrance.X, entrance.Y) || facing != Opposite(streetFacing))
            return null;

        var template = InteriorTemplateCatalog.For(entrance.Kind);
        return new InteriorTransition(
            InteriorTransitionKind.Enter,
            template.Kind,
            overworld.AreaId,
            position,
            template.Arrival,
            Opposite(streetFacing),
            template);
    }

    public static InteriorTransition? Exit(
        int overworldAreaId,
        BuildingEntrance entrance,
        InteriorTemplate template,
        GridPosition position,
        Facing facing)
    {
        var streetFacing = FacingFor(entrance.StreetEdge);
        if (position != template.Arrival || facing != streetFacing)
            return null;

        return new InteriorTransition(
            InteriorTransitionKind.Exit,
            template.Kind,
            overworldAreaId,
            new GridPosition(entrance.X, entrance.Y),
            position,
            streetFacing,
            template);
    }

    public static Facing FacingFor(EdgeSide streetEdge) => streetEdge switch
    {
        EdgeSide.Left => Facing.West,
        EdgeSide.Right => Facing.East,
        EdgeSide.Top => Facing.North,
        EdgeSide.Bottom => Facing.South,
        _ => throw new ArgumentOutOfRangeException(nameof(streetEdge), streetEdge, null),
    };

    public static Facing Opposite(Facing facing) => facing switch
    {
        Facing.North => Facing.South,
        Facing.East => Facing.West,
        Facing.South => Facing.North,
        Facing.West => Facing.East,
        _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, null),
    };
}
