using System;
using Godot;
using ProcPoke.Generation.Carving;
using ProcPoke.Overworld;

namespace ProcPoke.OverworldView;

/// <summary>Presentation-only darkness mask; the radius and Flash rules live in the pure domain model.</summary>
public partial class DarkCaveVisionOverlay : Node2D
{
    [Export] public int TilePixels { get; set; } = 16;
    [Export] public Color Darkness { get; set; } = new(0, 0, 0, 0.82f);

    public TileGrid? Grid { get; private set; }
    public GridPosition Player { get; private set; }
    public VisionState State { get; private set; }

    public void Configure(TileGrid grid, GridPosition player, VisionState state)
    {
        ArgumentNullException.ThrowIfNull(grid);
        Grid = grid;
        Player = player;
        State = state;
        QueueRedraw();
    }

    public void UpdatePlayer(GridPosition player)
    {
        Player = player;
        QueueRedraw();
    }

    public void UpdateVision(VisionState state)
    {
        State = state;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Grid is null || !State.IsDark) return;

        for (var y = 0; y < Grid.Height; y++)
        for (var x = 0; x < Grid.Width; x++)
        {
            var tile = new GridPosition(x, y);
            if (DarkCaveVision.IsVisible(State, Player, tile)) continue;
            DrawRect(new Rect2(x * TilePixels, y * TilePixels, TilePixels, TilePixels), Darkness);
        }
    }
}
