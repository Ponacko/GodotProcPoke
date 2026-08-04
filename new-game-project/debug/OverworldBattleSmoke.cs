using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ProcPoke;
using ProcPoke.Battle;
using ProcPoke.BattleView;
using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Overworld;
using ProcPoke.OverworldView;

namespace ProcPoke.Debug;

/// <summary>
/// Throwaway headless smoke driver for manual verification only (not part of the shipped game or the
/// automated test suite) — run via
/// <c>godot --headless --path . --script res://debug/OverworldBattleSmoke.cs</c>. Drives the real
/// OverworldRoot/BattleScreen scenes end to end (no GUI automation available in this environment): walks
/// onto tall grass until a wild encounter fires, presses the first move button repeatedly until the battle
/// ends, then confirms control returns to the overworld. Prints PASS/FAIL lines and quits.
/// </summary>
public partial class OverworldBattleSmoke : SceneTree
{
    public override void _Initialize()
    {
        Callable.From(RunAsync).CallDeferred();
    }

    private async void RunAsync()
    {
        try
        {
            await Run();
            GD.Print("SMOKE: PASS");
        }
        catch (Exception exception)
        {
            GD.Print($"SMOKE: FAIL — {exception}");
        }
        Quit();
    }

    private async Task Run()
    {
        var data = GameDataLoader.Load(ProjectSettings.GlobalizePath("res://data"));
        var settings = new GenerationSettings { Seed = 7, BadgeCount = 8, DexSize = 150, RosterCap = 5 };
        var region = RegionGenerator.Generate(settings, data);
        var identity = NewGameIdentity.Create(CharacterChoice.CharacterA, "Smoke");
        var session = new OverworldSession(region, identity);
        session.Interactions.EnableDebugUnlock();
        session.Party.Add(BattleProgressionFactory.AtLevel(data, speciesId: 1, level: 5));

        // Jump straight to some area with TallGrass — towns/League/hideouts carry no wild tables by
        // design (only Route/Forest/cave-family/Tower do), so the start town never has grass to test with.
        var grassAreaIds = region.Carved
            .Where(pair => FindTallGrass(pair.Value.Grid) is not null)
            .Select(pair => pair.Key)
            .ToList();
        if (grassAreaIds.Count == 0)
            throw new InvalidOperationException("no area in this region has a TallGrass tile at all.");
        var grassAreaId = grassAreaIds[0];
        session.VisitArea(grassAreaId);

        GameState.Instance.Data = data;
        GameState.Instance.Session = session;
        GameState.Instance.ResumePlayerState = null; // let OverworldRoot fall back to its normal first-walkable spawn

        ChangeSceneToFile("res://overworld/OverworldRoot.tscn");
        var overworld = await WaitForScene<OverworldRootController>();
        GD.Print("SMOKE: overworld scene loaded");

        var movement = overworld.GetNode<GridMovementController>("Player");
        var grid = region.Carved[grassAreaId].Grid;
        var target = FindTallGrass(grid)
            ?? throw new InvalidOperationException("chosen area has no TallGrass tile after all.");
        var path = FindPath(grid, movement.State.Position, target)
            ?? throw new InvalidOperationException("no walkable path to a TallGrass tile from the spawn position.");
        if (path.Count == 0)
            throw new InvalidOperationException("spawn position is already on tall grass — cannot test a step-on encounter roll.");
        GD.Print($"SMOKE: walking {path.Count} steps to tall grass at ({target.X},{target.Y})");

        // The path itself may cross other tall-grass tiles (BFS doesn't avoid them), so an encounter can
        // fire before the target is even reached — bail out of walking the instant it does (checking
        // GameState's transient flag would race BattleScreen clearing it, so check node liveness instead:
        // ChangeSceneToFile frees the whole OverworldRoot tree, including this Player node, the moment it
        // happens).
        var attempts = 0;
        foreach (var intent in path)
        {
            if (!GodotObject.IsInstanceValid(movement)) break;
            movement.Apply(intent);
            await NextFrame();
        }

        if (GodotObject.IsInstanceValid(movement))
        {
            var onto = path[^1];
            var back = Opposite(onto);
            while (GodotObject.IsInstanceValid(movement) && attempts < 400)
            {
                movement.Apply(back);
                await NextFrame();
                if (!GodotObject.IsInstanceValid(movement)) break;
                movement.Apply(onto);
                await NextFrame();
                attempts++;
            }
            if (GodotObject.IsInstanceValid(movement))
                throw new InvalidOperationException($"no wild encounter fired after {attempts} step pairs on tall grass.");
        }
        GD.Print($"SMOKE: wild encounter fired after {attempts} step pair(s)");

        var battle = await WaitForScene<BattleScreen>();
        GD.Print("SMOKE: battle scene loaded");

        // Each real turn replays several Tween-animated events (lunge, HP drain, ...) at real wall-clock
        // speed, not per-frame — a single Godot frame is nowhere near enough time for one to finish, so
        // each attempt below waits out a whole turn's worth of real time before trying the next press.
        var turns = 0;
        while (CurrentScene is BattleScreen && turns < 20)
        {
            if (CurrentScene.GetNode<HBoxContainer>("%MoveButtons").Visible)
            {
                var button = CurrentScene.GetNode<HBoxContainer>("%MoveButtons")
                    .GetChildren().OfType<Button>().First();
                button.EmitSignal(BaseButton.SignalName.Pressed);
            }
            else if (CurrentScene.GetNode<Button>("%FightButton") is { Disabled: false } fightButton)
            {
                fightButton.EmitSignal(BaseButton.SignalName.Pressed);
            }
            await Wait(2.0);
            turns++;
        }
        GD.Print($"SMOKE: battle resolved after {turns} UI step(s), scene is now {CurrentScene.Name}");

        if (CurrentScene is not OverworldRootController)
            throw new InvalidOperationException($"expected to return to OverworldRoot, still on {CurrentScene.GetType().Name}.");
        GD.Print("SMOKE: control returned to the overworld");
    }

    private async Task NextFrame()
        => await ToSignal(this, SceneTree.SignalName.ProcessFrame);

    private async Task Wait(double seconds)
        => await ToSignal(CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task<T> WaitForScene<T>() where T : Node
    {
        for (var i = 0; i < 60; i++)
        {
            if (CurrentScene is T match) return match;
            await NextFrame();
        }
        throw new InvalidOperationException(
            $"{typeof(T).Name} did not become the current scene within 60 frames (current: {CurrentScene?.GetType().Name}).");
    }

    private static MovementIntent Opposite(MovementIntent intent) => intent switch
    {
        MovementIntent.MoveNorth => MovementIntent.MoveSouth,
        MovementIntent.MoveSouth => MovementIntent.MoveNorth,
        MovementIntent.MoveEast => MovementIntent.MoveWest,
        MovementIntent.MoveWest => MovementIntent.MoveEast,
        _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, null),
    };

    private static GridPosition? FindTallGrass(TileGrid grid)
        => Enumerable.Range(0, grid.Height)
            .SelectMany(y => Enumerable.Range(0, grid.Width).Select(x => new GridPosition(x, y)))
            .Cast<GridPosition?>()
            .FirstOrDefault(position => grid[position!.Value.X, position.Value.Y] == LogicalTile.TallGrass);

    private static List<MovementIntent>? FindPath(
        TileGrid grid, GridPosition start, GridPosition goal)
    {
        var capabilities = new MovementCapabilities(DebugUnlock: true);
        var came = new Dictionary<GridPosition, (GridPosition Prev, MovementIntent Intent)>();
        var seen = new HashSet<GridPosition> { start };
        var queue = new Queue<GridPosition>();
        queue.Enqueue(start);
        var directions = new[]
        {
            MovementIntent.MoveNorth, MovementIntent.MoveEast, MovementIntent.MoveSouth, MovementIntent.MoveWest,
        };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == goal) break;
            foreach (var intent in directions)
            {
                var resolution = MovementResolver.Resolve(grid, new PlayerState(current), intent, capabilities);
                if (resolution.Result != MovementResultKind.Moved || resolution.Target is not { } next) continue;
                if (!seen.Add(next)) continue;
                came[next] = (current, intent);
                queue.Enqueue(next);
            }
        }

        if (!seen.Contains(goal)) return null;
        var pathReversed = new List<MovementIntent>();
        var cursor = goal;
        while (cursor != start)
        {
            var (prev, intent) = came[cursor];
            pathReversed.Add(intent);
            cursor = prev;
        }
        pathReversed.Reverse();
        return pathReversed;
    }
}
