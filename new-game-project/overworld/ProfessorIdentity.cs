using System;
using System.Linq;

namespace ProcPoke.OverworldView;

/// <summary>
/// The generated-per-seed framing NPC's name and gender (GDD §7.1a) — rolled fresh per playthrough, not
/// part of the deterministic region (the professor is presentation flavor, not a generation-pass fact).
/// Follows <c>IdentityPass.CanonTeamNames</c>'s precedent: a small hardcoded curated pool filtered against
/// a small hardcoded canon-name blocklist. No "already used" collision state to redraw against here (unlike
/// villain team names across multiple teams), so a one-shot filter is enough — there's nothing to redraw.
/// </summary>
public static class ProfessorIdentity
{
    // Curated pool of professor-appropriate names distinct from canon Pokémon professors/characters
    // (GDD §7.1a: "curated pool — Aspen, Laurel, Hawthorn, Linden…").
    private static readonly string[] NamePool =
        ["Aspen", "Laurel", "Hawthorn", "Linden", "Cypress", "Larch", "Basswood", "Cedar", "Poplar", "Sequoia"];

    // Real Pokémon professors/characters this pool must never collide with.
    private static readonly string[] CanonNames =
        ["Oak", "Elm", "Birch", "Rowan", "Juniper", "Sycamore", "Kukui", "Magnolia", "Sada", "Turo", "Cerise", "Willow"];

    public static (string Name, string Pronoun) Generate(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var name = NamePool
            .Where(candidate => !CanonNames.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            .OrderBy(_ => random.Next())
            .First();
        var pronoun = random.Next(2) == 0 ? "he" : "she";
        return (name, pronoun);
    }
}
