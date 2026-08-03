using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ProcPoke;
using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Overworld;

namespace ProcPoke.BattleView;

/// <summary>
/// The real Gen 5-style wild battle presentation (ADR-0006: the sim resolves a turn, this replays the
/// returned event stream as animation + log text, strictly in order). Entered only via a scene swap from
/// <c>OverworldRootController</c>, which stashes the pending <see cref="WildBattleTransitionRequest"/> on
/// <see cref="GameState"/> before switching here.
/// </summary>
public partial class BattleScreen : Control
{
    private ColorRect _backdrop = null!;
    private AnimatedSprite2D _opponentSprite = null!;
    private AnimatedSprite2D _playerSprite = null!;
    private Sprite2D _ball = null!;
    private Label _opponentLabel = null!;
    private ProgressBar _opponentHp = null!;
    private Label _opponentStatus = null!;
    private Label _playerLabel = null!;
    private ProgressBar _playerHp = null!;
    private Label _playerStatus = null!;
    private RichTextLabel _log = null!;
    private HBoxContainer _actions = null!;
    private HBoxContainer _moveButtons = null!;
    private Button _fightButton = null!;
    private Button _ballButton = null!;
    private Button _runButton = null!;
    private AudioStreamPlayer _cry = null!;

    private GameData _data = null!;
    private WildBattleTransitionRequest _request = null!;
    private BattleProgressionState _progression = null!;
    private WildBattleSession _session = null!;
    private BattleState _state = null!;
    private bool _busy;

    public override void _Ready()
    {
        BindNodes();

        _data = GameState.Instance.Data
            ?? throw new InvalidOperationException("BattleScreen entered with no GameData — launch via RegionViewer's Play button.");
        _request = GameState.Instance.PendingBattleRequest
            ?? throw new InvalidOperationException("BattleScreen entered with no pending battle request.");
        GameState.Instance.PendingBattleRequest = null;

        _progression = GameState.Instance.PlayerProgression ?? DefaultProgression(_data);
        GameState.Instance.PlayerProgression = _progression;

        var player = BattlePokemonFactory.Create(_data, _progression.SpeciesId, _progression.Level,
            new BattlePokemonBuildOptions { MoveIds = _progression.MoveIds, EffortValues = _progression.EffortValues });
        _session = WildBattleSession.Start(_data, player, _request.Battle, RandomSeed());
        _state = _session.State;

        var biome = GameState.Instance.Session!.Region.Biomes.Of(_request.Encounter.AreaId);
        _backdrop.Color = BattleBackdropPalette.Of(biome);

        var opponentSprite = BattleSpriteLoader.Load(_state.Opponent.SpeciesId, preferBack: false);
        _opponentSprite.SpriteFrames = opponentSprite.Frames;
        _opponentSprite.Play("idle");
        var playerSprite = BattleSpriteLoader.Load(_state.Player.SpeciesId, preferBack: true);
        _playerSprite.SpriteFrames = playerSprite.Frames;
        _playerSprite.FlipH = !playerSprite.IsBackArt; // mirror the front sprite only when no real back art exists
        _playerSprite.Play("idle");
        PlayCry(_state.Opponent.SpeciesId);

        _fightButton.Pressed += () => { _actions.Visible = false; _moveButtons.Visible = true; };
        _ballButton.Pressed += () => ResolveAction(new ThrowBallAction("Poké Ball"));
        _runButton.Pressed += Flee;

        BuildMoveButtons();
        RefreshLabels();
        _log.Text = $"A wild {_data.Species[_state.Opponent.SpeciesId].Name} appeared!\n";
    }

    private void BindNodes()
    {
        _backdrop = GetNode<ColorRect>("%Backdrop");
        _opponentSprite = GetNode<AnimatedSprite2D>("%OpponentSprite");
        _playerSprite = GetNode<AnimatedSprite2D>("%PlayerSprite");
        _ball = GetNode<Sprite2D>("%Ball");
        _opponentLabel = GetNode<Label>("%OpponentLabel");
        _opponentHp = GetNode<ProgressBar>("%OpponentHp");
        _opponentStatus = GetNode<Label>("%OpponentStatus");
        _playerLabel = GetNode<Label>("%PlayerLabel");
        _playerHp = GetNode<ProgressBar>("%PlayerHp");
        _playerStatus = GetNode<Label>("%PlayerStatus");
        _log = GetNode<RichTextLabel>("%Log");
        _actions = GetNode<HBoxContainer>("%Actions");
        _moveButtons = GetNode<HBoxContainer>("%MoveButtons");
        _fightButton = GetNode<Button>("%FightButton");
        _ballButton = GetNode<Button>("%BallButton");
        _runButton = GetNode<Button>("%RunButton");
        _cry = GetNode<AudioStreamPlayer>("%Cry");
    }

    private void BuildMoveButtons()
    {
        foreach (var child in _moveButtons.GetChildren()) child.QueueFree();
        foreach (var move in _state.Player.Moves)
        {
            var button = new Button { Text = $"{move.Name} ({move.Type})" };
            var moveId = move.Id;
            button.Pressed += () => ResolveAction(new UseMoveAction(moveId));
            _moveButtons.AddChild(button);
        }
        var back = new Button { Text = "Back" };
        back.Pressed += () => { _moveButtons.Visible = false; _actions.Visible = true; };
        _moveButtons.AddChild(back);
    }

    private async void Flee()
    {
        if (_busy || _session.Outcome != WildBattleOutcome.Ongoing) return;
        _log.AppendText("\nGot away safely!\n");
        await ReturnToOverworld(_request.ReturnState);
    }

    private async void ResolveAction(BattleAction action)
    {
        if (_busy || _session.Outcome != WildBattleOutcome.Ongoing) return;
        _busy = true;
        SetActionsEnabled(false);
        _moveButtons.Visible = false;

        var result = _session.TakeTurn(action);
        _state = result.State;
        foreach (var battleEvent in result.Events)
            await Animate(battleEvent);
        RefreshLabels();
        _busy = false;

        switch (_session.Outcome)
        {
            case WildBattleOutcome.PlayerWon:
                ApplyVictory();
                await ReturnToOverworld(_request.ReturnState);
                break;
            case WildBattleOutcome.PlayerLost:
                GameState.Instance.Session!.ApplyWhiteout();
                await ReturnToOverworld(resumeAt: null);
                break;
            case WildBattleOutcome.Captured:
                await ReturnToOverworld(_request.ReturnState);
                break;
            default:
                _actions.Visible = true;
                SetActionsEnabled(true);
                break;
        }
    }

    /// <summary>Ends the battle scene and hands control back to the overworld. <paramref name="resumeAt"/>
    /// is null after a whiteout — <see cref="OverworldSession.ApplyWhiteout"/> already moved the session to
    /// the Pokémon Center, and OverworldRoot falls back to that area's first walkable tile.</summary>
    private async Task ReturnToOverworld(PlayerState? resumeAt)
    {
        GameState.Instance.ResumePlayerState = resumeAt;
        await Pause();
        GetTree().ChangeSceneToFile("res://overworld/OverworldRoot.tscn");
    }

    private void ApplyVictory()
    {
        var reward = _session.ApplyVictoryRewards(_progression);
        _progression = reward.Progression;
        GameState.Instance.PlayerProgression = _progression;

        _log.AppendText($"\n{_state.Player.Id} gained {reward.Experience.ExperienceGained} EXP!\n");
        if (reward.Experience.NewLevel > reward.Experience.PreviousLevel)
            _log.AppendText($"{_data.Species[_progression.SpeciesId].Name} grew to Lv{reward.Experience.NewLevel}!\n");
        foreach (var moveId in reward.Moves.LearnedMoveIds)
            _log.AppendText($"Learned {_data.Moves[moveId].Name}!\n");
        if (reward.Evolution is { } evolution)
            _log.AppendText(
                $"{_data.Species[evolution.FromSpeciesId].Name} evolved into {_data.Species[evolution.ToSpeciesId].Name}!\n");
    }

    private async Task Pause() => await ToSignal(GetTree().CreateTimer(0.8), Timer.SignalName.Timeout);

    private void SetActionsEnabled(bool enabled)
    {
        _fightButton.Disabled = !enabled;
        _ballButton.Disabled = !enabled;
        _runButton.Disabled = !enabled;
        foreach (var child in _moveButtons.GetChildren())
            if (child is Button button) button.Disabled = !enabled;
    }

    private void RefreshLabels()
    {
        _opponentLabel.Text = $"{_state.Opponent.Id}  Lv{_state.Opponent.Level}";
        _opponentHp.MaxValue = _state.Opponent.MaxHp;
        _opponentHp.Value = _state.Opponent.Hp;
        _opponentHp.SelfModulate = HpColor((double)_state.Opponent.Hp / _state.Opponent.MaxHp);
        _opponentStatus.Text = Abbreviate(_state.Opponent.Status);

        _playerLabel.Text = $"{_state.Player.Id}  Lv{_state.Player.Level}";
        _playerHp.MaxValue = _state.Player.MaxHp;
        _playerHp.Value = _state.Player.Hp;
        _playerHp.SelfModulate = HpColor((double)_state.Player.Hp / _state.Player.MaxHp);
        _playerStatus.Text = Abbreviate(_state.Player.Status);
    }

    // ── event replay: ADR-0006 — the sim narrates, this only animates + logs in strict order ──────────

    private async Task Animate(BattleEvent battleEvent)
    {
        switch (battleEvent)
        {
            case TurnStarted:
            case TurnEnded:
                break;
            case MoveUsed move:
                _log.AppendText($"\n{NameOf(move.Actor)} used {move.MoveName}!\n");
                await Lunge(SpriteFor(move.Actor));
                break;
            case MoveMissed missed:
                _log.AppendText($"{NameOf(missed.Actor)}'s attack missed!\n");
                break;
            case CriticalHit:
                _log.AppendText("A critical hit!\n");
                break;
            case DamageDealt damage:
                await DrainHp(damage.Target, damage.Amount);
                if (damage.Effectiveness > 1) _log.AppendText("It's super effective!\n");
                else if (damage.Effectiveness is > 0 and < 1) _log.AppendText("It's not very effective...\n");
                else if (damage.Effectiveness == 0) _log.AppendText("It had no effect...\n");
                break;
            case StatStageChanged stage:
                _log.AppendText($"{NameOf(stage.Target)}'s {stage.Stat} {(stage.Stages > 0 ? "rose" : "fell")}!\n");
                break;
            case StatusInflicted status:
                _log.AppendText($"{NameOf(status.Target)} became {status.Status}!\n");
                break;
            case StatusCured cured:
                _log.AppendText($"{NameOf(cured.Target)} recovered from {cured.Status}.\n");
                break;
            case ResidualDamage residual:
                await DrainHp(residual.Target, residual.Amount);
                _log.AppendText($"{NameOf(residual.Target)} was hurt by {residual.Status}.\n");
                break;
            case WeatherStarted weather:
                _log.AppendText($"{weather.Weather} started!\n");
                break;
            case WeatherEnded weather:
                _log.AppendText($"{weather.Weather} ended.\n");
                break;
            case WeatherDamage weather:
                await DrainHp(weather.Target, weather.Amount);
                _log.AppendText($"{NameOf(weather.Target)} was buffeted by {weather.Weather}.\n");
                break;
            case HpRestored healed:
                await RestoreHp(healed.Target, healed.Amount);
                _log.AppendText($"{NameOf(healed.Target)} recovered {healed.Amount} HP.\n");
                break;
            case RecoilDamage recoil:
                await DrainHp(recoil.Target, recoil.Amount);
                _log.AppendText($"{NameOf(recoil.Target)} was hurt by recoil.\n");
                break;
            case FlinchInflicted flinch:
                _log.AppendText($"{NameOf(flinch.Target)} flinched!\n");
                break;
            case BallThrown ball:
                _log.AppendText($"{NameOf(ball.Actor)} threw a {ball.BallId}!\n");
                await ThrowBallAnimation();
                break;
            case CaptureShake shake:
                await ShakeBall(shake.Succeeded);
                break;
            case CaptureFailed failed:
                _log.AppendText($"Oh no! The Pokémon broke free after {failed.SuccessfulShakes} shake(s)!\n");
                await PopOutOfBall();
                break;
            case PokemonCaptured:
                _log.AppendText("Gotcha! The wild Pokémon was caught!\n");
                PlayCry(_state.Opponent.SpeciesId);
                await CaptureFlash();
                break;
            case AbilityTriggered ability:
                _log.AppendText($"{NameOf(ability.Actor)}'s {ability.AbilityName} activated!\n");
                break;
            case Fainted fainted:
                await FaintDrop(fainted.Actor);
                _log.AppendText($"{NameOf(fainted.Actor)} fainted!\n");
                break;
            case EffectNotImplemented fallback:
                // ADR-0006: dev-flagged, visible in logs, invisible in release UI — never shown to the player.
                GD.PushWarning($"EffectNotImplemented: {fallback.MoveName} (move {fallback.MoveId})");
                break;
            case MessageShown message:
                _log.AppendText($"{message.Message}\n");
                break;
            default:
                _log.AppendText($"{battleEvent.GetType().Name}\n");
                break;
        }
    }

    private async Task Lunge(Node2D sprite)
    {
        var start = sprite.Position;
        var toward = sprite == _opponentSprite ? -14 : 14;
        var tween = CreateTween();
        tween.TweenProperty(sprite, "position", start + new Vector2(toward, 0), 0.12);
        tween.TweenProperty(sprite, "position", start, 0.12);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private async Task DrainHp(BattleActor target, int amount)
    {
        var bar = HpBarFor(target);
        var tween = CreateTween();
        tween.TweenProperty(bar, "value", Math.Max(0, bar.Value - amount), 0.4);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private async Task RestoreHp(BattleActor target, int amount)
    {
        var bar = HpBarFor(target);
        var tween = CreateTween();
        tween.TweenProperty(bar, "value", Math.Min(bar.MaxValue, bar.Value + amount), 0.4);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private async Task ThrowBallAnimation()
    {
        _ball.Visible = true;
        _ball.Modulate = Colors.White;
        _ball.Scale = Vector2.One;
        _ball.Rotation = 0;
        _ball.Position = _playerSprite.Position;
        var tween = CreateTween();
        tween.TweenProperty(_ball, "position", _opponentSprite.Position, 0.5)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private async Task ShakeBall(bool succeeded)
    {
        var wobble = succeeded ? 0.35f : 0.6f;
        var tween = CreateTween();
        tween.TweenProperty(_ball, "rotation", wobble, 0.15);
        tween.TweenProperty(_ball, "rotation", -wobble, 0.15);
        tween.TweenProperty(_ball, "rotation", 0f, 0.1);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private Task PopOutOfBall()
    {
        _ball.Visible = false;
        _opponentSprite.Modulate = Colors.White;
        _opponentSprite.Scale = Vector2.One;
        return Task.CompletedTask;
    }

    private async Task CaptureFlash()
    {
        var tween = CreateTween();
        tween.TweenProperty(_opponentSprite, "scale", Vector2.Zero, 0.3);
        tween.Parallel().TweenProperty(_ball, "modulate:a", 0f, 0.2);
        await ToSignal(tween, Tween.SignalName.Finished);
        _ball.Visible = false;
    }

    private async Task FaintDrop(BattleActor actor)
    {
        var sprite = SpriteFor(actor);
        var tween = CreateTween();
        tween.TweenProperty(sprite, "modulate:a", 0f, 0.4);
        tween.Parallel().TweenProperty(sprite, "position:y", sprite.Position.Y + 12, 0.4);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private void PlayCry(int speciesId)
    {
        var path = ProjectSettings.GlobalizePath($"res://.cache/assets/cries/{speciesId}.ogg");
        if (!File.Exists(path)) return;
        var stream = AudioStreamOggVorbis.LoadFromFile(path);
        if (stream is null) return;
        _cry.Stream = stream;
        _cry.Play();
    }

    private AnimatedSprite2D SpriteFor(BattleActor actor) => actor == BattleActor.Player ? _playerSprite : _opponentSprite;

    private ProgressBar HpBarFor(BattleActor actor) => actor == BattleActor.Player ? _playerHp : _opponentHp;

    private string NameOf(BattleActor actor) => actor == BattleActor.Player ? _state.Player.Id : _state.Opponent.Id;

    private static Color HpColor(double fraction) => fraction switch
    {
        > 0.5 => Colors.LimeGreen,
        > 0.2 => Colors.Gold,
        _ => Colors.Red,
    };

    private static string Abbreviate(BattleStatus status) => status switch
    {
        BattleStatus.Sleep => "SLP",
        BattleStatus.Poison or BattleStatus.BadPoison => "PSN",
        BattleStatus.Burn => "BRN",
        BattleStatus.Freeze => "FRZ",
        BattleStatus.Paralysis => "PAR",
        _ => "",
    };

    private static ulong RandomSeed()
        => unchecked((ulong)DateTime.UtcNow.Ticks ^ (ulong)System.Environment.TickCount64);

    private static BattleProgressionState DefaultProgression(GameData data)
    {
        const int speciesId = 1; // arbitrary starter stand-in until Phase 5's real starter/party flow exists
        const int level = 5;
        var player = BattlePokemonFactory.Create(data, speciesId, level);
        var growth = data.GrowthRates[data.Species[speciesId].GrowthRate];
        return new BattleProgressionState
        {
            SpeciesId = speciesId,
            Level = level,
            TotalExperience = growth.CumulativeExperience[level],
            EffortValues = new StatBlock(0, 0, 0, 0, 0, 0),
            MoveIds = player.Moves.Select(move => move.Id).ToArray(),
        };
    }
}
