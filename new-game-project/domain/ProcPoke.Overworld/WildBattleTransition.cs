using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Generation.Encounters;

namespace ProcPoke.Overworld;

/// <summary>
/// Immutable hand-off from the overworld to battle. ReturnState is the post-step state, so returning
/// from battle resumes on the tile where the encounter was triggered with its facing and movement mode.
/// </summary>
public sealed record WildBattleTransitionRequest(
    WildEncounter Encounter,
    PlayerState ReturnState,
    WildBattleDefinition Battle);

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

        return new WildBattleTransitionRequest(
            encounter,
            movement.State,
            new WildBattleDefinition(
                encounter.AreaId,
                encounter.SpeciesId,
                encounter.Level,
                encounter.HiddenAbility));
    }

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
