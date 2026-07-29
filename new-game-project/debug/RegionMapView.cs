using System.Collections.Generic;
using System.Linq;
using Godot;
using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;

namespace ProcPoke.DebugView;

/// <summary>
/// Pan/zoom view of one generated region, stitched from the per-area Logical Tile grids the same way
/// <c>ProcPoke.MapGen</c>'s overview PNG is (ticket 2b): <see cref="OverviewLayout"/> decides the cells, this
/// only paints them. One texture is built per generated region and scaled at draw time, so zooming costs
/// nothing. Click an area to select it — <see cref="AreaSelected"/> carries the id to the detail panel.
/// </summary>
public partial class RegionMapView : Control
{
    /// <summary>Blank tiles left between overview cells so neighbouring areas stay visually separate.</summary>
    private const int CellGap = 3;

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
        BuildFootprints(region);
        _texture = ImageTexture.CreateFromImage(Stitch(region));
        ShowStart();
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
    /// Mirrors <c>MapImage.SaveOverview</c>'s geometry: each column is as wide as its widest area, each row
    /// as tall as its tallest, and every area is centred in its own cell.
    /// </summary>
    private void BuildFootprints(GeneratedRegion region)
    {
        _footprints.Clear();

        var cells = OverviewLayout.Plan(region.Graph).ToDictionary(cell => cell.AreaId);
        var carved = region.Carved;

        int ColWidth(int col) => cells.Values.Where(c => c.Col == col).Max(c => carved[c.AreaId].Grid.Width);
        int RowHeight(int row) => cells.Values.Where(c => c.Row == row).Max(c => carved[c.AreaId].Grid.Height);

        var colX = new Dictionary<int, int>();
        var x = CellGap;
        foreach (var col in cells.Values.Select(c => c.Col).Distinct().OrderBy(c => c))
        {
            colX[col] = x;
            x += ColWidth(col) + CellGap;
        }

        var rowY = new Dictionary<int, int>();
        var y = CellGap;
        foreach (var row in cells.Values.Select(c => c.Row).Distinct().OrderBy(r => r))
        {
            rowY[row] = y;
            y += RowHeight(row) + CellGap;
        }

        _mapTiles = new Vector2I(x, y);

        foreach (var area in region.Graph.Areas)
        {
            var cell = cells[area.Id];
            var grid = carved[area.Id].Grid;
            var origin = new Vector2I(
                colX[cell.Col] + (ColWidth(cell.Col) - grid.Width) / 2,
                rowY[cell.Row] + (RowHeight(cell.Row) - grid.Height) / 2);

            _footprints.Add(new Footprint(
                area.Id,
                new Rect2I(origin, new Vector2I(grid.Width, grid.Height)),
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

    /// <summary>Blits every carved grid into one RGB8 image at one pixel per tile.</summary>
    private Image Stitch(GeneratedRegion region)
    {
        var pixels = new byte[_mapTiles.X * _mapTiles.Y * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = (byte)LogicalTilePalette.Background.R8;
            pixels[i + 1] = (byte)LogicalTilePalette.Background.G8;
            pixels[i + 2] = (byte)LogicalTilePalette.Background.B8;
        }

        foreach (var footprint in _footprints)
        {
            var grid = region.Carved[footprint.AreaId].Grid;
            for (var ty = 0; ty < grid.Height; ty++)
                for (var tx = 0; tx < grid.Width; tx++)
                {
                    var colour = LogicalTilePalette.Of(grid[tx, ty]);
                    var i = ((footprint.Rect.Position.Y + ty) * _mapTiles.X + footprint.Rect.Position.X + tx) * 3;
                    pixels[i] = (byte)colour.R8;
                    pixels[i + 1] = (byte)colour.G8;
                    pixels[i + 2] = (byte)colour.B8;
                }
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

        foreach (var connection in _region.Graph.Connections)
            DrawLine(
                ConnectorPoint(connection.AreaA, connection.AreaB),
                ConnectorPoint(connection.AreaB, connection.AreaA),
                connection.IsLoopBack ? LogicalTilePalette.LoopBackConnector : LogicalTilePalette.Connector,
                1.5f);

        foreach (var footprint in _footprints)
        {
            var rect = ScreenRect(footprint.Rect);
            DrawRect(rect, footprint.OnCriticalPath
                ? LogicalTilePalette.SpineOutline
                : LogicalTilePalette.BranchOutline, false, 1f);

            if (rect.Size.X >= LabelMinWidth)
                DrawString(font, rect.Position + new Vector2(1f, -3f), footprint.Label,
                    HorizontalAlignment.Left, -1, 11, LogicalTilePalette.AreaLabel);
        }

        var selected = _footprints.FirstOrDefault(f => f.AreaId == _selectedAreaId);
        if (selected is not null)
            DrawRect(ScreenRect(selected.Rect).Grow(2f), LogicalTilePalette.Selection, false, 2f);
    }

    /// <summary>
    /// Where a connector should touch <paramref name="areaId"/>'s side of its edge to
    /// <paramref name="neighborId"/>: the planned aligned opening for a Seamless connection, or — for a Warp,
    /// which has no aligned opening — the point on this area's border facing the neighbour. Border rather
    /// than centre so a warp line runs through the gap between areas instead of straight across their maps.
    /// </summary>
    private Vector2 ConnectorPoint(int areaId, int neighborId)
    {
        var footprint = _footprints.First(f => f.AreaId == areaId);
        var grid = _region!.Carved[areaId].Grid;
        var opening = _region.Openings.OpeningsOf(areaId).FirstOrDefault(o => o.NeighborAreaId == neighborId);

        if (opening is not null)
        {
            var (tileX, tileY) = opening.TileOn(grid.Width, grid.Height);
            return ToScreen(new Vector2(
                footprint.Rect.Position.X + tileX + 0.5f,
                footprint.Rect.Position.Y + tileY + 0.5f));
        }

        var neighbour = _footprints.First(f => f.AreaId == neighborId);
        return ToScreen(BorderPointFacing(footprint.Rect, Centre(neighbour.Rect)));
    }

    private static Vector2 Centre(Rect2I rect)
        => (Vector2)rect.Position + (Vector2)rect.Size / 2f;

    /// <summary>The point on <paramref name="rect"/>'s border where the ray from its centre toward
    /// <paramref name="target"/> leaves the rectangle.</summary>
    private static Vector2 BorderPointFacing(Rect2I rect, Vector2 target)
    {
        var centre = Centre(rect);
        var direction = target - centre;
        if (direction.IsZeroApprox()) return centre;

        var half = (Vector2)rect.Size / 2f;
        var scaleX = Mathf.IsZeroApprox(direction.X) ? float.MaxValue : half.X / Mathf.Abs(direction.X);
        var scaleY = Mathf.IsZeroApprox(direction.Y) ? float.MaxValue : half.Y / Mathf.Abs(direction.Y);
        return centre + direction * Mathf.Min(scaleX, scaleY);
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
