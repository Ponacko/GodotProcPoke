using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Encounters;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class WildBattleTransitionTests
{
    private static readonly GameData Data = TestData.Data;

    [Fact]
    public void TransitionPreservesReturnStateAndMapsTheEncounterToBattleDefinition()
    {
        var encounter = new WildEncounter(7, EncounterMethod.Land, 25, 14, true, true);
        var state = new PlayerState(
            new GridPosition(3, 4), Facing.West, MovementMode.Run, Surfing: false);
        var movement = new MovementResolution(state, MovementResultKind.Moved, state.Position);

        var request = WildBattleTransitionResolver.Create(encounter, movement);

        Assert.Equal(state, request.ReturnState);
        Assert.Equal(encounter, request.Encounter);
        Assert.Equal(new WildBattleDefinition(7, 25, 14, true), request.Battle);
    }

    [Fact]
    public void TransitionStartsTheBattleSessionWithTheSelectedWildPokemon()
    {
        var encounter = new WildEncounter(7, EncounterMethod.Surf, 129, 12, false, false);
        var state = new PlayerState(new GridPosition(1, 1));
        var request = WildBattleTransitionResolver.Create(
            encounter,
            new MovementResolution(state, MovementResultKind.Moved, state.Position));
        var player = BattlePokemonFactory.Create(Data, 1, 20,
            new BattlePokemonBuildOptions { MoveIds = [33] });

        var session = WildBattleTransitionResolver.Start(Data, player, request, seed: 99);

        Assert.Equal(129, session.State.Opponent.SpeciesId);
        Assert.Equal(12, session.State.Opponent.Level);
        Assert.Equal(WildBattleOutcome.Ongoing, session.Outcome);
    }

    [Fact]
    public void FailedMovementCannotCreateABattleTransition()
    {
        var encounter = new WildEncounter(7, EncounterMethod.Land, 25, 14, false, false);
        var movement = new MovementResolution(
            new PlayerState(new GridPosition(1, 1)), MovementResultKind.Blocked);

        Assert.Throws<ArgumentException>(
            () => WildBattleTransitionResolver.Create(encounter, movement));
    }
}
