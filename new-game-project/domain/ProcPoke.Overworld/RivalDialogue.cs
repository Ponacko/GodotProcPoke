using ProcPoke.Generation.Identity;

namespace ProcPoke.Overworld;

/// <summary>
/// Per-archetype dialogue tone for scripted rival battles (GDD §7.2: each seed's rival archetype is "kept
/// consistent across every encounter (dialogue tone, team-building flavor)"). Team-building flavor is
/// already expressed by <c>RivalPass</c>'s own species/BST choices; this covers the textual tone only, and
/// deliberately stays flat across beats — the Underdog archetype's "visibly grows in confidence over the
/// game" beat-by-beat arc is a follow-up, not implemented here.
/// </summary>
public static class RivalDialogue
{
    public static string Greeting(RivalArchetype archetype) => archetype switch
    {
        RivalArchetype.Cocky => "Rival: Ha! Let's see what you've got.",
        RivalArchetype.Friendly => "Rival: Ready? Let's have a great battle together!",
        RivalArchetype.Brooding => "Rival: ...Show me.",
        RivalArchetype.Underdog => "Rival: I've been training. This time's different!",
        _ => throw new ArgumentOutOfRangeException(nameof(archetype)),
    };

    public static string Victory(RivalArchetype archetype) => archetype switch
    {
        RivalArchetype.Cocky => "Rival: Tch. Lucky shot. Next time's mine.",
        RivalArchetype.Friendly => "Rival: You're amazing! That was a blast!",
        RivalArchetype.Brooding => "Rival: ...Noted.",
        RivalArchetype.Underdog => "Rival: Argh! But I'm not giving up!",
        _ => throw new ArgumentOutOfRangeException(nameof(archetype)),
    };
}
