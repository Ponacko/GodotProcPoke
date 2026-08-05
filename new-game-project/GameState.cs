using Godot;
using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Overworld;

namespace ProcPoke;

/// <summary>
/// Autoload singleton carrying state across scene swaps (RegionViewer -&gt; new-game flow -&gt; overworld
/// -&gt; battle -&gt; overworld). Godot destroys a scene's whole node tree on <c>ChangeSceneToFile</c>, so
/// anything that must survive a swap — the generated region's live session, baked data, an in-flight
/// battle hand-off — lives here instead of on a per-scene controller. The player's actual progression
/// lives on <see cref="OverworldSession.Party"/> once a session exists; this only carries the region
/// through the new-game flow's screens, before a session (which needs a chosen identity) can be built.
/// </summary>
public partial class GameState : Node
{
    public static GameState Instance { get; private set; } = null!;

    public GameData? Data { get; set; }
    public OverworldSession? Session { get; set; }
    public WildBattleTransitionRequest? PendingBattleRequest { get; set; }
    public RivalBattleTransitionRequest? PendingRivalBattleRequest { get; set; }

    /// <summary>Set by RegionViewer's "New Game" button; consumed by StarterSelectScreen once identity and
    /// starter are both chosen and a real <see cref="OverworldSession"/> can be constructed.</summary>
    public GeneratedRegion? PendingNewGameRegion { get; set; }

    /// <summary>The identity chosen on CharacterSelectScreen, carried through ProfessorIntroScreen to
    /// StarterSelectScreen — cleared once the real <see cref="OverworldSession"/> is built.</summary>
    public PlayerIdentity? PendingIdentity { get; set; }

    /// <summary>Where to resume in the overworld after a battle ends — null after a whiteout (the session's
    /// new <c>CurrentAreaId</c> is already the Center; the overworld root falls back to its first walkable
    /// tile).</summary>
    public PlayerState? ResumePlayerState { get; set; }

    public override void _Ready() => Instance = this;
}
