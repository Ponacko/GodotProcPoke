using System;
using System.Threading.Tasks;
using Godot;
using ProcPoke;
using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.OverworldView;

namespace ProcPoke.Debug;

/// <summary>
/// Throwaway headless smoke driver for manual verification only (not part of the shipped game or the
/// automated test suite) — run via
/// <c>godot --headless --path . --script res://debug/NewGameFlowSmoke.cs</c>. Drives the real
/// CharacterSelectScreen -&gt; ProfessorIntroScreen -&gt; StarterSelectScreen -&gt; OverworldRoot chain (no
/// GUI automation available in this environment) and confirms the real Party ends up seeded with exactly
/// the chosen starter at level 5. Battle-loop correctness against a real Party is covered by the existing
/// <see cref="OverworldBattleSmoke"/> (BattleScreen reads generically from Session.Party regardless of how
/// it was populated, so that script's pass is sufficient evidence the two compose correctly).
/// </summary>
public partial class NewGameFlowSmoke : SceneTree
{
    public override void _Initialize()
    {
        Callable.From(RunAsync).CallDeferred();
    }

    private async void RunAsync()
    {
        try
        {
            await Run();
            GD.Print("SMOKE: PASS");
        }
        catch (Exception exception)
        {
            GD.Print($"SMOKE: FAIL — {exception}");
        }
        Quit();
    }

    private async Task Run()
    {
        var data = GameDataLoader.Load(ProjectSettings.GlobalizePath("res://data"));
        var settings = new GenerationSettings { Seed = 7, BadgeCount = 8, DexSize = 150, RosterCap = 5 };
        var region = RegionGenerator.Generate(settings, data);

        GameState.Instance.Data = data;
        GameState.Instance.PendingNewGameRegion = region;
        ChangeSceneToFile("res://overworld/CharacterSelectScreen.tscn");

        var characterSelect = await WaitForScene<CharacterSelectScreen>();
        GD.Print("SMOKE: character select loaded");
        characterSelect.GetNode<LineEdit>("%NameEdit").Text = "Smoke";
        // A toggle-mode button's pressed *state* flips via the real input pipeline, not by emitting its
        // "pressed" signal, and ButtonGroup's mutual exclusivity is also input-driven — set both states
        // explicitly rather than relying on the group to un-press CharacterA on its own.
        characterSelect.GetNode<Button>("%CharacterAButton").ButtonPressed = false;
        characterSelect.GetNode<Button>("%CharacterBButton").ButtonPressed = true;
        characterSelect.GetNode<Button>("%ConfirmButton").EmitSignal(BaseButton.SignalName.Pressed);

        var professorIntro = await WaitForScene<ProfessorIntroScreen>();
        GD.Print("SMOKE: professor intro loaded");
        GD.Print($"SMOKE: PendingIdentity = {GameState.Instance.PendingIdentity}");
        if (GameState.Instance.PendingIdentity is not { } identity
            || identity.Character != ProcPoke.Overworld.CharacterChoice.CharacterB || identity.Name != "Smoke")
            throw new InvalidOperationException("CharacterSelectScreen did not hand off the chosen identity correctly.");

        professorIntro.GetNode<Button>("%ContinueButton").EmitSignal(BaseButton.SignalName.Pressed);

        var starterSelect = await WaitForScene<StarterSelectScreen>();
        GD.Print("SMOKE: starter select loaded");
        const int chosenCorner = 1;
        var expectedSpeciesId = region.Starters.Corners[chosenCorner].BaseSpeciesId;
        starterSelect.GetNode<HBoxContainer>("%Corners").GetChild<Button>(chosenCorner)
            .EmitSignal(BaseButton.SignalName.Pressed);

        var overworld = await WaitForScene<OverworldRootController>();
        GD.Print("SMOKE: overworld loaded after starter pick");

        var party = GameState.Instance.Session?.Party
            ?? throw new InvalidOperationException("No session/party after the new-game flow completed.");
        if (party.Members.Count != 1)
            throw new InvalidOperationException($"expected exactly 1 party member, found {party.Members.Count}.");
        if (party.Members[0].SpeciesId != expectedSpeciesId)
            throw new InvalidOperationException(
                $"expected species {expectedSpeciesId}, party has {party.Members[0].SpeciesId}.");
        if (party.Members[0].Level != 5)
            throw new InvalidOperationException($"expected level 5, party member is level {party.Members[0].Level}.");
        GD.Print($"SMOKE: party seeded with species {expectedSpeciesId} at level 5, as chosen");

        _ = overworld;
    }

    // StarterSelectScreen holds for 1.5 real seconds before switching scenes; headless frames can tick
    // much faster than 60/sec with no rendering/vsync overhead, so wait on wall-clock time, not frames.
    private async Task<T> WaitForScene<T>() where T : Node
    {
        for (var i = 0; i < 100; i++)
        {
            if (CurrentScene is T match) return match;
            await ToSignal(CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
        }
        throw new InvalidOperationException(
            $"{typeof(T).Name} did not become the current scene within 5 seconds (current: {CurrentScene?.GetType().Name}).");
    }
}
