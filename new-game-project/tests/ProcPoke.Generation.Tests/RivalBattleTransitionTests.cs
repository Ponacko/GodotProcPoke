using System;
using System.Linq;
using ProcPoke.Generation;
using ProcPoke.Generation.Rivals;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class RivalBattleTransitionTests
{
    private static GeneratedRegion Region(ulong seed = 7, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    private static OverworldSession Session(GeneratedRegion region)
        => new(region, new PlayerIdentity(CharacterChoice.CharacterA, "Ash"));

    [Fact]
    public void NoTriggerFiresUntilAStarterCornerIsSet()
    {
        var region = Region();
        var session = Session(region);
        var areaId = RivalBeatTriggers.AreaFor(RivalBeat.EarlyRoute, region.Graph, region.Identity);

        var result = session.TryTriggerRivalBattle(areaId, new PlayerState(new GridPosition(0, 0)));

        Assert.Null(result);
    }

    [Fact]
    public void SetStarterCornerRejectsAnOutOfRangeIndex()
    {
        var session = Session(Region());

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetStarterCorner(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetStarterCorner(3));
    }

    [Fact]
    public void EnteringANonTriggerAreaFiresNothing()
    {
        var region = Region();
        var session = Session(region);
        session.SetStarterCorner(0);

        var result = session.TryTriggerRivalBattle(region.Graph.StartAreaId, new PlayerState(new GridPosition(0, 0)));

        Assert.Null(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EnteringATriggerAreaBuildsTheMatchingRosterForTheChosenCorner(int cornerIndex)
    {
        var region = Region();
        var session = Session(region);
        session.SetStarterCorner(cornerIndex);
        var returnState = new PlayerState(new GridPosition(3, 4));
        var areaId = RivalBeatTriggers.AreaFor(RivalBeat.EarlyRoute, region.Graph, region.Identity);

        var result = session.TryTriggerRivalBattle(areaId, returnState);

        Assert.NotNull(result);
        Assert.Equal(RivalBeat.EarlyRoute, result!.Beat);
        Assert.Equal(cornerIndex, result.PlayerCornerIndex);
        Assert.Equal(returnState, result.ReturnState);
        var expectedTeam = region.Rivals.ForPlayerCorner(cornerIndex).TeamAt(RivalBeat.EarlyRoute);
        Assert.Equal(
            expectedTeam.Members.Select(member => (member.SpeciesId, member.Level)),
            result.Roster.Select(member => (member.SpeciesId, member.Level)));
    }

    [Fact]
    public void ADefeatedBeatDoesNotFireAgainAtTheSameArea()
    {
        var region = Region();
        var session = Session(region);
        session.SetStarterCorner(0);
        var areaId = RivalBeatTriggers.AreaFor(RivalBeat.EarlyRoute, region.Graph, region.Identity);
        var returnState = new PlayerState(new GridPosition(0, 0));

        Assert.NotNull(session.TryTriggerRivalBattle(areaId, returnState));
        session.MarkRivalBeatDefeated(RivalBeat.EarlyRoute);
        var result = session.TryTriggerRivalBattle(areaId, returnState);

        Assert.True(result is null || result.Beat != RivalBeat.EarlyRoute);
        Assert.True(session.HasDefeatedRivalBeat(RivalBeat.EarlyRoute));
        Assert.False(session.HasDefeatedRivalBeat(RivalBeat.Midpoint));
    }
}
