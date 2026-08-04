using System;
using Godot;
using ProcPoke;
using ProcPoke.Overworld;

namespace ProcPoke.OverworldView;

/// <summary>The first new-game screen: pick a character and enter a name (GDD §8). Hands the chosen
/// <see cref="PlayerIdentity"/> to <see cref="GameState"/> and moves on to the professor intro.</summary>
public partial class CharacterSelectScreen : Control
{
    private Button _characterA = null!;
    private Button _characterB = null!;
    private LineEdit _nameEdit = null!;
    private Label _error = null!;

    public override void _Ready()
    {
        if (GameState.Instance.PendingNewGameRegion is null)
            throw new InvalidOperationException(
                "CharacterSelectScreen entered with no pending region — launch via RegionViewer's New Game button.");

        _characterA = GetNode<Button>("%CharacterAButton");
        _characterB = GetNode<Button>("%CharacterBButton");
        _nameEdit = GetNode<LineEdit>("%NameEdit");
        _error = GetNode<Label>("%ErrorLabel");

        var group = new ButtonGroup();
        _characterA.ButtonGroup = group;
        _characterB.ButtonGroup = group;

        GetNode<Button>("%ConfirmButton").Pressed += Confirm;
    }

    private void Confirm()
    {
        var character = _characterA.ButtonPressed ? CharacterChoice.CharacterA : CharacterChoice.CharacterB;
        try
        {
            GameState.Instance.PendingIdentity = NewGameIdentity.Create(character, _nameEdit.Text);
        }
        catch (ArgumentException exception)
        {
            _error.Text = exception.Message;
            return;
        }

        GetTree().ChangeSceneToFile("res://overworld/ProfessorIntroScreen.tscn");
    }
}
