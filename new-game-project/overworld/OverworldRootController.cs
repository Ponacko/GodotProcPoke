using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProcPoke;
using ProcPoke.Battle;
using ProcPoke.Data;
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
    private IReadOnlyList<InteractionRegistration> _interiorInteractions = [];
    private OverworldSession _session = null!;
    private bool _suppressNextEncounterRoll;

    private static GameData Data => GameState.Instance.Data
        ?? throw new InvalidOperationException("OverworldRootController entered with no GameData.");

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

        _movement.IsInteractable = position => InteractionAt(position) is not null;
        _movement.EncounterRoller = TryRollEncounter;
        _movement.BattleTransitionRequested += OnBattleTransitionRequested;
        _movement.Resolved += OnResolved;

        var start = GameState.Instance.ResumePlayerState
            ?? (_session.ActiveInterior is { } active
                ? new PlayerState(active.Template.Arrival)
                : new PlayerState(FirstWalkable(_session.Region.Carved[_session.CurrentAreaId].Grid)));
        GameState.Instance.ResumePlayerState = null;
        if (_session.ActiveInterior is { } interior)
            LoadInterior(interior, start);
        else
            LoadArea(_session.CurrentAreaId, start);
    }

    private void LoadArea(int areaId, PlayerState start)
    {
        _session.VisitArea(areaId);
        _interiorInteractions = [];
        if (_session.TryTriggerRivalBattle(areaId, start) is { } rivalRequest)
        {
            GameState.Instance.PendingRivalBattleRequest = rivalRequest;
            GetTree().ChangeSceneToFile("res://battle/TrainerBattleScreen.tscn");
            return;
        }

        if (_session.TryTriggerLegendaryEncounter(areaId, start) is { } legendaryRequest)
        {
            GameState.Instance.PendingBattleRequest = legendaryRequest;
            GetTree().ChangeSceneToFile("res://battle/BattleScreen.tscn");
            return;
        }

        foreach (var fossil in _session.TryCollectFossils(areaId))
            _session.Party.Add(BattleProgressionFactory.AtLevel(Data, fossil.SpeciesId, fossil.Level));

        var carved = _session.Region.Carved[areaId];
        _realizer.Realize(carved, _session.Region.Biomes.Of(areaId));
        var capabilities = _session.Interactions.ForMovement(
            areaId, new MovementCapabilities(), _registry, _session.Region.Gating, _session.Badges);
        _movement.Configure(carved.Grid, start, capabilities);
        _camera.Configure(carved.Grid);
        _camera.Follow(start.Position);
    }

    private void LoadInterior(ActiveInterior interior, PlayerState start)
    {
        _interiorInteractions = interior.Template.Kind switch
        {
            InteriorKind.Gym => GymInteriorBattles.Build(_session.Region, interior.OverworldAreaId, interior.Template),
            InteriorKind.Mart => MartInteractions.Build(interior.OverworldAreaId, interior.Template),
            InteriorKind.Center => CenterInteractions.Build(interior.OverworldAreaId, interior.Template),
            _ => [],
        };
        _realizer.Realize(interior.Template.Grid, interior.Template.Biome);
        _movement.Configure(interior.Template.Grid, start, new MovementCapabilities
        {
            DebugUnlock = _session.DebugUnlock,
        });
        _camera.Configure(interior.Template.Grid);
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
            CurrentInteractions(),
            CurrentGrid(),
            target,
            _session.DefeatedTrainerIds);
        if (sightlineTrainer is not null && StartTrainerBattle(sightlineTrainer)) return;

        if (_session.ActiveInterior is { } interior)
        {
            if (target == interior.Template.Door
                && _movement.State.Facing == InteriorTransitionResolver.FacingFor(interior.Entrance.StreetEdge))
            {
                var returnState = new PlayerState(
                    new GridPosition(interior.Entrance.X, interior.Entrance.Y),
                    InteriorTransitionResolver.FacingFor(interior.Entrance.StreetEdge));
                var overworldAreaId = interior.OverworldAreaId;
                _suppressNextEncounterRoll = true;
                _session.ExitInterior();
                LoadArea(overworldAreaId, returnState);
            }
            return;
        }

        var region = _session.Region;
        var areaId = _session.CurrentAreaId;
        var grid = region.Carved[areaId].Grid;
        var building = region.Carved[areaId].BuildingEntrances.FirstOrDefault(entrance =>
            entrance.X == target.X && entrance.Y == target.Y);
        if (building is not null)
        {
            var enter = InteriorTransitionResolver.Enter(
                region.Carved[areaId], building, target, _movement.State.Facing);
            if (enter is not null)
            {
                _suppressNextEncounterRoll = true;
                _session.EnterInterior(enter);
                LoadInterior(_session.ActiveInterior!,
                    new PlayerState(enter.InteriorPosition, enter.ArrivalFacing));
            }
            return;
        }

        var crossing = region.Openings.EdgesOf(areaId).FirstOrDefault(opening =>
            opening.TileOn(grid.Width, grid.Height) == (target.X, target.Y)
            && TransitionResolver.OutwardFacing(opening.Edge) == _movement.State.Facing);
        if (crossing is null) return;

        _suppressNextEncounterRoll = true;
        await _transition.TransitionAsync(region, areaId, target, _movement.State.Facing,
            arrival => LoadArea(arrival.AreaId, new PlayerState(arrival.Position, arrival.Facing)));
    }

    private void HandleInteraction(GridPosition position)
    {
        var interaction = InteractionAt(position);
        if (interaction is null) return;

        var outcome = _session.Interactions.Interact(interaction, _session.Badges);
        _message.Text = outcome.Message;

        if (outcome.Kind == InteractionOutcomeKind.TrainerPrompt)
        {
            StartTrainerBattle(interaction);
            return;
        }

        if (interaction.Kind == InteractionKind.Shop)
            _message.Text += "\n" + _session.BuyFromMart(Data, interaction.AreaId).Message;

        if (interaction.Kind == InteractionKind.Heal)
        {
            _session.HealParty(Data);
            _message.Text += "\nYour Pokémon are fighting fit!";
        }

        if (outcome.Kind == InteractionOutcomeKind.GateUnlocked)
            _movement.UpdateCapabilities(_session.Interactions.ForMovement(
                _session.CurrentAreaId, new MovementCapabilities(), _registry, _session.Region.Gating, _session.Badges));
    }

    private WildEncounter? TryRollEncounter(MovementResolution resolution)
    {
        if (_suppressNextEncounterRoll)
        {
            _suppressNextEncounterRoll = false;
            return null;
        }
        // Resolved handlers run before the controller rolls a wild encounter. A trainer who spotted the
        // same step takes precedence, exactly as a forced trainer engagement should.
        if (GameState.Instance.PendingTrainerBattleRequest is not null) return null;
        if (_session.ActiveInterior is not null) return null;
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

    private IReadOnlyList<InteractionRegistration> CurrentInteractions()
        => _session.ActiveInterior is null
            ? _registry.Of(_session.CurrentAreaId)
            : _interiorInteractions;

    private InteractionRegistration? InteractionAt(GridPosition position)
        => _session.ActiveInterior is null
            ? _registry.At(_session.CurrentAreaId, position)
            : _interiorInteractions.FirstOrDefault(interaction => interaction.Position == position);

    private TileGrid CurrentGrid()
        => _session.ActiveInterior?.Template.Grid
            ?? _session.Region.Carved[_session.CurrentAreaId].Grid;

    private static GridPosition FirstWalkable(TileGrid grid)
        => Enumerable.Range(0, grid.Height)
            .SelectMany(y => Enumerable.Range(0, grid.Width).Select(x => new GridPosition(x, y)))
            .First(position => grid[position.X, position.Y].IsWalkable());
}
