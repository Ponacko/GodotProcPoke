using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProcPoke;
using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Overworld;

namespace ProcPoke.BattleView;

/// <summary>
/// Multi-mon trainer battle presentation for generated map trainers and scripted rivals, replaying
/// <see cref="TrainerBattleSession"/>'s event stream through <see cref="BattleScreenBase"/> exactly like
/// <see cref="BattleScreen"/> replays a wild one. No Ball or Run buttons — trainer battles never allow
/// catching or fleeing.
/// </summary>
public partial class TrainerBattleScreen : BattleScreenBase
{
    private Vector2 _opponentSpriteHome;

    private GameData _data = null!;
    private RivalBattleTransitionRequest? _rivalRequest;
    private GeneratedTrainerBattleTransitionRequest? _generatedRequest;
    private PlayerState _returnState = null!;
    private IReadOnlyList<TrainerRosterMember> _roster = null!;
    private string _trainerName = null!;
    private BattleProgressionState _progression = null!;
    private TrainerBattleSession _session = null!;
    private bool _busy;

    public override void _Ready()
    {
        BindCommonNodes();
        _opponentSpriteHome = OpponentSprite.Position;

        _data = GameState.Instance.Data
            ?? throw new InvalidOperationException("TrainerBattleScreen entered with no GameData.");
        _rivalRequest = GameState.Instance.PendingRivalBattleRequest;
        _generatedRequest = GameState.Instance.PendingTrainerBattleRequest;
        if ((_rivalRequest is null) == (_generatedRequest is null))
            throw new InvalidOperationException(
                "TrainerBattleScreen requires exactly one pending rival or generated trainer request.");
        GameState.Instance.PendingRivalBattleRequest = null;
        GameState.Instance.PendingTrainerBattleRequest = null;

        if (_rivalRequest is { } rival)
        {
            _returnState = rival.ReturnState;
            _roster = rival.Roster;
            _trainerName = "Rival";
        }
        else
        {
            _returnState = _generatedRequest!.ReturnState;
            _roster = _generatedRequest.Roster;
            _trainerName = _generatedRequest.TrainerName;
        }

        var session = GameState.Instance.Session
            ?? throw new InvalidOperationException("TrainerBattleScreen entered with no active session.");
        if (session.Party.Members.Count == 0)
            throw new InvalidOperationException("TrainerBattleScreen entered with an empty party — no Pokémon to send out.");
        _progression = session.Party.Members[0];

        var player = BattlePokemonFactory.Create(_data, _progression.SpeciesId, _progression.Level,
            new BattlePokemonBuildOptions
            {
                // The battle HUD prints Id beside the level, so a party member is labelled by the name the
                // player knows it as — its nickname from the Name Rater, or its species.
                Id = Nicknames.DisplayName(_data, _progression),
                MoveIds = _progression.MoveIds,
                EffortValues = _progression.EffortValues,
                StartingHp = _progression.CurrentHp,
            });
        // A gym leader, an Elite Four member, the Champion and a hideout boss all fight at the boss tier
        // (CONTEXT.md's AI Tier): no imperfection, and a potion when their Pokémon drops low. A rival beat
        // is a scripted trainer battle, not a boss fight.
        _session = TrainerBattleSession.Start(
            _data, player, _roster, RandomSeed(), $"{_trainerName}'s",
            _generatedRequest?.AiTier ?? BattleAiTier.Trainer);
        State = _session.State;
        session.MarkSpeciesSeen(State.Opponent.SpeciesId);

        var biome = session.Region.Biomes.Of(session.CurrentAreaId);
        Backdrop.Color = BattleBackdropPalette.Of(biome);

        LoadOpponentSprite();
        var playerSprite = BattleSpriteLoader.Load(State.Player.SpeciesId, preferBack: true);
        PlayerSprite.SpriteFrames = playerSprite.Frames;
        PlayerSprite.FlipH = !playerSprite.IsBackArt; // mirror the front sprite only when no real back art exists
        PlayerSprite.Play("idle");
        PlayCry(State.Opponent.SpeciesId);

        FightButton.Pressed += () => { Actions.Visible = false; MoveButtons.Visible = true; };

        BuildMoveButtons(moveId => ResolveAction(new UseMoveAction(moveId)));
        BuildBagButton(ResolveAction);
        RefreshBagButton();
        RefreshLabels();
        var greeting = _rivalRequest is not null
            ? RivalDialogue.Greeting(session.Region.Identity.Rival)
            : $"{_trainerName} wants to battle!";
        Log.Text = $"{greeting}\n"
            + $"{_trainerName} sent out {_data.Species[State.Opponent.SpeciesId].Name}!\n";
    }

    private async void ResolveAction(BattleAction action)
    {
        if (_busy || _session.Outcome != TrainerBattleOutcome.Ongoing) return;
        _busy = true;
        SetActionsEnabled(false);
        MoveButtons.Visible = false;

        var previousOpponentSpecies = State.Opponent.SpeciesId;
        var result = _session.TakeTurn(action);
        State = result.State;
        foreach (var battleEvent in result.Events)
            await Animate(battleEvent);

        if (State.Opponent.SpeciesId != previousOpponentSpecies)
        {
            LoadOpponentSprite();
            PlayCry(State.Opponent.SpeciesId);
            // Each Pokémon a trainer sends out is one the player has now seen.
            GameState.Instance.Session!.MarkSpeciesSeen(State.Opponent.SpeciesId);
        }
        RefreshLabels();
        _busy = false;

        switch (_session.Outcome)
        {
            case TrainerBattleOutcome.PlayerWon:
                ApplyVictory();
                await ReturnToOverworld(_returnState);
                break;
            case TrainerBattleOutcome.PlayerLost:
                GameState.Instance.Session!.ApplyWhiteout(_data);
                await ReturnToOverworld(resumeAt: null);
                break;
            default:
                Actions.Visible = true;
                SetActionsEnabled(true);
                break;
        }
    }

    private void ApplyVictory()
    {
        var rewards = _session.ApplyVictoryRewards(_progression);
        foreach (var reward in rewards)
        {
            var speciesName = _data.Species[_progression.SpeciesId].Name;
            _progression = reward.Progression;
            Log.AppendText($"\n{speciesName} gained {reward.Experience.ExperienceGained} EXP!\n");
            if (reward.Experience.NewLevel > reward.Experience.PreviousLevel)
                Log.AppendText($"{speciesName} grew to Lv{reward.Experience.NewLevel}!\n");
            foreach (var moveId in reward.Moves.LearnedMoveIds)
                Log.AppendText($"Learned {_data.Moves[moveId].Name}!\n");
            if (reward.Evolution is { } evolution)
                Log.AppendText(
                    $"{_data.Species[evolution.FromSpeciesId].Name} evolved into {_data.Species[evolution.ToSpeciesId].Name}!\n");
        }

        var session = GameState.Instance.Session!;
        session.Party.Replace(0, _progression);
        if (_rivalRequest is { } rival)
        {
            session.MarkRivalBeatDefeated(rival.Beat);
            Log.AppendText($"\n{RivalDialogue.Victory(session.Region.Identity.Rival)}\n");
        }
        else
        {
            session.CompleteTrainerBattle(_generatedRequest!);
            Log.AppendText($"\nDefeated {_trainerName}!\n"
                + $"Received ₽{_generatedRequest!.PrizeMoney}.\n");
            if (_generatedRequest.GymReward is { } gym)
                Log.AppendText($"Received Badge {gym.BadgeNumber}!\n");
            if (_generatedRequest.LeagueReward is { } league)
            {
                if (league.Stage < session.Region.Bosses.EliteFour.Count)
                    Log.AppendText($"Elite Four {league.Stage + 1} defeated!\n");
                else
                    Log.AppendText("Champion defeated! The credits roll.\n");
            }
        }
    }

    private void SetActionsEnabled(bool enabled)
    {
        FightButton.Disabled = !enabled;
        foreach (var child in MoveButtons.GetChildren())
            if (child is Button button) button.Disabled = !enabled;
        if (BagButton is not null) BagButton.Disabled = !enabled;
        if (enabled) RefreshBagButton();
    }

    private void LoadOpponentSprite()
    {
        var sprite = BattleSpriteLoader.Load(State.Opponent.SpeciesId, preferBack: false);
        OpponentSprite.SpriteFrames = sprite.Frames;
        OpponentSprite.Modulate = Colors.White;
        OpponentSprite.Position = _opponentSpriteHome;
        OpponentSprite.Play("idle");
    }

    private void RefreshLabels()
    {
        OpponentLabel.Text = $"{State.Opponent.Id}  Lv{State.Opponent.Level}  ({_session.OpponentPosition}/{_session.OpponentRosterSize})";
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
}
