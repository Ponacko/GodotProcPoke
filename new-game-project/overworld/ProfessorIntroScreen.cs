using System;
using System.Linq;
using Godot;
using ProcPoke;

namespace ProcPoke.OverworldView;

/// <summary>
/// The generated-per-seed framing NPC (GDD §7.1a): a name/gender rolled fresh per playthrough (not part
/// of the deterministic region — the professor is presentation flavor, not a generation-pass fact) who
/// delivers the opening monologue before the starter pick.
/// </summary>
public partial class ProfessorIntroScreen : Control
{
    public override void _Ready()
    {
        var region = GameState.Instance.PendingNewGameRegion
            ?? throw new InvalidOperationException("ProfessorIntroScreen entered with no pending region.");
        var data = GameState.Instance.Data
            ?? throw new InvalidOperationException("ProfessorIntroScreen entered with no GameData.");

        var (name, pronoun) = ProfessorIdentity.Generate(new Random());

        var wildSpeciesId = region.Dex.Entries.First(entry => entry.Number > 9).SpeciesId;
        var wildSpeciesName = data.Species[wildSpeciesId].Name;

        GetNode<Label>("%NameLabel").Text = $"Professor {name}";
        GetNode<RichTextLabel>("%Monologue").Text =
            $"Welcome to the world of Pokémon! My name is {name}, but everyone calls me the {region.Names.Of(region.Graph.StartAreaId)} Professor.\n\n"
            + $"This world is inhabited far and wide by creatures called Pokémon. Why, just outside, {pronoun} could point out a wild {wildSpeciesName}!\n\n"
            + "Before you begin your own journey, let's get you a partner.";

        GetNode<Button>("%ContinueButton").Pressed +=
            () => GetTree().ChangeSceneToFile("res://overworld/StarterSelectScreen.tscn");
    }
}
