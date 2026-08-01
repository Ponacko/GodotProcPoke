using System;
using Godot;
using ProcPoke.Generation.Carving;
using ProcPoke.Overworld;

namespace ProcPoke.OverworldView;

/// <summary>Camera adapter bounded to the currently realized area's tile rectangle.</summary>
public partial class GridCamera : Camera2D
{
    [Export] public int TilePixels { get; set; } = 16;

    private int _mapWidth;
    private int _mapHeight;

    public override void _Ready()
    {
        PositionSmoothingEnabled = false;
        Enabled = true;
    }

    public void Configure(TileGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        Configure(grid.Width, grid.Height);
    }

    public void Configure(int mapWidth, int mapHeight)
    {
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        LimitLeft = 0;
        LimitTop = 0;
        LimitRight = mapWidth * TilePixels;
        LimitBottom = mapHeight * TilePixels;
    }

    public void Follow(GridPosition player)
    {
        var viewportTiles = GetViewportRect().Size / TilePixels;
        var center = CameraBounds.Clamp(player, _mapWidth, _mapHeight, viewportTiles.X, viewportTiles.Y);
        GlobalPosition = new Vector2(center.X * TilePixels, center.Y * TilePixels);
    }
}
