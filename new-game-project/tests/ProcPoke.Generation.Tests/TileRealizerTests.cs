using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Carving;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class TileRealizerTests
{
    [Fact]
    public void EveryLogicalTileHasALayerAndRemainsVisible()
    {
        var grid = new TileGrid(Enum.GetValues<LogicalTile>().Length, 1);
        for (var index = 0; index < grid.Width; index++)
            grid[index, 0] = (LogicalTile)index;

        var realized = TileRealizerModel.Realize(grid, Biome.Forest);

        Assert.Equal(grid.Width, realized.Tiles.Count());
        Assert.All(realized.Tiles, tile => Assert.True(Enum.IsDefined(tile.Layer)));
        Assert.Equal(Biome.Forest, realized.Biome);
        Assert.Equal(LogicalTile.NpcPost, realized[realized.Width - 1, 0].Logical);
    }

    [Theory]
    [InlineData(LogicalTile.Ground, RealizationLayer.Ground, TileCollision.None)]
    [InlineData(LogicalTile.TallGrass, RealizationLayer.Ground, TileCollision.None)]
    [InlineData(LogicalTile.Sand, RealizationLayer.Ground, TileCollision.None)]
    [InlineData(LogicalTile.Water, RealizationLayer.Water, TileCollision.Solid | TileCollision.SurfRequired)]
    [InlineData(LogicalTile.Wall, RealizationLayer.Terrain, TileCollision.Solid)]
    [InlineData(LogicalTile.Tree, RealizationLayer.Terrain, TileCollision.Solid)]
    [InlineData(LogicalTile.Ledge, RealizationLayer.Terrain, TileCollision.OneWaySouth)]
    [InlineData(LogicalTile.Boulder, RealizationLayer.Terrain, TileCollision.Solid | TileCollision.StrengthRequired)]
    [InlineData(LogicalTile.CutTree, RealizationLayer.Terrain, TileCollision.Solid | TileCollision.CutRequired)]
    [InlineData(LogicalTile.GateObstacle, RealizationLayer.Terrain, TileCollision.Solid | TileCollision.GateLocked)]
    [InlineData(LogicalTile.Warp, RealizationLayer.Markers, TileCollision.None)]
    [InlineData(LogicalTile.TrainerPost, RealizationLayer.Markers, TileCollision.None)]
    [InlineData(LogicalTile.ItemBall, RealizationLayer.Markers, TileCollision.None)]
    [InlineData(LogicalTile.NpcPost, RealizationLayer.Markers, TileCollision.None)]
    public void LogicalTileMapsToThePinnedLayerAndCollision(
        LogicalTile logical, RealizationLayer layer, TileCollision collision)
    {
        var grid = new TileGrid(1, 1);
        grid[0, 0] = logical;

        var realized = TileRealizerModel.Realize(grid, Biome.Cave)[0, 0];

        Assert.Equal(logical, realized.Logical);
        Assert.Equal(layer, realized.Layer);
        Assert.Equal(collision, realized.Collision);
    }

    [Fact]
    public void RealizationDoesNotMutateTheSourceGrid()
    {
        var grid = new TileGrid(3, 1);
        grid[0, 0] = LogicalTile.Wall;
        grid[1, 0] = LogicalTile.Warp;
        grid[2, 0] = LogicalTile.ItemBall;

        _ = TileRealizerModel.Realize(grid, Biome.Urban);

        Assert.Equal(LogicalTile.Wall, grid[0, 0]);
        Assert.Equal(LogicalTile.Warp, grid[1, 0]);
        Assert.Equal(LogicalTile.ItemBall, grid[2, 0]);
    }
}
