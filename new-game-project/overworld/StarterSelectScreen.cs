using System;
using System.IO;
using Godot;
using ProcPoke;
using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Rivals;
using ProcPoke.Overworld;

namespace ProcPoke.OverworldView;

/// <summary>
/// The starter pick — three Poké Balls, one per <see cref="GeneratedRegion.Starters"/> corner, with the
/// rival present and auto-picking the counter-type corner (GDD §7.1a's "the exact Gen 1–5 beat"). Building
/// the player's first Party member and the real <see cref="OverworldSession"/> happens here, once both the
/// chosen identity (from CharacterSelectScreen) and the chosen starter are known.
/// </summary>
public partial class StarterSelectScreen : Control
{
    private const int StarterLevel = 5; // GDD: "Starters begin at level 5."

    private HBoxContainer _corners = null!;
    private Label _status = null!;
    private bool _picked;

    public override void _Ready()
    {
        var region = GameState.Instance.PendingNewGameRegion
            ?? throw new InvalidOperationException("StarterSelectScreen entered with no pending region.");
        var data = GameState.Instance.Data
            ?? throw new InvalidOperationException("StarterSelectScreen entered with no GameData.");
        var identity = GameState.Instance.PendingIdentity
            ?? throw new InvalidOperationException("StarterSelectScreen entered with no chosen identity.");

        _corners = GetNode<HBoxContainer>("%Corners");
        _status = GetNode<Label>("%StatusLabel");

        for (var i = 0; i < region.Starters.Corners.Count; i++)
        {
            var corner = region.Starters.Corners[i];
            var species = data.Species[corner.BaseSpeciesId];
            var button = (Button)_corners.GetChild(i);
            button.Text = $"{species.Name}\n({corner.Type})";
            var icon = LoadIcon(corner.BaseSpeciesId);
            if (icon is not null) button.Icon = icon;

            var cornerIndex = i;
            button.Pressed += () => Pick(region, data, identity, cornerIndex);
        }
    }

    private async void Pick(GeneratedRegion region, GameData data, PlayerIdentity identity, int cornerIndex)
    {
        if (_picked) return;
        _picked = true;
        foreach (var child in _corners.GetChildren())
            if (child is Button button) button.Disabled = true;

        var corner = region.Starters.Corners[cornerIndex];
        var progression = BattleProgressionFactory.AtLevel(data, corner.BaseSpeciesId, StarterLevel);

        var rivalCorner = region.Rivals.ForPlayerCorner(cornerIndex);
        var rivalSpeciesId = rivalCorner.TeamAt(RivalBeat.PostStarter).RivalStarterSpeciesId;

        _status.Text = $"You chose {data.Species[corner.BaseSpeciesId].Name}!\n"
            + $"Your rival chose {data.Species[rivalSpeciesId].Name}!";
        await ToSignal(GetTree().CreateTimer(1.5), Timer.SignalName.Timeout);

        var session = new OverworldSession(region, identity, NewGame.StartingMoney);
        session.GainPokemon(data, progression);
        session.SetStarterCorner(cornerIndex);

        GameState.Instance.Session = session;
        GameState.Instance.PendingNewGameRegion = null;
        GameState.Instance.PendingIdentity = null;
        GameState.Instance.ResumePlayerState = null;
        GetTree().ChangeSceneToFile("res://overworld/OverworldRoot.tscn");
    }

    private static Texture2D? LoadIcon(int speciesId)
    {
        var path = ProjectSettings.GlobalizePath($"res://.cache/assets/icons/{speciesId}.png");
        if (!File.Exists(path)) return null;
        var image = new Image();
        return image.Load(path) == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
    }
}
