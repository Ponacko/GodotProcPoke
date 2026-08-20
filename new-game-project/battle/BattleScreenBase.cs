using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using ProcPoke.Battle;
using ProcPoke.Overworld;

namespace ProcPoke.BattleView;

/// <summary>
/// Shared event-replay/animation plumbing for every battle screen (ADR-0006: the sim narrates, this only
/// animates + logs in strict order). <see cref="BattleScreen"/> (wild) and <see cref="TrainerBattleScreen"/>
/// (scripted rival battles) both derive from this — everything session-specific (setup, input wiring,
/// outcome handling) stays on the concrete screen. <see cref="TryAnimateExtra"/> is the one seam a subclass
/// hooks for events only it can emit (wild-only capture events, so far).
/// </summary>
public abstract partial class BattleScreenBase : Control
{
    protected ColorRect Backdrop = null!;
    protected AnimatedSprite2D OpponentSprite = null!;
    protected AnimatedSprite2D PlayerSprite = null!;
    protected Label OpponentLabel = null!;
    protected ProgressBar OpponentHp = null!;
    protected Label OpponentStatus = null!;
    protected Label PlayerLabel = null!;
    protected ProgressBar PlayerHp = null!;
    protected Label PlayerStatus = null!;
    protected RichTextLabel Log = null!;
    protected HBoxContainer Actions = null!;
    protected HBoxContainer MoveButtons = null!;
    protected Button FightButton = null!;
    protected AudioStreamPlayer Cry = null!;

    protected BattleState State { get; set; } = null!;

    protected void BindCommonNodes()
    {
        Backdrop = GetNode<ColorRect>("%Backdrop");
        OpponentSprite = GetNode<AnimatedSprite2D>("%OpponentSprite");
        PlayerSprite = GetNode<AnimatedSprite2D>("%PlayerSprite");
        OpponentLabel = GetNode<Label>("%OpponentLabel");
        OpponentHp = GetNode<ProgressBar>("%OpponentHp");
        OpponentStatus = GetNode<Label>("%OpponentStatus");
        PlayerLabel = GetNode<Label>("%PlayerLabel");
        PlayerHp = GetNode<ProgressBar>("%PlayerHp");
        PlayerStatus = GetNode<Label>("%PlayerStatus");
        Log = GetNode<RichTextLabel>("%Log");
        Actions = GetNode<HBoxContainer>("%Actions");
        MoveButtons = GetNode<HBoxContainer>("%MoveButtons");
        FightButton = GetNode<Button>("%FightButton");
        Cry = GetNode<AudioStreamPlayer>("%Cry");
    }

    protected void BuildMoveButtons(Action<int> onMovePressed)
    {
        foreach (var child in MoveButtons.GetChildren()) child.QueueFree();
        foreach (var move in State.Player.Moves)
        {
            var button = new Button { Text = $"{move.Name} ({move.Type})" };
            var moveId = move.Id;
            button.Pressed += () => onMovePressed(moveId);
            MoveButtons.AddChild(button);
        }
        var back = new Button { Text = "Back" };
        back.Pressed += () => { MoveButtons.Visible = false; Actions.Visible = true; };
        MoveButtons.AddChild(back);
    }

    /// <summary>Ends the battle scene and hands control back to the overworld. <paramref name="resumeAt"/>
    /// is null after a whiteout — <see cref="OverworldSession.ApplyWhiteout"/> already moved the session to
    /// the Pokémon Center, and OverworldRoot falls back to that area's first walkable tile.</summary>
    protected async Task ReturnToOverworld(PlayerState? resumeAt)
    {
        GameState.Instance.ResumePlayerState = resumeAt;
        await Pause();
        GetTree().ChangeSceneToFile("res://overworld/OverworldRoot.tscn");
    }

    /// <summary>
    /// Adds the Bag button beside Fight, built in code because every other action button is authored in the
    /// scene and there is no item menu to author yet: one press uses the most fitting medicine in the bag
    /// (<see cref="OverworldSession.BestBattleItem"/>) on the active Pokémon, the same
    /// one-unambiguous-action shape the Mart clerk and the service NPCs use. It disables itself whenever
    /// the bag holds nothing that would help, so the choice is never a wasted turn.
    /// </summary>
    protected Button BuildBagButton(Action<BattleAction> takeTurn)
    {
        var button = new Button { Text = "Bag" };
        button.Pressed += () =>
        {
            if (CurrentBattleItem() is not { } choice) return;
            // The item is spent whether or not the simulation finds anything to heal; the button is only
            // offered when there is, and a spent turn with a spent item is the mainline bargain either way.
            GameState.Instance.Session!.SpendItem(choice.ItemId);
            takeTurn(choice.ToAction());
        };
        Actions.AddChild(button);
        BagButton = button;
        return button;
    }

    protected Button? BagButton { get; private set; }

    /// <summary>The item the Bag button would use right now, or null if there is nothing worth using.</summary>
    protected BattleItemChoice? CurrentBattleItem()
    {
        var session = GameState.Instance.Session;
        var data = GameState.Instance.Data;
        if (session is null || data is null) return null;
        return session.BestBattleItem(data, State.Player.Hp, State.Player.MaxHp, State.Player.Status);
    }

    /// <summary>Greys the Bag button out when the bag has nothing to offer the active Pokémon.</summary>
    protected void RefreshBagButton()
    {
        if (BagButton is not null) BagButton.Disabled = CurrentBattleItem() is null;
    }

    /// <summary>A species' name for narration, from the baked data the scene already holds.</summary>
    private static string SpeciesName(int speciesId)
        => GameState.Instance.Data is { } data && data.Species.TryGetValue(speciesId, out var species)
            ? species.Name
            : $"#{speciesId}";

    protected async Task Pause() => await ToSignal(GetTree().CreateTimer(0.8), Timer.SignalName.Timeout);

    // ── event replay: ADR-0006 — the sim narrates, this only animates + logs in strict order ──────────

    protected async Task Animate(BattleEvent battleEvent)
    {
        switch (battleEvent)
        {
            case TurnStarted:
            case TurnEnded:
                break;
            case MoveUsed move:
                Log.AppendText($"\n{NameOf(move.Actor)} used {move.MoveName}!\n");
                await Lunge(SpriteFor(move.Actor));
                break;
            case MoveMissed missed:
                Log.AppendText($"{NameOf(missed.Actor)}'s attack missed!\n");
                break;
            case CriticalHit:
                Log.AppendText("A critical hit!\n");
                break;
            case DamageDealt damage:
                await DrainHp(damage.Target, damage.Amount);
                if (damage.Effectiveness > 1) Log.AppendText("It's super effective!\n");
                else if (damage.Effectiveness is > 0 and < 1) Log.AppendText("It's not very effective...\n");
                else if (damage.Effectiveness == 0) Log.AppendText("It had no effect...\n");
                break;
            case StatStageChanged stage:
                Log.AppendText($"{NameOf(stage.Target)}'s {stage.Stat} {(stage.Stages > 0 ? "rose" : "fell")}!\n");
                break;
            case StatusInflicted status:
                Log.AppendText($"{NameOf(status.Target)} became {status.Status}!\n");
                break;
            case StatusCured cured:
                Log.AppendText($"{NameOf(cured.Target)} recovered from {cured.Status}.\n");
                break;
            case ResidualDamage residual:
                await DrainHp(residual.Target, residual.Amount);
                Log.AppendText($"{NameOf(residual.Target)} was hurt by {residual.Status}.\n");
                break;
            case WeatherStarted weather:
                Log.AppendText($"{weather.Weather} started!\n");
                break;
            case WeatherEnded weather:
                Log.AppendText($"{weather.Weather} ended.\n");
                break;
            case WeatherDamage weather:
                await DrainHp(weather.Target, weather.Amount);
                Log.AppendText($"{NameOf(weather.Target)} was buffeted by {weather.Weather}.\n");
                break;
            case HpRestored healed:
                await RestoreHp(healed.Target, healed.Amount);
                Log.AppendText($"{NameOf(healed.Target)} recovered {healed.Amount} HP.\n");
                break;
            case RecoilDamage recoil:
                await DrainHp(recoil.Target, recoil.Amount);
                Log.AppendText($"{NameOf(recoil.Target)} was hurt by recoil.\n");
                break;
            case FlinchInflicted flinch:
                Log.AppendText($"{NameOf(flinch.Target)} flinched!\n");
                break;
            case PokemonSwitched switched:
                Log.AppendText(switched.Fainted
                    ? $"{NameOf(switched.Actor)} sent out {SpeciesName(switched.ToSpeciesId)}!\n"
                    : $"{NameOf(switched.Actor)} withdrew {SpeciesName(switched.FromSpeciesId)} "
                        + $"and sent out {SpeciesName(switched.ToSpeciesId)}!\n");
                break;
            case ItemUsed item:
                Log.AppendText(item.Actor == BattleActor.Player
                    ? $"You used the {item.ItemName}.\n"
                    : $"The opponent used a {item.ItemName}!\n");
                break;
            case AbilityTriggered ability:
                Log.AppendText($"{NameOf(ability.Actor)}'s {ability.AbilityName} activated!\n");
                break;
            case Fainted fainted:
                await FaintDrop(fainted.Actor);
                Log.AppendText($"{NameOf(fainted.Actor)} fainted!\n");
                break;
            case EffectNotImplemented fallback:
                // ADR-0006: dev-flagged, visible in logs, invisible in release UI — never shown to the player.
                GD.PushWarning($"EffectNotImplemented: {fallback.MoveName} (move {fallback.MoveId})");
                break;
            case MessageShown message:
                Log.AppendText($"{message.Message}\n");
                break;
            default:
                if (!await TryAnimateExtra(battleEvent))
                    Log.AppendText($"{battleEvent.GetType().Name}\n");
                break;
        }
    }

    /// <summary>Hook for events only one concrete screen can emit (wild-only capture events, so far).
    /// Returns whether it handled the event; unhandled events fall back to a generic log line.</summary>
    protected virtual Task<bool> TryAnimateExtra(BattleEvent battleEvent) => Task.FromResult(false);

    protected async Task Lunge(Node2D sprite)
    {
        var start = sprite.Position;
        var toward = sprite == OpponentSprite ? -14 : 14;
        var tween = CreateTween();
        tween.TweenProperty(sprite, "position", start + new Vector2(toward, 0), 0.12);
        tween.TweenProperty(sprite, "position", start, 0.12);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    protected async Task DrainHp(BattleActor target, int amount)
    {
        var bar = HpBarFor(target);
        var tween = CreateTween();
        tween.TweenProperty(bar, "value", Math.Max(0, bar.Value - amount), 0.4);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    protected async Task RestoreHp(BattleActor target, int amount)
    {
        var bar = HpBarFor(target);
        var tween = CreateTween();
        tween.TweenProperty(bar, "value", Math.Min(bar.MaxValue, bar.Value + amount), 0.4);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    protected async Task FaintDrop(BattleActor actor)
    {
        var sprite = SpriteFor(actor);
        var tween = CreateTween();
        tween.TweenProperty(sprite, "modulate:a", 0f, 0.4);
        tween.Parallel().TweenProperty(sprite, "position:y", sprite.Position.Y + 12, 0.4);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    protected void PlayCry(int speciesId)
    {
        var path = ProjectSettings.GlobalizePath($"res://.cache/assets/cries/{speciesId}.ogg");
        if (!File.Exists(path)) return;
        var stream = AudioStreamOggVorbis.LoadFromFile(path);
        if (stream is null) return;
        Cry.Stream = stream;
        Cry.Play();
    }

    protected AnimatedSprite2D SpriteFor(BattleActor actor) => actor == BattleActor.Player ? PlayerSprite : OpponentSprite;

    protected ProgressBar HpBarFor(BattleActor actor) => actor == BattleActor.Player ? PlayerHp : OpponentHp;

    protected string NameOf(BattleActor actor) => actor == BattleActor.Player ? State.Player.Id : State.Opponent.Id;

    protected static Color HpColor(double fraction) => fraction switch
    {
        > 0.5 => Colors.LimeGreen,
        > 0.2 => Colors.Gold,
        _ => Colors.Red,
    };

    protected static string Abbreviate(BattleStatus status) => status switch
    {
        BattleStatus.Sleep => "SLP",
        BattleStatus.Poison or BattleStatus.BadPoison => "PSN",
        BattleStatus.Burn => "BRN",
        BattleStatus.Freeze => "FRZ",
        BattleStatus.Paralysis => "PAR",
        _ => "",
    };

    protected static ulong RandomSeed()
        => unchecked((ulong)DateTime.UtcNow.Ticks ^ (ulong)System.Environment.TickCount64);
}
