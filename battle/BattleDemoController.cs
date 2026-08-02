using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProcPoke.Battle;
using ProcPoke.Data;

namespace ProcPoke.BattleView;

/// <summary>
/// Small presentation harness for the headless battle contract. It is intentionally a separate scene:
/// the region viewer remains the generation/debug entry point while this scene proves that an event stream
/// can drive a battle UI without putting rules into Godot nodes.
/// </summary>
public partial class BattleDemoController : Control
{
    private GameData _data = null!;
    private BattleState _state = null!;
    private BattleRandom _random = null!;
    private Label _playerLabel = null!;
    private Label _opponentLabel = null!;
    private ProgressBar _playerHp = null!;
    private ProgressBar _opponentHp = null!;
    private RichTextLabel _log = null!;
    private HBoxContainer _moveButtons = null!;
    private Label _status = null!;

    public override void _Ready()
    {
        BuildUi();
        try
        {
            _data = GameDataLoader.Load(ProjectSettings.GlobalizePath("res://data"));
            ResetBattle();
        }
        catch (Exception exception)
        {
            _status.Text = $"Battle setup failed: {exception.Message}";
            GD.PushError(exception.ToString());
        }
    }

    private void BuildUi()
    {
        var margin = new MarginContainer
        {
            ThemeOverrideConstants = { ["margin_left"] = 18, ["margin_top"] = 18, ["margin_right"] = 18, ["margin_bottom"] = 18 },
        };
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(margin);

        var column = new VBoxContainer { ThemeOverrideConstants = { ["separation"] = 10 } };
        margin.AddChild(column);
        column.AddChild(new Label { Text = "ProcPoke — Battle Event Stream Demo", ThemeOverrideFontSizes = { ["font_size"] = 22 } });

        _status = new Label { Text = "Loading baked battle data…" };
        column.AddChild(_status);

        var field = new GridContainer { Columns = 2, ThemeOverrideConstants = { ["h_separation"] = 24, ["v_separation"] = 8 } };
        column.AddChild(field);
        _opponentLabel = new Label();
        _opponentHp = HpBar();
        field.AddChild(_opponentLabel);
        field.AddChild(_opponentHp);
        _playerLabel = new Label();
        _playerHp = HpBar();
        field.AddChild(_playerLabel);
        field.AddChild(_playerHp);

        _log = new RichTextLabel { BbcodeEnabled = false, FitContent = false, CustomMinimumSize = new Vector2(0, 220) };
        column.AddChild(_log);

        _moveButtons = new HBoxContainer { ThemeOverrideConstants = { ["separation"] = 8 } };
        column.AddChild(_moveButtons);
        var reset = new Button { Text = "Reset battle" };
        reset.Pressed += ResetBattle;
        column.AddChild(reset);
    }

    private static ProgressBar HpBar() => new()
    {
        ShowPercentage = true,
        CustomMinimumSize = new Vector2(240, 28),
    };

    private void ResetBattle()
    {
        if (_data is null) return;
        _random = new BattleRandom(0xBA771EUL);
        _state = new BattleState
        {
            Player = DemoPokemon("player", 25, [33, 45, 73, 22], PokeType.Grass, 80),
            Opponent = DemoPokemon("wild Pidgey", 16, [33, 16, 98], PokeType.Flying, 60),
        };
        foreach (var child in _moveButtons.GetChildren()) child.QueueFree();
        foreach (var move in _state.Player.Moves)
        {
            var button = new Button { Text = move.Name, TooltipText = $"{move.Type}  {move.Power?.ToString() ?? "status"}" };
            var moveId = move.Id;
            button.Pressed += () => Resolve(moveId);
            _moveButtons.AddChild(button);
        }
        _log.Text = "Choose a move. The opponent uses Wild AI.\n";
        _status.Text = "Battle started";
        RefreshUi();
    }

    private BattlePokemon DemoPokemon(string id, int speciesId, IReadOnlyList<int> moveIds, PokeType type, int hp)
        => new()
        {
            Id = id,
            SpeciesId = speciesId,
            Level = id == "player" ? 10 : 8,
            Speed = id == "player" ? 45 : 40,
            Attack = 35,
            Defense = 30,
            SpecialAttack = 35,
            SpecialDefense = 30,
            Types = [type],
            MaxHp = hp,
            Hp = hp,
            Moves = moveIds.Select(id => _data.Moves[id]).ToArray(),
        };

    private void Resolve(int moveId)
    {
        if (_state.Player.IsFainted || _state.Opponent.IsFainted) return;
        var opponentAction = BattleAi.ChooseAction(_state, BattleActor.Opponent, BattleAiTier.Wild, _random, _data.TypeChart);
        var result = BattleTurnResolver.Resolve(
            _state, new UseMoveAction(moveId), opponentAction, _random, _data.TypeChart);
        _state = result.State;
        foreach (var battleEvent in result.Events)
            _log.AppendText(Describe(battleEvent) + "\n");
        RefreshUi();
        if (_state.Player.IsFainted || _state.Opponent.IsFainted)
            _status.Text = _state.Player.IsFainted ? "You lost the demo battle." : "You won the demo battle.";
    }

    private void RefreshUi()
    {
        _opponentLabel.Text = $"{_state.Opponent.Id}  Lv{_state.Opponent.Level}";
        _playerLabel.Text = $"{_state.Player.Id}  Lv{_state.Player.Level}";
        _opponentHp.MaxValue = _state.Opponent.MaxHp;
        _opponentHp.Value = _state.Opponent.Hp;
        _playerHp.MaxValue = _state.Player.MaxHp;
        _playerHp.Value = _state.Player.Hp;
        foreach (var child in _moveButtons.GetChildren())
            if (child is Button button) button.Disabled = _state.Player.IsFainted || _state.Opponent.IsFainted;
    }

    private static string Describe(BattleEvent battleEvent) => battleEvent switch
    {
        TurnStarted started => $"\nTurn {started.Turn}",
        MoveUsed move => $"{move.Actor}: {move.MoveName}",
        MoveMissed missed => $"{missed.Actor}'s move missed.",
        CriticalHit => "A critical hit!",
        DamageDealt damage => $"  {damage.Amount} damage ({damage.Effectiveness:0.##}×)",
        StatStageChanged stage => $"  {stage.Target} {stage.Stat} stage → {stage.Stages}",
        StatusInflicted status => $"  {status.Target} became {status.Status}.",
        StatusCured cured => $"  {cured.Target} recovered from {cured.Status}.",
        ResidualDamage residual => $"  {residual.Target} took {residual.Amount} residual damage.",
        WeatherStarted weather => $"  {weather.Weather} started.",
        WeatherEnded weather => $"  {weather.Weather} ended.",
        WeatherDamage weather => $"  {weather.Target} took {weather.Amount} weather damage.",
        HpRestored healed => $"  {healed.Target} recovered {healed.Amount} HP.",
        RecoilDamage recoil => $"  {recoil.Target} took {recoil.Amount} recoil.",
        FlinchInflicted flinch => $"  {flinch.Target} flinched.",
        Fainted fainted => $"{fainted.Actor} fainted.",
        EffectNotImplemented fallback => $"  [fallback] {fallback.MoveName}",
        MessageShown message => $"  {message.Message}",
        _ => battleEvent.GetType().Name,
    };
}
