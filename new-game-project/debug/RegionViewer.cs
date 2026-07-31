using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Bosses;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Encounters;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Topology;

namespace ProcPoke.DebugView;

/// <summary>
/// The in-engine Phase 2 review harness — the same thing <c>tools/ProcPoke.MapGen</c> prints, but
/// interactive: it runs the real pipeline on a seed and shows the stitched Logical Tile map beside
/// <see cref="RegionGraphText"/>'s report, a per-area breakdown, and an implementation-status page.
///
/// Presentation only (ADR-0003): every fact on screen is read back from a <see cref="GeneratedRegion"/>,
/// and nothing here is game logic. It exists because Phase 3's Tile Realizer does not — until it lands
/// there is no other way to look at a generated region from inside the engine.
///
/// Runs from the editor (or any non-packaged build): baked data is read off disk through
/// <see cref="ProjectSettings.GlobalizePath"/>, which resolves <c>res://</c> to a real directory only when
/// the project is not running from a <c>.pck</c>.
/// </summary>
public partial class RegionViewer : Control
{
    /// <summary>Baked data is a few MB of JSON — load it once per run and reuse it for every seed.</summary>
    private static GameData? _data;

    private SpinBox _seedInput = null!;
    private SpinBox _badgeInput = null!;
    private SpinBox _dexInput = null!;
    private SpinBox _capInput = null!;
    private Button _generateButton = null!;
    private OptionButton _areaPicker = null!;
    private Label _statusLabel = null!;
    private RegionMapView _mapView = null!;
    private TabContainer _tabs = null!;
    private Label _reportText = null!;
    private Label _areaText = null!;
    private Label _dexText = null!;
    private Label _roadmapText = null!;
    private VBoxContainer _legendList = null!;

    private GeneratedRegion? _region;
    private bool _generating;

    /// <summary>Area ids in first-availability order — the order the picker and the ◀ ▶ steppers walk.</summary>
    private IReadOnlyList<int> _areaOrder = [];

    // Handed from the generation task back to the main thread by the deferred completion call.
    private GeneratedRegion? _pendingRegion;
    private Exception? _pendingError;
    private TimeSpan _pendingElapsed;

    public override void _Ready()
    {
        // The project renders at GBA resolution (256×192, integer-scaled) for the actual game. A dense
        // debug UI cannot live there, so this scene opts out of the pixel-art viewport for its window.
        var window = GetWindow();
        window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
        window.MinSize = new Vector2I(900, 560);
        if (window.Size.X < 1180 || window.Size.Y < 740) window.Size = new Vector2I(1280, 800);

        _seedInput = GetNode<SpinBox>("%SeedInput");
        _badgeInput = GetNode<SpinBox>("%BadgeInput");
        _dexInput = GetNode<SpinBox>("%DexInput");
        _capInput = GetNode<SpinBox>("%CapInput");
        _generateButton = GetNode<Button>("%GenerateButton");
        _areaPicker = GetNode<OptionButton>("%AreaPicker");
        _statusLabel = GetNode<Label>("%StatusLabel");
        _mapView = GetNode<RegionMapView>("%MapView");
        _tabs = GetNode<TabContainer>("%Tabs");
        _reportText = GetNode<Label>("%ReportText");
        _areaText = GetNode<Label>("%AreaText");
        _dexText = GetNode<Label>("%DexText");
        _roadmapText = GetNode<Label>("%RoadmapText");
        _legendList = GetNode<VBoxContainer>("%LegendList");

        _generateButton.Pressed += Generate;
        GetNode<Button>("%PrevSeedButton").Pressed += () => StepSeed(-1);
        GetNode<Button>("%NextSeedButton").Pressed += () => StepSeed(+1);
        GetNode<Button>("%FitButton").Pressed += _mapView.FitToView;
        GetNode<Button>("%StartButton").Pressed += _mapView.ShowStart;
        GetNode<Button>("%PrevAreaButton").Pressed += () => StepArea(-1);
        GetNode<Button>("%NextAreaButton").Pressed += () => StepArea(+1);
        _areaPicker.ItemSelected += index => GoToArea(_areaOrder[(int)index]);
        _mapView.AreaSelected += OnAreaSelected;

        BuildLegend();
        _roadmapText.Text = Roadmap;
        _areaText.Text = "Click an area on the map to inspect it.";
        _reportText.Text = "Press Generate to run the pipeline.";
        _dexText.Text = "Press Generate to run the pipeline.";

        Generate();
    }

    // ── generation ──────────────────────────────────────────────────────────────────────────────────

    private GenerationSettings ReadSettings() => new()
    {
        Seed = (ulong)_seedInput.Value,
        BadgeCount = (int)_badgeInput.Value,
        DexSize = (int)_dexInput.Value,
        RosterCap = (int)_capInput.Value,
    };

    private void StepSeed(int delta)
    {
        _seedInput.Value = Math.Max(0d, _seedInput.Value + delta);
        Generate();
    }

    /// <summary>
    /// Runs the pipeline off the main thread — the first call also pays for the JSON load — and hands the
    /// result back through a deferred call so only the main thread ever touches Godot objects.
    /// </summary>
    private void Generate()
    {
        if (_generating) return;

        var settings = ReadSettings();
        _generating = true;
        _generateButton.Disabled = true;
        _statusLabel.Text = _data is null
            ? $"Loading baked data, then generating seed {settings.Seed}…"
            : $"Generating seed {settings.Seed}…";

        Task.Run(() =>
        {
            try
            {
                _data ??= GameDataLoader.Load(ProjectSettings.GlobalizePath("res://data"));
                var stopwatch = Stopwatch.StartNew();
                _pendingRegion = RegionGenerator.Generate(settings, _data);
                _pendingElapsed = stopwatch.Elapsed;
                _pendingError = null;
            }
            catch (Exception exception)
            {
                _pendingRegion = null;
                _pendingError = exception;
            }

            Callable.From(OnGenerationFinished).CallDeferred();
        });
    }

    private void OnGenerationFinished()
    {
        _generating = false;
        _generateButton.Disabled = false;

        if (_pendingError is not null)
        {
            _statusLabel.Text = $"Generation failed: {_pendingError.Message}";
            _reportText.Text = _pendingError.ToString();
            GD.PushError(_pendingError.ToString());
            return;
        }

        _region = _pendingRegion;
        if (_region is null) return;

        _mapView.ShowRegion(_region);
        BuildAreaPicker(_region);
        _reportText.Text = RegionGraphText.Render(
            _region.Graph, _region.Gating, _region.Biomes, _region.Names, _region.Identity,
            _region.Starters, _region.Dex, _data, _region.Special, _region.Encounters,
            _region.Trainers, _region.Bosses, _region.Rivals, _region.Npcs);
        _areaText.Text = "Click an area on the map to inspect it.";
        _dexText.Text = BuildDexText(_region);

        var graph = _region.Graph;
        _statusLabel.Text =
            $"seed {_region.Settings.Seed}   {graph.Areas.Count} areas   {graph.Connections.Count} connections   " +
            $"{_region.Gating.Gates.Count} gates   {graph.CriticalPath.Count} on the spine   " +
            $"dex {_region.Dex.Entries.Count}+{_region.Special.Legendaries.Count}   " +
            $"generated in {_pendingElapsed.TotalMilliseconds:F0} ms";
        // Echoed so a headless run of this scene is self-checking, and so the summary can be copied out.
        GD.Print(_statusLabel.Text);
    }

    // ── area navigation ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists every area in first-availability order (<see cref="AvailabilityOrder"/>) — the same order the
    /// dex numbers by and encounters level by, so stepping through it walks the region as a player would.
    /// </summary>
    private void BuildAreaPicker(GeneratedRegion region)
    {
        _areaOrder = AvailabilityOrder.Of(region.Graph, GatingGenerator.OffSpineAnchors(region.Graph));
        _areaPicker.Clear();

        foreach (var areaId in _areaOrder)
        {
            var area = region.Graph[areaId];
            var prefix = area.OnCriticalPath ? $"{area.PathIndex,2}.  " : "     ↳ ";
            _areaPicker.AddItem($"{prefix}{region.Names.Of(areaId)}   [{area.Archetype}]");
        }

        _areaPicker.Selected = 0;
    }

    private void StepArea(int delta)
    {
        if (_areaOrder.Count == 0) return;

        var index = Mathf.PosMod(_areaPicker.Selected + delta, _areaOrder.Count);
        _areaPicker.Selected = index;
        GoToArea(_areaOrder[index]);
    }

    private void GoToArea(int areaId)
    {
        _mapView.FocusArea(areaId);
        _mapView.Select(areaId);
    }

    private void OnAreaSelected(int areaId)
    {
        if (_region is null) return;

        // Keep the picker in step when the selection came from a click on the map.
        for (var index = 0; index < _areaOrder.Count; index++)
            if (_areaOrder[index] == areaId)
            {
                _areaPicker.Selected = index;
                break;
            }

        _areaText.Text = DescribeArea(_region, areaId);
        _tabs.CurrentTab = AreaTabIndex;
    }

    // ── per-area detail ─────────────────────────────────────────────────────────────────────────────

    private const int AreaTabIndex = 1;

    private string DescribeArea(GeneratedRegion region, int areaId)
    {
        var graph = region.Graph;
        var area = graph[areaId];
        var carved = region.Carved[areaId];
        var text = new StringBuilder();

        var position = area.OnCriticalPath ? $"spine #{area.PathIndex}" : "off-spine";
        text.AppendLine($"{region.Names.Of(areaId)}");
        text.AppendLine($"{area.Archetype}  {area.Size}  {{{region.Biomes.Of(areaId)}}}  ({position})");
        if (areaId == graph.StartAreaId) text.AppendLine("The start town — the Professor's lab sits here.");
        if (areaId == graph.LeagueAreaId) text.AppendLine("The Pokémon League.");
        if (region.Identity.GymTypes.TryGetValue(areaId, out var gymType))
        {
            var leader = region.Bosses.GymLeaders[areaId];
            text.AppendLine($"Gym: {gymType} — {leader.Name}, {leader.Members.Count} mon, ace Lv{Ace(leader.Members)}");
        }

        AppendCarving(text, carved);
        AppendGating(text, region, area);
        AppendConnections(text, region, areaId);
        AppendEncounters(text, region, areaId);
        AppendPopulation(text, region, areaId);
        AppendNpcs(text, region, areaId);

        return text.ToString();
    }

    private static void AppendCarving(StringBuilder text, CarvedArea carved)
    {
        Section(text, "carving");
        text.AppendLine($"  grid          {carved.Grid.Width} × {carved.Grid.Height} tiles");
        text.AppendLine($"  openings      {carved.Openings.Count}");
        text.AppendLine($"  gate tiles    {carved.GateTiles.Count}");
        text.AppendLine("  tile mix");
        foreach (var tile in LogicalTilePalette.LegendOrder)
        {
            var count = carved.Grid.Count(tile);
            if (count > 0) text.AppendLine($"    {tile,-14}{count,6}");
        }
    }

    private void AppendGating(StringBuilder text, GeneratedRegion region, Area area)
    {
        var gating = region.Gating;
        var onExit = area.OnCriticalPath ? gating.GateBlockingExitOf(area.PathIndex) : null;
        var keysHere = gating.Gates.Where(gate => gate.KeyAreaId == area.Id).ToList();
        var hintsHere = gating.Gates.Where(gate => gate.HintAreaId == area.Id).ToList();
        var flyHere = gating.Fly.AreaId == area.Id;

        if (onExit is null && keysHere.Count == 0 && hintsHere.Count == 0 && !flyHere) return;

        Section(text, "gating");
        if (onExit is not null)
        {
            text.AppendLine($"  gate on exit  {onExit.Obstacle} → {onExit.KeyName} ({Prerequisite(onExit)})");
            text.AppendLine($"                key in \"{region.Names.Of(onExit.KeyAreaId)}\", " +
                            $"hint in \"{region.Names.Of(onExit.HintAreaId)}\"");
            if (onExit.TerrainTag is not null)
                text.AppendLine($"                biome requirement \"{onExit.TerrainTag}\"");
        }
        foreach (var gate in keysHere)
            text.AppendLine($"  holds key     {gate.KeyName} — opens {gate.Obstacle} at spine #{gate.BlockPathIndex}");
        foreach (var gate in hintsHere)
            text.AppendLine($"  hint NPC      names where to find {gate.KeyName}");
        if (flyHere)
            text.AppendLine($"  Fly           taught here (badge {gating.Fly.BadgePrerequisite}) — travel only, gates nothing");
    }

    private static string Prerequisite(Gate gate) => gate.BadgePrerequisite is { } badge
        ? $"{gate.KeyKind}, badge {badge}"
        : gate.KeyKind.ToString();

    private static void AppendConnections(StringBuilder text, GeneratedRegion region, int areaId)
    {
        Section(text, "connections");
        foreach (var connection in region.Graph.ConnectionsOf(areaId))
        {
            var other = region.Graph[connection.Other(areaId)];
            var loop = connection.IsLoopBack ? "   (loop-back)" : "";
            text.AppendLine($"  {connection.Kind,-9} → {region.Names.Of(other.Id)} " +
                            $"[{other.Archetype}]{loop}");
        }
    }

    private void AppendEncounters(StringBuilder text, GeneratedRegion region, int areaId)
    {
        var encounters = region.Encounters.Of(areaId);
        if (encounters is null || (encounters.Tables.Count == 0 && encounters.Overlay is null))
        {
            Section(text, "encounters");
            text.AppendLine("  none — towns, the League, and interiors carry no wild tables");
            return;
        }

        Section(text, $"encounters — base wild level {encounters.BaseLevel}");
        foreach (var table in encounters.Tables) AppendTable(text, table.Method.ToString(), table);
        if (encounters.Overlay is not null)
            AppendTable(text,
                $"Overlay ({encounters.Overlay.HiddenAbilityChance:P0} hidden ability)",
                encounters.Overlay);
    }

    private void AppendTable(StringBuilder text, string title, EncounterTable table)
    {
        text.AppendLine($"  {title}");
        foreach (var slot in table.Slots)
            text.AppendLine($"    {slot.Percent,3}%  {Species(slot.SpeciesId),-22} Lv{slot.MinLevel}–{slot.MaxLevel}");
    }

    private void AppendPopulation(StringBuilder text, GeneratedRegion region, int areaId)
    {
        var trainers = region.Trainers.TrainersOf(areaId);
        var items = region.Trainers.ItemsOf(areaId);
        var fossils = region.Special.Fossils.Where(f => f.AreaId == areaId).ToList();
        var legendaries = region.Special.Legendaries.Where(l => l.AreaId == areaId).ToList();

        if (trainers.Count > 0)
        {
            Section(text, $"trainers ({trainers.Count})");
            foreach (var trainer in trainers)
            {
                var where = trainer.Position is { } tile ? $"({tile.X,3},{tile.Y,3})" : "   gym    ";
                text.AppendLine($"  {where}  {trainer.DisplayClass,-22} ace Lv{trainer.AceLevel,-3} {trainer.Roster.Count} mon");
                foreach (var member in trainer.Roster)
                    text.AppendLine($"                  {Species(member.SpeciesId),-22} Lv{member.Level}");
            }
        }

        if (items.Count > 0)
        {
            Section(text, $"items ({items.Count})");
            foreach (var item in items)
                text.AppendLine($"  ({item.Position.X,3},{item.Position.Y,3})  {ItemName(item.ItemId)}");
        }

        if (fossils.Count > 0 || legendaries.Count > 0)
        {
            Section(text, "special species");
            foreach (var fossil in fossils)
                text.AppendLine($"  fossil      {Species(fossil.SpeciesId)}");
            foreach (var legendary in legendaries)
                text.AppendLine($"  legendary   #{legendary.DexNumber} {Species(legendary.SpeciesId)} Lv{legendary.Level}");
        }
    }

    private static void AppendNpcs(StringBuilder text, GeneratedRegion region, int areaId)
    {
        var posts = region.Npcs.Of(areaId);
        if (posts.Count == 0) return;

        Section(text, $"NPC posts ({posts.Count})");
        foreach (var post in posts)
            text.AppendLine($"  {post.Kind,-12} {post.Text}");
    }

    private static void Section(StringBuilder text, string title)
        => text.Append('\n').Append("── ").Append(title).Append(' ').AppendLine(new string('─', Math.Max(2, 52 - title.Length)));

    private static int Ace(IReadOnlyList<BossMember> members)
        => members.Count == 0 ? 0 : members.Max(member => member.Level);

    private string Species(int speciesId) => _data is not null && _data.Species.TryGetValue(speciesId, out var species)
        ? $"#{speciesId:D3} {species.Name}"
        : $"#{speciesId:D3}";

    private string ItemName(int itemId) => _data is not null && _data.Items.TryGetValue(itemId, out var item)
        ? item.Name
        : $"item {itemId}";

    // ── pokédex ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every regional dex entry — <see cref="RegionGraphText"/>'s report truncates this to ten rows at
    /// each end for console brevity, but a scrollable tab has no reason to.
    /// </summary>
    private string BuildDexText(GeneratedRegion region)
    {
        var dex = region.Dex;
        var text = new StringBuilder();
        text.AppendLine(region.Special.Legendaries.Count > 0
            ? $"Regional dex — {dex.Entries.Count} species + {region.Special.Legendaries.Count} legendaries"
            : $"Regional dex — {dex.Entries.Count} species");
        text.AppendLine();

        var areaOf = new Dictionary<int, int>();
        foreach (var (areaId, speciesIds) in dex.SpeciesByArea)
            foreach (var speciesId in speciesIds)
                areaOf[speciesId] = areaId;
        var fossilFamily = dex.FossilFamilySpecies.ToHashSet();

        foreach (var entry in dex.Entries)
        {
            var species = _data!.Species[entry.SpeciesId];
            var where = areaOf.TryGetValue(entry.SpeciesId, out var areaId)
                ? region.Names.Of(areaId) + (fossilFamily.Contains(entry.SpeciesId) ? " [fossil]" : "")
                : "—";
            text.AppendLine($"#{entry.Number,3}  {species.Name,-14}{string.Join("/", species.Types),-14}" +
                            $"BST {Bst(species),-5}{where}");
        }

        if (region.Special.Legendaries.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Legendaries:");
            foreach (var legendary in region.Special.Legendaries.OrderBy(l => l.DexNumber))
            {
                var species = _data!.Species[legendary.SpeciesId];
                text.AppendLine($"#{legendary.DexNumber,3}  {species.Name,-14}{string.Join("/", species.Types),-14}" +
                                $"Lv{legendary.Level,-4}{region.Names.Of(legendary.AreaId)}");
            }
        }

        return text.ToString();
    }

    private static int Bst(PokemonSpecies species) => species.BaseStats.Hp + species.BaseStats.Attack +
        species.BaseStats.Defense + species.BaseStats.SpecialAttack + species.BaseStats.SpecialDefense +
        species.BaseStats.Speed;

    // ── map key ─────────────────────────────────────────────────────────────────────────────────────

    private void BuildLegend()
    {
        foreach (var tile in LogicalTilePalette.LegendOrder)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var swatch = new ColorRect
            {
                Color = LogicalTilePalette.Of(tile),
                CustomMinimumSize = new Vector2(20f, 14f),
            };
            row.AddChild(swatch);
            row.AddChild(new Label { Text = LogicalTilePalette.Describe(tile) });
            _legendList.AddChild(row);
        }

        _legendList.AddChild(new HSeparator());
        _legendList.AddChild(new Label { Text = LegendNotes, AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    /// <summary>One paragraph per line — the label autowraps, so a hard break mid-paragraph would show.</summary>
    private const string LegendNotes = """
        Outlines mark areas: bright = on the Spine (the mandatory Route 1 → League path), dim blue-grey = off-spine (branches, detours, destination dungeons). ★ marks the start town, ◆ the League.

        Lines are connections. A Seamless Map Connection is drawn between the two areas' aligned openings; a Warp has no aligned edge, so it is drawn border-to-border. Blue lines are loop-back shortcuts.

        Colours carry gameplay meaning only, never art. The Tile Realizer that turns these into tileset graphics is Phase 3 work and does not exist yet.

        Drag to pan, scroll to zoom, click an area to inspect it.
        """;

    // ── implementation status ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hand-maintained summary of <c>docs/DEVELOPMENT-PLAN.md</c> and <c>tickets.md</c> — the answer to
    /// "what actually works so far?" for whoever runs this scene.
    /// </summary>
    private const string Roadmap = """
        ProcPoke — what is built so far

        Phase 0  Scaffolding ............................. DONE
          Godot project plus three engine-independent domain libraries
          (ADR-0003), three xUnit test projects and two console tools. Domain
          code never references Godot; this scene is the only presentation
          layer that exists.

        Phase 1  Data pipeline ........................... DONE
          tools/ProcPoke.Bake fetches a pinned PokeAPI commit and bakes
          data/*.json: 649 species, moves, learnsets, evolutions (with the
          trade → Link Cable transform applied), items, natures, growth
          curves, the 17-type chart, the canon-name blocklist, and a manifest
          a test asserts against the pin. Everything on this screen is read
          through ProcPoke.Data.

        Phase 2  Generator, headless ..................... NEARLY DONE
          The go/no-go milestone: the whole bet, with no gameplay attached.

          2a  Region graph ............................... DONE
              Constructive topology, Gate Budget and lock-and-key placement
              (ADR-0002), HM badge prerequisites, Fly, biomes, dungeon
              archetypes, names, gym / Elite Four / Champion types, villain
              teams, rival archetype, Champion roll.
          2b  Map carving ................................ DONE
              Route, town, forest, cave, mountain, villain hideout, tower and
              League carvers, all spine-first with gates on verified
              chokepoints (ADR-0001); decoration rules for directional
              ledges, item nooks and trainer sightlines; the PNG and ASCII
              debug renderers.
          2c  Population ................................. DONE
              Regional dex with whole evolution families and
              first-availability numbering, fossils and legendaries, Gen 5
              encounter slot tables with Special Encounter Overlays, route
              and gym trainers, item balls, gym / Elite Four / Champion
              teams, rival teams across four beats, and the graph-level NPC
              plan (hints, gym guides, gossip, signs, flavour).

          open  8b  NPC posts stamped onto carved tiles — the plan exists,
                    the tiles do not.
          open  9   Go/no-go sign-off packet: the 10,000-seed fuzz run plus
                    a human verdict on whether these maps read as
                    hand-crafted. That review is what this scene is for.

        Phase 3  Overworld ............................... NOT STARTED
          No Tile Realizer, no movement, no interiors — which is why the map
          beside this panel is flat Logical Tile colour rather than tileset
          art, and why nothing here is walkable.

        Phase 4  Battle engine ........................... NOT STARTED
          ProcPoke.Battle holds only the canonical-ruleset constant. The
          trainer and boss rosters in the Area tab are generated data with no
          simulation behind them yet.

        Phase 5  Loop closers → MVP ...................... NOT STARTED
        Phase 6  Shell and polish ........................ NOT STARTED
        Phase 7  Second region ........................... NOT STARTED

        Reference: docs/DEVELOPMENT-PLAN.md, tickets.md, docs/adr/, CONTEXT.md
        """;
}
