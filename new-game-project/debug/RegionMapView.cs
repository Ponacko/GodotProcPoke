using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using ProcPoke.Overworld;

namespace ProcPoke.DebugView;

/// <summary>
/// Pan/zoom view of one generated region, stitched from the per-area Logical Tile grids the same way
/// <c>ProcPoke.MapGen</c>'s world PNG is (ticket 10): <see cref="WorldCanvas"/> owns the fixed-cell composition,
/// this only paints it. One texture is built per generated region and scaled at draw time, so zooming costs
/// nothing. Click an area to select it — <see cref="AreaSelected"/> carries the id to the detail panel.
/// </summary>
public partial class RegionMapView : Control
{
    private const float MinZoom = 0.15f;
    private const float MaxZoom = 24f;

    /// <summary>Drag distance (pixels) past which a left-press counts as a pan rather than a click.</summary>
    private const float ClickSlop = 4f;

    /// <summary>Rendered footprint width below which an area's name is dropped as unreadable clutter.</summary>
    private const float LabelMinWidth = 46f;

    [Signal]
    public delegate void AreaSelectedEventHandler(int areaId);

    /// <summary>One area's placement in the stitched map, in tile units.</summary>
    private sealed record Footprint(int AreaId, Rect2I Rect, bool OnCriticalPath, string Label);

    private GeneratedRegion? _region;
    private ImageTexture? _texture;
    private Vector2I _mapTiles;
    private readonly List<Footprint> _footprints = [];
    private readonly HashSet<int> _visitedAreaIds = [];
    private readonly HashSet<int> _flyTargetIds = [];
    private int _selectedAreaId = -1;

    private float _zoom = 1f;
    private Vector2 _pan;
    private bool _panning;
    private bool _panMoved;
    private Vector2 _pressedAt;

    /// <summary>True once the user has panned or zoomed — suppresses the automatic re-fit on resize, so a
    /// deliberate close-up survives the window changing size.</summary>
    private bool _viewAdjusted;

    public override void _Ready()
    {
        // The map texture is one pixel per tile; nearest keeps it crisp at every zoom step.
        TextureFilter = TextureFilterEnum.Nearest;
        Resized += OnResized;
    }

    /// <summary>Replaces the displayed region and opens it at the start town.</summary>
    public void ShowRegion(GeneratedRegion region)
    {
        _region = region;
        _selectedAreaId = -1;
        _visitedAreaIds.Clear();
        _visitedAreaIds.Add(region.Graph.StartAreaId);
        _flyTargetIds.Clear();
        BuildFootprints(region);
        _texture = ImageTexture.CreateFromImage(Stitch(region));
        ShowStart();
    }

    /// <summary>Updates session-owned progress without changing the generated region or stitched art.</summary>
    public void SetProgress(IReadOnlySet<int> visitedAreaIds, IReadOnlySet<int> flyTargetIds)
    {
        ArgumentNullException.ThrowIfNull(visitedAreaIds);
        ArgumentNullException.ThrowIfNull(flyTargetIds);
        _visitedAreaIds.Clear();
        _visitedAreaIds.UnionWith(visitedAreaIds);
        _flyTargetIds.Clear();
        _flyTargetIds.UnionWith(flyTargetIds);
        QueueRedraw();
    }

    public void MarkVisited(int areaId)
    {
        _visitedAreaIds.Add(areaId);
        QueueRedraw();
    }

    /// <summary>
    /// The opening view: legible tiles at the head of the Spine. Not <see cref="FitToView"/>, because a
    /// region is ~30 areas wide by three rows tall — fitted, every tile collapses to a smear.
    /// </summary>
    public void ShowStart()
    {
        if (_region is null) return;

        var start = _footprints.FirstOrDefault(f => f.AreaId == _region.Graph.StartAreaId);
        if (start is null || Size.Y < 1f) return;

        // Show the start area at about a third of the view height, so its neighbours are visible too.
        _zoom = Mathf.Clamp(Size.Y / (start.Rect.Size.Y * 3f), MinZoom, MaxZoom);
        _pan = new Vector2(
            48f - start.Rect.Position.X * _zoom,
            Size.Y / 2f - (start.Rect.Position.Y + start.Rect.Size.Y / 2f) * _zoom);
        _viewAdjusted = false;
        QueueRedraw();
    }

    public void FitToView()
    {
        if (_mapTiles == Vector2I.Zero || Size.X < 1f || Size.Y < 1f) return;

        const float margin = 20f;
        _zoom = Mathf.Clamp(
            Mathf.Min((Size.X - margin) / _mapTiles.X, (Size.Y - margin) / _mapTiles.Y), MinZoom, MaxZoom);
        _pan = (Size - (Vector2)_mapTiles * _zoom) / 2f;
        _viewAdjusted = true;
        QueueRedraw();
    }

    /// <summary>Centres and zooms onto one area — how the detail panel jumps to a selection.</summary>
    public void FocusArea(int areaId)
    {
        var footprint = _footprints.FirstOrDefault(f => f.AreaId == areaId);
        if (footprint is null || Size.X < 1f) return;

        const float padding = 10f;
        _zoom = Mathf.Clamp(
            Mathf.Min(Size.X / (footprint.Rect.Size.X + padding), Size.Y / (footprint.Rect.Size.Y + padding)),
            MinZoom, MaxZoom);
        var centre = (Vector2)footprint.Rect.Position + (Vector2)footprint.Rect.Size / 2f;
        _pan = Size / 2f - centre * _zoom;
        _viewAdjusted = true;
        QueueRedraw();
    }

    public void Select(int areaId)
    {
        _selectedAreaId = areaId;
        QueueRedraw();
        EmitSignal(SignalName.AreaSelected, areaId);
    }

    private void OnResized()
    {
        if (!_viewAdjusted) ShowStart();
    }

    // ── layout ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uses the domain-composed canvas directly. Area origins are cell origins, not centred footprints, so the
    /// view and the headless PNG cannot disagree about seams.
    /// </summary>
    private void BuildFootprints(GeneratedRegion region)
    {
        _footprints.Clear();

        var world = region.World;
        _mapTiles = new Vector2I(world.Grid.Width, world.Grid.Height);

        foreach (var area in region.Graph.Areas)
        {
            var grid = region.Carved[area.Id].Grid;
            var origin = region.World.OriginOf(area.Id);

            _footprints.Add(new Footprint(
                area.Id,
                new Rect2I(new Vector2I(origin.X, origin.Y), new Vector2I(grid.Width, grid.Height)),
                area.OnCriticalPath,
                LabelFor(region, area)));
        }
    }

    private static string LabelFor(GeneratedRegion region, Area area)
    {
        var name = region.Names.Of(area.Id);
        if (area.Id == region.Graph.StartAreaId) return $"★ {name}";
        if (area.Id == region.Graph.LeagueAreaId) return $"◆ {name}";
        return area.OnCriticalPath ? $"{area.PathIndex}. {name}" : name;
    }

    /// <summary>Blits the domain-composed world canvas into one RGB8 image at one pixel per tile.</summary>
    private Image Stitch(GeneratedRegion region)
    {
        var pixels = new byte[_mapTiles.X * _mapTiles.Y * 3];
        var grid = region.World.Grid;
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var colour = LogicalTilePalette.Of(grid[x, y]);
                var i = (y * _mapTiles.X + x) * 3;
                pixels[i] = (byte)colour.R8;
                pixels[i + 1] = (byte)colour.G8;
                pixels[i + 2] = (byte)colour.B8;
            }

        return Image.CreateFromData(_mapTiles.X, _mapTiles.Y, false, Image.Format.Rgb8, pixels);
    }

    // ── drawing ─────────────────────────────────────────────────────────────────────────────────────

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), LogicalTilePalette.Background);

        var font = GetThemeDefaultFont();
        if (_region is null || _texture is null)
        {
            DrawString(font, new Vector2(18f, 30f), "No region yet — press Generate.",
                HorizontalAlignment.Left, -1, 14, Colors.Gray);
            return;
        }

        DrawTextureRect(_texture, new Rect2(_pan, (Vector2)_mapTiles * _zoom), false);

        foreach (var footprint in _footprints)
        {
            var rect = ScreenRect(footprint.Rect);
            var visited = _visitedAreaIds.Contains(footprint.AreaId);
            if (!visited)
                DrawRect(rect, new Color(0f, 0f, 0f, 0.48f));
            DrawRect(rect, footprint.OnCriticalPath
                ? LogicalTilePalette.SpineOutline
                : LogicalTilePalette.BranchOutline, false, 1f);

            if (_flyTargetIds.Contains(footprint.AreaId))
                DrawRect(rect.Grow(2f), Colors.LightGreen, false, 2f);

            if (rect.Size.X >= LabelMinWidth)
                DrawString(font, rect.Position + new Vector2(1f, -3f), footprint.Label,
                    HorizontalAlignment.Left, -1, 11,
                    visited ? LogicalTilePalette.AreaLabel : Colors.DarkGray);
        }

        var selected = _footprints.FirstOrDefault(f => f.AreaId == _selectedAreaId);
        if (selected is not null)
            DrawRect(ScreenRect(selected.Rect).Grow(2f), LogicalTilePalette.Selection, false, 2f);
    }

    private Vector2 ToScreen(Vector2 tile) => _pan + tile * _zoom;

    private Vector2 ToTile(Vector2 screen) => (screen - _pan) / _zoom;

    private Rect2 ScreenRect(Rect2I tiles)
        => new(ToScreen((Vector2)tiles.Position), (Vector2)tiles.Size * _zoom);

    // ── input ───────────────────────────────────────────────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } up:
                ZoomAt(up.Position, 1.2f);
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } down:
                ZoomAt(down.Position, 1f / 1.2f);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Middle } button:
                if (button.Pressed)
                {
                    _panning = true;
                    _panMoved = false;
                    _pressedAt = button.Position;
                }
                else
                {
                    _panning = false;
                    // A left press that never travelled is a click, not a drag: select what is under it.
                    if (!_panMoved && button.ButtonIndex == MouseButton.Left) SelectAt(button.Position);
                }
                break;

            case InputEventMouseMotion motion when _panning:
                if (motion.Position.DistanceTo(_pressedAt) > ClickSlop) _panMoved = true;
                _pan += motion.Relative;
                _viewAdjusted = true;
                QueueRedraw();
                break;
        }
    }

    private void ZoomAt(Vector2 screen, float factor)
    {
        var anchor = ToTile(screen);
        _zoom = Mathf.Clamp(_zoom * factor, MinZoom, MaxZoom);
        // Keep the tile that was under the cursor under the cursor.
        _pan += screen - ToScreen(anchor);
        _viewAdjusted = true;
        QueueRedraw();
    }

    private void SelectAt(Vector2 screen)
    {
        var tile = ToTile(screen);
        var point = new Vector2I(Mathf.FloorToInt(tile.X), Mathf.FloorToInt(tile.Y));
        var hit = _footprints.FirstOrDefault(f => f.Rect.HasPoint(point));
        if (hit is not null) Select(hit.AreaId);
    }
}
