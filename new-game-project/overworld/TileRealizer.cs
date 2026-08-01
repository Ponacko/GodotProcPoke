using System;
using System.Linq;
using Godot;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Carving;
using ProcPoke.Overworld;

namespace ProcPoke.OverworldView;

/// <summary>
/// Godot adapter for the Phase 3 Tile Realizer. It creates a debug atlas with one biome row per logical
/// tile vocabulary, then fills independent ground, water, terrain, and marker TileMapLayers. Collision is
/// authored on the atlas TileData, so the rendered tile and its physics cannot drift apart.
/// </summary>
public partial class TileRealizer : Node2D
{
    private const int AtlasSourceId = 0;
    private const int TilePixels = 16;

    private TileMapLayer _groundLayer = null!;
    private TileMapLayer _waterLayer = null!;
    private TileMapLayer _terrainLayer = null!;
    private TileMapLayer _markerLayer = null!;
    private TileSet _tileSet = null!;

    public TileMapLayer GroundLayer => _groundLayer;
    public TileMapLayer WaterLayer => _waterLayer;
    public TileMapLayer TerrainLayer => _terrainLayer;
    public TileMapLayer MarkerLayer => _markerLayer;
    public TileRealizationMap? LastRealization { get; private set; }

    public override void _Ready()
    {
        EnsureLayers();
        _tileSet = BuildTileSet();
        AssignTileSet();
    }

    /// <summary>Realizes one generated area without changing its source grid or any generation state.</summary>
    public TileRealizationMap Realize(CarvedArea area, Biome biome)
    {
        ArgumentNullException.ThrowIfNull(area);
        return Realize(area.Grid, biome);
    }

    public TileRealizationMap Realize(TileGrid grid, Biome biome)
    {
        ArgumentNullException.ThrowIfNull(grid);
        EnsureLayers();
        if (_tileSet is null)
        {
            _tileSet = BuildTileSet();
            AssignTileSet();
        }

        var realization = TileRealizerModel.Realize(grid, biome);
        ClearLayers();
        foreach (var tile in realization.Tiles.Select((value, index) => (value, index)))
        {
            var x = tile.index % realization.Width;
            var y = tile.index / realization.Width;
            LayerFor(tile.value.Layer).SetCell(
                new Vector2I(x, y), AtlasSourceId,
                new Vector2I((int)tile.value.Logical, (int)biome), 0);
        }

        _groundLayer.UpdateInternals();
        _waterLayer.UpdateInternals();
        _terrainLayer.UpdateInternals();
        _markerLayer.UpdateInternals();
        LastRealization = realization;
        return realization;
    }

    public void Clear()
    {
        EnsureLayers();
        ClearLayers();
        LastRealization = null;
    }

    private void EnsureLayers()
    {
        _groundLayer = GetNodeOrNull<TileMapLayer>("GroundLayer") ?? AddLayer("GroundLayer");
        _waterLayer = GetNodeOrNull<TileMapLayer>("WaterLayer") ?? AddLayer("WaterLayer");
        _terrainLayer = GetNodeOrNull<TileMapLayer>("TerrainLayer") ?? AddLayer("TerrainLayer");
        _markerLayer = GetNodeOrNull<TileMapLayer>("MarkerLayer") ?? AddLayer("MarkerLayer");

        _groundLayer.CollisionEnabled = false;
        _waterLayer.CollisionEnabled = true;
        _terrainLayer.CollisionEnabled = true;
        _markerLayer.CollisionEnabled = false;
    }

    private TileMapLayer AddLayer(string name)
    {
        var layer = new TileMapLayer { Name = name };
        AddChild(layer);
        return layer;
    }

    private void AssignTileSet()
    {
        _groundLayer.TileSet = _tileSet;
        _waterLayer.TileSet = _tileSet;
        _terrainLayer.TileSet = _tileSet;
        _markerLayer.TileSet = _tileSet;
    }

    private void ClearLayers()
    {
        _groundLayer.Clear();
        _waterLayer.Clear();
        _terrainLayer.Clear();
        _markerLayer.Clear();
    }

    private TileMapLayer LayerFor(RealizationLayer layer) => layer switch
    {
        RealizationLayer.Ground => _groundLayer,
        RealizationLayer.Water => _waterLayer,
        RealizationLayer.Terrain => _terrainLayer,
        RealizationLayer.Markers => _markerLayer,
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null),
    };

    private static TileSet BuildTileSet()
    {
        var logicalTiles = Enum.GetValues<LogicalTile>();
        var biomes = Enum.GetValues<Biome>();
        var atlasImage = Image.CreateEmpty(logicalTiles.Length * TilePixels, biomes.Length * TilePixels,
            false, Image.Format.Rgba8);
        atlasImage.Fill(Colors.Transparent);

        foreach (var biome in biomes)
        foreach (var logical in logicalTiles)
            PaintTile(atlasImage, (int)logical, (int)biome, biome, logical);

        var atlas = new TileSetAtlasSource
        {
            Texture = ImageTexture.CreateFromImage(atlasImage),
            TextureRegionSize = new Vector2I(TilePixels, TilePixels),
        };
        var tileSet = new TileSet
        {
            TileSize = new Vector2I(TilePixels, TilePixels),
        };
        tileSet.AddPhysicsLayer(-1);
        tileSet.SetPhysicsLayerCollisionLayer(0, 1);
        tileSet.SetPhysicsLayerCollisionMask(0, 1);

        foreach (var biome in biomes)
        foreach (var logical in logicalTiles)
        {
            var coords = new Vector2I((int)logical, (int)biome);
            atlas.CreateTile(coords);
            var collision = TileRealizerModel.CollisionFor(logical);
            if (collision == TileCollision.None) continue;

            var data = atlas.GetTileData(coords, 0);
            data.SetCollisionPolygonsCount(0, 1);
            data.SetCollisionPolygonPoints(0, 0, FullTilePolygon());
            data.SetCollisionPolygonOneWay(0, 0, collision.HasFlag(TileCollision.OneWaySouth));
        }

        tileSet.AddSource(atlas, AtlasSourceId);
        return tileSet;
    }

    private static Vector2[] FullTilePolygon() =>
    [
        new Vector2(-TilePixels / 2f, -TilePixels / 2f),
        new Vector2(TilePixels / 2f, -TilePixels / 2f),
        new Vector2(TilePixels / 2f, TilePixels / 2f),
        new Vector2(-TilePixels / 2f, TilePixels / 2f),
    ];

    private static void PaintTile(Image image, int column, int row, Biome biome, LogicalTile logical)
    {
        var origin = new Vector2I(column * TilePixels, row * TilePixels);
        var rect = new Rect2I(origin, new Vector2I(TilePixels, TilePixels));
        var colour = TileRealizerPalette.For(biome, logical);
        image.FillRect(rect, colour);

        var ink = TileRealizerPalette.Ink(biome);
        if (logical is LogicalTile.Wall or LogicalTile.Tree or LogicalTile.Boulder or LogicalTile.CutTree
            or LogicalTile.GateObstacle or LogicalTile.Ledge)
            image.FillRect(new Rect2I(origin + new Vector2I(1, 1), new Vector2I(TilePixels - 2, 2)), ink);

        var marker = logical switch
        {
            LogicalTile.Warp => Colors.White,
            LogicalTile.TrainerPost => Colors.White,
            LogicalTile.ItemBall => Colors.White,
            LogicalTile.NpcPost => Colors.White,
            _ => Colors.Transparent,
        };
        if (marker == Colors.Transparent) return;

        image.FillRect(new Rect2I(origin + new Vector2I(6, 4), new Vector2I(4, 8)), marker);
        image.FillRect(new Rect2I(origin + new Vector2I(4, 6), new Vector2I(8, 4)), marker);
    }
}
