using System;
using System.Threading.Tasks;
using Godot;
using ProcPoke;
using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Overworld;

namespace ProcPoke.BattleView;

/// <summary>
/// The real Gen 5-style wild battle presentation (ADR-0006: the sim resolves a turn, this replays the
/// returned event stream as animation + log text, strictly in order) via <see cref="BattleScreenBase"/>.
/// Entered only via a scene swap from <c>OverworldRootController</c>, which stashes the pending
/// <see cref="WildBattleTransitionRequest"/> on <see cref="GameState"/> before switching here.
/// </summary>
public partial class BattleScreen : BattleScreenBase
{
    private Sprite2D _ball = null!;
    private Button _ballButton = null!;
    private Button _runButton = null!;

    private GameData _data = null!;
    private WildBattleTransitionRequest _request = null!;
    private BattleProgressionState _progression = null!;
    private WildBattleSession _session = null!;
    private bool _busy;

    public override void _Ready()
    {
        BindCommonNodes();
        _ball = GetNode<Sprite2D>("%Ball");
        _ballButton = GetNode<Button>("%BallButton");
        _runButton = GetNode<Button>("%RunButton");

        _data = GameState.Instance.Data
            ?? throw new InvalidOperationException("BattleScreen entered with no GameData — launch via RegionViewer's Play button.");
        _request = GameState.Instance.PendingBattleRequest
            ?? throw new InvalidOperationException("BattleScreen entered with no pending battle request.");
        GameState.Instance.PendingBattleRequest = null;

        var party = GameState.Instance.Session?.Party
            ?? throw new InvalidOperationException("BattleScreen entered with no active session/party.");
        if (party.Members.Count == 0)
            throw new InvalidOperationException("BattleScreen entered with an empty party — no Pokémon to send out.");
        _progression = party.Members[0];

        var player = BattlePokemonFactory.Create(_data, _progression.SpeciesId, _progression.Level,
            new BattlePokemonBuildOptions { MoveIds = _progression.MoveIds, EffortValues = _progression.EffortValues });
        _session = WildBattleSession.Start(_data, player, _request.Battle, RandomSeed());
        State = _session.State;

        var biome = GameState.Instance.Session!.Region.Biomes.Of(_request.Encounter.AreaId);
        Backdrop.Color = BattleBackdropPalette.Of(biome);

        var opponentSprite = BattleSpriteLoader.Load(State.Opponent.SpeciesId, preferBack: false);
        OpponentSprite.SpriteFrames = opponentSprite.Frames;
        OpponentSprite.Play("idle");
        var playerSprite = BattleSpriteLoader.Load(State.Player.SpeciesId, preferBack: true);
        PlayerSprite.SpriteFrames = playerSprite.Frames;
        PlayerSprite.FlipH = !playerSprite.IsBackArt; // mirror the front sprite only when no real back art exists
        PlayerSprite.Play("idle");
        PlayCry(State.Opponent.SpeciesId);

        FightButton.Pressed += () => { Actions.Visible = false; MoveButtons.Visible = true; };
        _ballButton.Pressed += () => ResolveAction(new ThrowBallAction("Poké Ball"));
        _runButton.Pressed += Flee;

        BuildMoveButtons(moveId => ResolveAction(new UseMoveAction(moveId)));
        RefreshLabels();
        Log.Text = $"A wild {_data.Species[State.Opponent.SpeciesId].Name} appeared!\n";
    }

    private async void Flee()
    {
        if (_busy || _session.Outcome != WildBattleOutcome.Ongoing) return;
        Log.AppendText("\nGot away safely!\n");
        await ReturnToOverworld(_request.ReturnState);
    }

    private async void ResolveAction(BattleAction action)
    {
        if (_busy || _session.Outcome != WildBattleOutcome.Ongoing) return;
        _busy = true;
        SetActionsEnabled(false);
        MoveButtons.Visible = false;

        var result = _session.TakeTurn(action);
        State = result.State;
        foreach (var battleEvent in result.Events)
            await Animate(battleEvent);
        RefreshLabels();
        _busy = false;

        switch (_session.Outcome)
        {
            case WildBattleOutcome.PlayerWon:
                ApplyVictory();
                ResolveLegendaryIfPresent();
                await ReturnToOverworld(_request.ReturnState);
                break;
            case WildBattleOutcome.PlayerLost:
                GameState.Instance.Session!.ApplyWhiteout();
                await ReturnToOverworld(resumeAt: null);
                break;
            case WildBattleOutcome.Captured:
                ResolveLegendaryIfPresent();
                await ReturnToOverworld(_request.ReturnState);
                break;
            default:
                Actions.Visible = true;
                SetActionsEnabled(true);
                break;
        }
    }

    private void ResolveLegendaryIfPresent()
    {
        if (_request.Legendary is { } legendary)
            GameState.Instance.Session!.MarkLegendaryResolved(legendary.SpeciesId);
    }

    private void ApplyVictory()
    {
        var reward = _session.ApplyVictoryRewards(_progression);
        _progression = reward.Progression;
        GameState.Instance.Session!.Party.Replace(0, _progression);

        Log.AppendText($"\n{State.Player.Id} gained {reward.Experience.ExperienceGained} EXP!\n");
        if (reward.Experience.NewLevel > reward.Experience.PreviousLevel)
            Log.AppendText($"{_data.Species[_progression.SpeciesId].Name} grew to Lv{reward.Experience.NewLevel}!\n");
        foreach (var moveId in reward.Moves.LearnedMoveIds)
            Log.AppendText($"Learned {_data.Moves[moveId].Name}!\n");
        if (reward.Evolution is { } evolution)
            Log.AppendText(
                $"{_data.Species[evolution.FromSpeciesId].Name} evolved into {_data.Species[evolution.ToSpeciesId].Name}!\n");
    }

    private void SetActionsEnabled(bool enabled)
    {
        FightButton.Disabled = !enabled;
        _ballButton.Disabled = !enabled;
        _runButton.Disabled = !enabled;
        foreach (var child in MoveButtons.GetChildren())
            if (child is Button button) button.Disabled = !enabled;
    }

    private void RefreshLabels()
    {
        OpponentLabel.Text = $"{State.Opponent.Id}  Lv{State.Opponent.Level}";
        OpponentHp.MaxValue = State.Opponent.MaxHp;
        OpponentHp.Value = State.Opponent.Hp;
        OpponentHp.SelfModulate = HpColor((double)State.Opponent.Hp / State.Opponent.MaxHp);
        OpponentStatus.Text = Abbreviate(State.Opponent.Status);

        PlayerLabel.Text = $"{State.Player.Id}  Lv{State.Player.Level}";
        PlayerHp.MaxValue = State.Player.MaxHp;
        PlayerHp.Value = State.Player.Hp;
        PlayerHp.SelfModulate = HpColor((double)State.Player.Hp / State.Player.MaxHp);
        PlayerStatus.Text = Abbreviate(State.Player.Status);
    }

    // ── wild-only events: catching. Everything else replays through BattleScreenBase.Animate ──────────

    protected override async Task<bool> TryAnimateExtra(BattleEvent battleEvent)
    {
        switch (battleEvent)
        {
            case BallThrown ball:
                Log.AppendText($"{NameOf(ball.Actor)} threw a {ball.BallId}!\n");
                await ThrowBallAnimation();
                return true;
            case CaptureShake shake:
                await ShakeBall(shake.Succeeded);
                return true;
            case CaptureFailed failed:
                Log.AppendText($"Oh no! The Pokémon broke free after {failed.SuccessfulShakes} shake(s)!\n");
                await PopOutOfBall();
                return true;
            case PokemonCaptured:
                Log.AppendText("Gotcha! The wild Pokémon was caught!\n");
                PlayCry(State.Opponent.SpeciesId);
                await CaptureFlash();
                return true;
            default:
                return false;
        }
    }

    private async Task ThrowBallAnimation()
    {
        _ball.Visible = true;
        _ball.Modulate = Colors.White;
        _ball.Scale = Vector2.One;
        _ball.Rotation = 0;
        _ball.Position = PlayerSprite.Position;
        var tween = CreateTween();
        tween.TweenProperty(_ball, "position", OpponentSprite.Position, 0.5)
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
        OpponentSprite.Modulate = Colors.White;
        OpponentSprite.Scale = Vector2.One;
        return Task.CompletedTask;
    }

    private async Task CaptureFlash()
    {
        var tween = CreateTween();
        tween.TweenProperty(OpponentSprite, "scale", Vector2.Zero, 0.3);
        tween.Parallel().TweenProperty(_ball, "modulate:a", 0f, 0.2);
        await ToSignal(tween, Tween.SignalName.Finished);
        _ball.Visible = false;
    }
}
