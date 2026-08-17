using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Generation.Encounters;
using ProcPoke.Generation.Roster;

namespace ProcPoke.Overworld;

/// <summary>
/// Immutable hand-off from the overworld to battle. ReturnState is the post-step state, so returning
/// from battle resumes on the tile where the encounter was triggered with its facing and movement mode.
/// <see cref="Legendary"/> is set only for a generated static encounter (never a rolled tall-grass/surf
/// encounter); the battle screen marks it resolved on a win or capture, never on a flee or loss, so an
/// unresolved legendary can always be challenged again (§5.5: legendaries never gate progress).
/// </summary>
public sealed record WildBattleTransitionRequest(
    WildEncounter Encounter,
    PlayerState ReturnState,
    WildBattleDefinition Battle,
    LegendaryPlacement? Legendary = null);

public static class WildBattleTransitionResolver
{
    public static WildBattleTransitionRequest Create(
        WildEncounter encounter,
        MovementResolution movement)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(movement);
        if (movement.Result != MovementResultKind.Moved || movement.Target is null)
            throw new ArgumentException("A battle transition requires a successful movement resolution.", nameof(movement));

        return new WildBattleTransitionRequest(encounter, movement.State, DefinitionFor(encounter));
    }

    /// <summary>A legendary static encounter: no movement to resolve into (there is no tile marker to walk
    /// up to — the trigger is reaching the placement's area), so the return state is handed in directly.</summary>
    public static WildBattleTransitionRequest CreateStatic(
        WildEncounter encounter, PlayerState returnState, LegendaryPlacement legendary)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(returnState);
        ArgumentNullException.ThrowIfNull(legendary);
        return new WildBattleTransitionRequest(encounter, returnState, DefinitionFor(encounter), legendary);
    }

    private static WildBattleDefinition DefinitionFor(WildEncounter encounter) => new(
        encounter.AreaId, encounter.SpeciesId, encounter.Level, encounter.HiddenAbility);

    public static WildBattleSession Start(
        GameData data,
        BattlePokemon player,
        WildBattleTransitionRequest request,
        ulong seed,
        BattlePokemonBuildOptions? wildOptions = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WildBattleSession.Start(data, player, request.Battle, seed, wildOptions);
    }
}
