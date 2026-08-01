using System;
using System.Threading.Tasks;
using Godot;
using ProcPoke.Generation;
using ProcPoke.Overworld;

namespace ProcPoke.OverworldView;

/// <summary>
/// Godot transition adapter. Seamless crossings invoke the loader immediately; Warp crossings fade to black,
/// invoke the same loader at the paired arrival, then fade back in. Area loading remains owned by the session.
/// </summary>
public partial class TransitionController : CanvasLayer
{
    [Export] public float FadeSeconds { get; set; } = 0.18f;

    private ColorRect _fade = null!;

    public override void _Ready()
    {
        _fade = GetNodeOrNull<ColorRect>("Fade") ?? CreateFade();
        _fade.Modulate = new Color(1, 1, 1, 0);
        _fade.MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    public async Task<bool> TransitionAsync(
        GeneratedRegion region,
        int fromAreaId,
        GridPosition position,
        Facing facing,
        Action<AreaArrival> load)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(load);

        var result = TransitionResolver.Resolve(region, fromAreaId, position, facing);
        if (!result.Succeeded || result.Arrival is null)
        {
            GD.PushWarning(result.Failure ??
                $"no transition from area {fromAreaId} at ({position.X},{position.Y}) facing {facing}");
            return false;
        }

        if (result.Kind == TransitionResultKind.Seamless)
        {
            load(result.Arrival);
            return true;
        }

        await FadeTo(1f);
        load(result.Arrival);
        await FadeTo(0f);
        return true;
    }

    private async Task FadeTo(float alpha)
    {
        var tween = CreateTween();
        tween.TweenProperty(_fade, "modulate:a", alpha, FadeSeconds);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private ColorRect CreateFade()
    {
        var fade = new ColorRect
        {
            Name = "Fade",
            Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        fade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(fade);
        return fade;
    }
}
