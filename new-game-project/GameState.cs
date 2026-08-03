using Godot;
using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Overworld;

namespace ProcPoke;

/// <summary>
/// Autoload singleton carrying state across scene swaps (RegionViewer -&gt; overworld -&gt; battle -&gt;
/// overworld). Godot destroys a scene's whole node tree on <c>ChangeSceneToFile</c>, so anything that must
/// survive a swap — the generated region's live session, baked data, an in-flight battle hand-off, the
/// player's in-memory progression — lives here instead of on a per-scene controller.
/// </summary>
public partial class GameState : Node
{
    public static GameState Instance { get; private set; } = null!;

    public GameData? Data { get; set; }
    public OverworldSession? Session { get; set; }
    public BattleProgressionState? PlayerProgression { get; set; }
    public WildBattleTransitionRequest? PendingBattleRequest { get; set; }

    /// <summary>Where to resume in the overworld after a battle ends — null after a whiteout (the session's
    /// new <c>CurrentAreaId</c> is already the Center; the overworld root falls back to its first walkable
    /// tile).</summary>
    public PlayerState? ResumePlayerState { get; set; }

    public override void _Ready() => Instance = this;
}
