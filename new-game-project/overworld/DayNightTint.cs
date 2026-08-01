using Godot;
using ProcPoke.Overworld;

namespace ProcPoke.OverworldView;

/// <summary>Godot presentation adapter for the engine-independent time-of-day tint.</summary>
public partial class DayNightTint : CanvasModulate
{
    public TimeOfDayState State { get; private set; } = TimeOfDayClock.FromMinutes(12 * 60);

    public void Apply(TimeOfDayState state)
    {
        State = state;
        Color = new Color(state.Tint.Red, state.Tint.Green, state.Tint.Blue, state.Tint.Alpha);
    }
}
