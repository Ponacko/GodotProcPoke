using System;
using Godot;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Encounters;
using ProcPoke.Overworld;

namespace ProcPoke.OverworldView;

/// <summary>Thin Godot adapter: input becomes a pure MovementIntent, then the resolver owns the rules.</summary>
public partial class GridMovementController : Node2D
{
    [Export] public int TilePixels { get; set; } = 16;

    public TileGrid? Grid { get; private set; }
    public PlayerState State { get; private set; } = new(new GridPosition(0, 0));
    public MovementCapabilities Capabilities { get; private set; } = new();
    public Func<GridPosition, bool>? IsInteractable { get; set; }
    public Func<MovementResolution, WildEncounter?>? EncounterRoller { get; set; }

    public event Action<MovementResolution>? Resolved;
    public event Action<WildEncounter>? EncounterTriggered;
    public event Action<WildBattleTransitionRequest>? BattleTransitionRequested;

    public override void _Ready() => InputBindings.EnsureDefaults();

    public void Configure(TileGrid grid, GridPosition start, MovementCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(capabilities);
        Grid = grid;
        State = new PlayerState(start);
        Capabilities = capabilities;
        SyncPosition();
    }

    public MovementResolution Apply(MovementIntent intent)
    {
        if (Grid is null)
            return new MovementResolution(State, MovementResultKind.NoOp);

        var resolution = MovementResolver.Resolve(Grid, State, intent, Capabilities, IsInteractable);
        State = resolution.State;
        SyncPosition();
        Resolved?.Invoke(resolution);
        if (resolution.Result == MovementResultKind.Moved
            && EncounterRoller?.Invoke(resolution) is { } encounter)
        {
            EncounterTriggered?.Invoke(encounter);
            BattleTransitionRequested?.Invoke(
                WildBattleTransitionResolver.Create(encounter, resolution));
        }
        return resolution;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Grid is null || !@event.IsPressed()) return;
        var intent = InputBindings.IntentFor(@event);
        if (intent is null) return;
        Apply(intent.Value);
        GetViewport().SetInputAsHandled();
    }

    private void SyncPosition()
        => Position = new Vector2(
            (State.Position.X + 0.5f) * TilePixels,
            (State.Position.Y + 0.5f) * TilePixels);
}
