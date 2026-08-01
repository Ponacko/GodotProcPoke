using Godot;
using ProcPoke.Overworld;

namespace ProcPoke.OverworldView;

/// <summary>One action vocabulary for keyboard and controller input.</summary>
public static class InputBindings
{
    public const string MoveUp = "move_up";
    public const string MoveRight = "move_right";
    public const string MoveDown = "move_down";
    public const string MoveLeft = "move_left";
    public const string Confirm = "confirm";
    public const string Cancel = "cancel";
    public const string Run = "run";
    public const string Bicycle = "bicycle";
    public const string Surf = "surf";
    public const string Menu = "menu";
    public const string Map = "map";

    private static readonly (string Action, Key Key, JoyButton Button)[] Defaults =
    [
        (MoveUp, Key.W, JoyButton.DpadUp),
        (MoveRight, Key.D, JoyButton.DpadRight),
        (MoveDown, Key.S, JoyButton.DpadDown),
        (MoveLeft, Key.A, JoyButton.DpadLeft),
        (Confirm, Key.Z, JoyButton.A),
        (Cancel, Key.X, JoyButton.B),
        (Run, Key.Shift, JoyButton.X),
        (Bicycle, Key.B, JoyButton.Y),
        (Surf, Key.F, JoyButton.LeftShoulder),
        (Menu, Key.Escape, JoyButton.Start),
        (Map, Key.M, JoyButton.Back),
    ];

    public static void EnsureDefaults()
    {
        foreach (var (action, key, button) in Defaults)
        {
            if (!InputMap.HasAction(action)) InputMap.AddAction(action);

            var keyboard = new InputEventKey { PhysicalKeycode = key };
            if (!InputMap.ActionHasEvent(action, keyboard)) InputMap.ActionAddEvent(action, keyboard);

            var controller = new InputEventJoypadButton { ButtonIndex = button };
            if (!InputMap.ActionHasEvent(action, controller)) InputMap.ActionAddEvent(action, controller);
        }
    }

    public static MovementIntent? IntentFor(InputEvent input)
    {
        if (input.IsActionPressed(Confirm)) return MovementIntent.Confirm;
        if (input.IsActionPressed(Run)) return MovementIntent.ToggleRun;
        if (input.IsActionPressed(Bicycle)) return MovementIntent.ToggleBicycle;
        if (input.IsActionPressed(Surf)) return MovementIntent.ToggleSurf;
        if (input.IsActionPressed(MoveUp)) return MovementIntent.MoveNorth;
        if (input.IsActionPressed(MoveRight)) return MovementIntent.MoveEast;
        if (input.IsActionPressed(MoveDown)) return MovementIntent.MoveSouth;
        if (input.IsActionPressed(MoveLeft)) return MovementIntent.MoveWest;
        return null;
    }
}
