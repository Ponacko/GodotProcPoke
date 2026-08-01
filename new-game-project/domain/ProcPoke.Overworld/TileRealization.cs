using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Carving;

namespace ProcPoke.Overworld;

/// <summary>The Godot layer a logical tile belongs to. Layers keep terrain, obstacles, and interactions independently replaceable.</summary>
public enum RealizationLayer
{
    Ground,
    Water,
    Terrain,
    Markers,
}

[Flags]
public enum TileCollision
{
    None = 0,
    Solid = 1 << 0,
    SurfRequired = 1 << 1,
    StrengthRequired = 1 << 2,
    CutRequired = 1 << 3,
    GateLocked = 1 << 4,
    OneWaySouth = 1 << 5,
}

/// <summary>One immutable presentation and collision decision for one logical tile.</summary>
public readonly record struct RealizedTile(
    LogicalTile Logical,
    RealizationLayer Layer,
    TileCollision Collision);

/// <summary>A realized view of a grid. The source grid remains owned by generation and is never changed.</summary>
public sealed class TileRealizationMap
{
    private readonly RealizedTile[] _tiles;

    internal TileRealizationMap(int width, int height, Biome biome, RealizedTile[] tiles)
    {
        Width = width;
        Height = height;
        Biome = biome;
        _tiles = tiles;
    }

    public int Width { get; }
    public int Height { get; }
    public Biome Biome { get; }

    public RealizedTile this[int x, int y] => _tiles[y * Width + x];

    public IEnumerable<RealizedTile> Tiles => _tiles;

    public int Count(RealizationLayer layer) => _tiles.Count(tile => tile.Layer == layer);
}

/// <summary>
/// Engine-independent Tile Realizer contract. It owns no art and makes no random choices; the Godot adapter
/// selects a debug palette for the returned biome/tile pairs.
/// </summary>
public static class TileRealizerModel
{
    public static TileRealizationMap Realize(TileGrid source, Biome biome)
    {
        ArgumentNullException.ThrowIfNull(source);
        var tiles = new RealizedTile[source.Width * source.Height];
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var logical = source[x, y];
            tiles[y * source.Width + x] = new RealizedTile(
                logical,
                LayerFor(logical),
                CollisionFor(logical));
        }

        return new TileRealizationMap(source.Width, source.Height, biome, tiles);
    }

    public static RealizationLayer LayerFor(LogicalTile tile) => tile switch
    {
        LogicalTile.Water => RealizationLayer.Water,
        LogicalTile.Wall or LogicalTile.Tree or LogicalTile.Ledge or LogicalTile.Boulder
            or LogicalTile.CutTree or LogicalTile.GateObstacle => RealizationLayer.Terrain,
        LogicalTile.Warp or LogicalTile.TrainerPost or LogicalTile.ItemBall or LogicalTile.NpcPost
            => RealizationLayer.Markers,
        _ => RealizationLayer.Ground,
    };

    public static TileCollision CollisionFor(LogicalTile tile) => tile switch
    {
        LogicalTile.Wall or LogicalTile.Tree => TileCollision.Solid,
        LogicalTile.Water => TileCollision.Solid | TileCollision.SurfRequired,
        LogicalTile.Boulder => TileCollision.Solid | TileCollision.StrengthRequired,
        LogicalTile.CutTree => TileCollision.Solid | TileCollision.CutRequired,
        LogicalTile.GateObstacle => TileCollision.Solid | TileCollision.GateLocked,
        LogicalTile.Ledge => TileCollision.OneWaySouth,
        _ => TileCollision.None,
    };
}
