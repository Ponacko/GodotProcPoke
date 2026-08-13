using System;
using System.Linq;
using Godot;
using ProcPoke;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Encounters;
using ProcPoke.Overworld;

namespace ProcPoke.OverworldView;

/// <summary>
/// The first real playable overworld root: loads the session handed off by <c>GameState</c> (from
/// RegionViewer's Play button, or resumed after a battle), wires <see cref="GridMovementController"/>,
/// <see cref="TileRealizer"/>, <see cref="GridCamera"/>, and <see cref="TransitionController"/> together,
/// rolls wild encounters on tall grass / surfed water, and hands battle transitions off to
/// <c>battle/BattleScreen.tscn</c> (wild) or <c>battle/TrainerBattleScreen.tscn</c> (generated trainers
/// and scripted rival beats) via <c>GameState</c>.
/// </summary>
public partial class OverworldRootController : Node2D
{
    /// <summary>Gen 5 tall-grass encounter rate: 21/256 per step.</summary>
    private const int EncounterChanceOutOf256 = 21;

    private readonly Random _random = new();

    private TileRealizer _realizer = null!;
    private GridMovementController _movement = null!;
    private GridCamera _camera = null!;
    private TransitionController _transition = null!;
    private Label _message = null!;
    private InteractionRegistry _registry = null!;
    private OverworldSession _session = null!;

    public override void _Ready()
    {
        InputBindings.EnsureDefaults();
        _session = GameState.Instance.Session
            ?? throw new InvalidOperationException(
                "No overworld session — launch this scene from RegionViewer's Play button.");
        _registry = InteractionRegistry.Build(_session.Region);

        _realizer = GetNode<TileRealizer>("TileRealizer");
        _movement = GetNode<GridMovementController>("Player");
        _camera = GetNode<GridCamera>("Player/Camera");
        _transition = GetNode<TransitionController>("TransitionController");
        _message = GetNode<Label>("Hud/MessageLabel");

        _movement.IsInteractable = position => _registry.At(_session.CurrentAreaId, position) is not null;
        _movement.EncounterRoller = TryRollEncounter;
        _movement.BattleTransitionRequested += OnBattleTransitionRequested;
        _movement.Resolved += OnResolved;

        var start = GameState.Instance.ResumePlayerState
            ?? new PlayerState(FirstWalkable(_session.Region.Carved[_session.CurrentAreaId].Grid));
        GameState.Instance.ResumePlayerState = null;
        LoadArea(_session.CurrentAreaId, start);
    }

    private void LoadArea(int areaId, PlayerState start)
    {
        _session.VisitArea(areaId);
        if (_session.TryTriggerRivalBattle(areaId, start) is { } rivalRequest)
        {
            GameState.Instance.PendingRivalBattleRequest = rivalRequest;
            GetTree().ChangeSceneToFile("res://battle/TrainerBattleScreen.tscn");
            return;
        }

        var carved = _session.Region.Carved[areaId];
        _realizer.Realize(carved, _session.Region.Biomes.Of(areaId));
        var capabilities = _session.Interactions.ForMovement(areaId, new MovementCapabilities(), _registry);
        _movement.Configure(carved.Grid, start, capabilities);
        _camera.Configure(carved.Grid);
        _camera.Follow(start.Position);
    }

    private async void OnResolved(MovementResolution resolution)
    {
        _camera.Follow(_movement.State.Position);

        if (resolution.Result == MovementResultKind.Interacted && resolution.Target is { } interactedAt)
        {
            HandleInteraction(interactedAt);
            return;
        }

        if (resolution.Result != MovementResultKind.Moved || resolution.Target is not { } target) return;

        var sightlineTrainer = TrainerSightlines.FindEngaging(
            _registry.Of(_session.CurrentAreaId),
            _session.Region.Carved[_session.CurrentAreaId].Grid,
            target,
            _session.DefeatedTrainerIds);
        if (sightlineTrainer is not null && StartTrainerBattle(sightlineTrainer)) return;

        var region = _session.Region;
        var areaId = _session.CurrentAreaId;
        var grid = region.Carved[areaId].Grid;
        var crossing = region.Openings.EdgesOf(areaId).FirstOrDefault(opening =>
            opening.TileOn(grid.Width, grid.Height) == (target.X, target.Y)
            && TransitionResolver.OutwardFacing(opening.Edge) == _movement.State.Facing);
        if (crossing is null) return;

        await _transition.TransitionAsync(region, areaId, target, _movement.State.Facing,
            arrival => LoadArea(arrival.AreaId, new PlayerState(arrival.Position, arrival.Facing)));
    }

    private void HandleInteraction(GridPosition position)
    {
        var interaction = _registry.At(_session.CurrentAreaId, position);
        if (interaction is null) return;

        var outcome = _session.Interactions.Interact(interaction);
        _message.Text = outcome.Message;

        if (outcome.Kind == InteractionOutcomeKind.TrainerPrompt)
        {
            StartTrainerBattle(interaction);
            return;
        }

        if (outcome.Kind == InteractionOutcomeKind.GateUnlocked)
            _movement.UpdateCapabilities(
                _session.Interactions.ForMovement(_session.CurrentAreaId, new MovementCapabilities(), _registry));
    }

    private WildEncounter? TryRollEncounter(MovementResolution resolution)
    {
        // Resolved handlers run before the controller rolls a wild encounter. A trainer who spotted the
        // same step takes precedence, exactly as a forced trainer engagement should.
        if (GameState.Instance.PendingTrainerBattleRequest is not null) return null;
        if (resolution.Target is not { } target) return null;
        var grid = _session.Region.Carved[_session.CurrentAreaId].Grid;
        if (!grid.InBounds(target.X, target.Y)) return null;

        var method = grid[target.X, target.Y] switch
        {
            LogicalTile.TallGrass => EncounterMethod.Land,
            LogicalTile.Water when _movement.State.Surfing => EncounterMethod.Surf,
            _ => (EncounterMethod?)null,
        };
        if (method is null) return null;
        if (_random.Next(256) >= EncounterChanceOutOf256) return null;

        var plan = _session.Region.Encounters;
        var areaEncounters = plan.Of(_session.CurrentAreaId);
        if (areaEncounters is null || !areaEncounters.Tables.Any(table => table.Method == method.Value))
            return null;

        return WildEncounterSelector.Select(plan, _session.CurrentAreaId, method.Value, useOverlay: false, _random.Next);
    }

    private void OnBattleTransitionRequested(WildBattleTransitionRequest request)
    {
        GameState.Instance.PendingBattleRequest = request;
        GetTree().ChangeSceneToFile("res://battle/BattleScreen.tscn");
    }

    private bool StartTrainerBattle(InteractionRegistration interaction)
    {
        var request = _session.TryStartTrainerBattle(interaction, _movement.State);
        if (request is null)
        {
            _message.Text = $"You already defeated {interaction.TrainerClass}.";
            return false;
        }

        GameState.Instance.PendingTrainerBattleRequest = request;
        GetTree().ChangeSceneToFile("res://battle/TrainerBattleScreen.tscn");
        return true;
    }

    private static GridPosition FirstWalkable(TileGrid grid)
        => Enumerable.Range(0, grid.Height)
            .SelectMany(y => Enumerable.Range(0, grid.Width).Select(x => new GridPosition(x, y)))
            .First(position => grid[position.X, position.Y].IsWalkable());
}
