using ProcPoke.Generation;
using ProcPoke.Generation.Topology;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class SessionStateTests
{
    private static GeneratedRegion Generate(ulong seed = 42)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);

    [Fact]
    public void NewGameCapturesTrimmedIdentityBeforeExploration()
    {
        var identity = NewGameIdentity.Create(CharacterChoice.CharacterB, "  Misty  ");

        Assert.Equal(CharacterChoice.CharacterB, identity.Character);
        Assert.Equal("Misty", identity.Name);
        Assert.Throws<ArgumentException>(() => NewGameIdentity.Create(CharacterChoice.CharacterA, ""));
        Assert.Throws<ArgumentException>(() => NewGameIdentity.Create(
            CharacterChoice.CharacterA, new string('x', NewGameIdentity.MaxNameLength + 1)));
    }

    [Fact]
    public void VisitedCitiesBecomeFlyTargetsOnlyWithPermission()
    {
        var region = Generate();
        var town = region.Graph.Areas.First(area => area.Archetype == AreaArchetype.Town);
        var map = new RegionMapState(region.Graph.StartAreaId);
        map.Visit(town.Id);

        Assert.Empty(map.FlyTargets(region, hasFly: false, badges: 8));
        Assert.Contains(town.Id, map.FlyTargets(
            region, hasFly: true, badges: region.Gating.Fly.BadgePrerequisite));
        Assert.All(region.Graph.Areas
            .Where(area => area.Archetype is AreaArchetype.Town && area.Id != town.Id)
            .Select(area => area.Id), areaId =>
            Assert.DoesNotContain(areaId, map.FlyTargets(region, true, 8)));
        Assert.True(map.CanFlyTo(region, town.Id, true, 8));
        Assert.False(map.CanFlyTo(region, region.Graph.LeagueAreaId, true, 8));
        Assert.Contains(town.Id, map.FlyTargets(region, false, 0, debugUnlock: true));
    }

    [Fact]
    public void SessionTracksVisitsFlyAndPresentationTimeWithoutChangingRegion()
    {
        var region = Generate(777);
        var before = region.World.Grid[0, 0];
        var session = new OverworldSession(
            region, NewGameIdentity.Create(CharacterChoice.CharacterA, "Player"));
        var town = region.Graph.Areas.First(area => area.Archetype == AreaArchetype.Town);

        session.VisitArea(town.Id);
        session.SetTime(-1);
        session.EnableDebugUnlock();

        Assert.Equal(town.Id, session.CurrentAreaId);
        Assert.True(session.Map.IsVisited(town.Id));
        Assert.Equal(TimeOfDayPhase.Night, session.Time.Phase);
        Assert.Contains(town.Id, session.FlyTargets());
        Assert.True(session.TryFlyTo(town.Id));
        Assert.Equal(before, region.World.Grid[0, 0]);
    }

    [Fact]
    public void TimeOfDayHasStablePresentationBandsAndDebugFacts()
    {
        var dawn = TimeOfDayClock.FromMinutes(6 * 60);
        var day = TimeOfDayClock.FromMinutes(12 * 60);
        var dusk = TimeOfDayClock.FromMinutes(19 * 60);
        var night = TimeOfDayClock.FromMinutes(23 * 60);

        Assert.Equal(TimeOfDayPhase.Dawn, dawn.Phase);
        Assert.Equal(TimeOfDayPhase.Day, day.Phase);
        Assert.Equal(TimeOfDayPhase.Dusk, dusk.Phase);
        Assert.True(night.IsNight);
        Assert.NotEqual(day.Tint, night.Tint);
        Assert.Equal(night, TimeOfDayClock.FromMinutes(-60));

        var info = GenerationDebugInfo.From(Generate(2026));
        Assert.Equal((ulong)2026, info.Seed);
        Assert.Equal(GeneratorVersion.Current, info.GeneratorVersion);
        Assert.Equal(150, info.DexSize);
        Assert.Equal(8, info.BadgeCount);
    }
}
