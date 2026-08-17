using ProcPoke.Battle;
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
    public void VisitingAGatesKeyAreaGrantsItsKeyAndVisitingFlysAreaGrantsFly()
    {
        var region = Generate(31337);
        var session = new OverworldSession(
            region, NewGameIdentity.Create(CharacterChoice.CharacterA, "Player"));
        var gate = region.Gating.Gates.First(g => g.KeyAreaId != region.Graph.StartAreaId);

        Assert.False(session.Interactions.HasKey(gate.KeyName));
        session.VisitArea(gate.KeyAreaId);
        Assert.True(session.Interactions.HasKey(gate.KeyName));

        Assert.False(session.HasFly);
        session.VisitArea(region.Gating.Fly.AreaId);
        Assert.True(session.HasFly);
    }

    [Fact]
    public void LegendaryEncounterAdvancesThroughRoundRobinPlacementsUntilAllResolved()
    {
        var region = Generate(31337);
        var session = new OverworldSession(
            region, NewGameIdentity.Create(CharacterChoice.CharacterA, "Player"));
        var returnState = new PlayerState(new GridPosition(1, 1));
        // Round-robin placement (4c) can seat more than one legendary in the same dungeon; walk every
        // legendary sharing this area in order and confirm the trigger advances to the next unresolved
        // one rather than stopping or re-firing an already-resolved placement.
        var sharedArea = region.Special.Legendaries
            .Where(legendary => legendary.AreaId == region.Special.Legendaries[0].AreaId)
            .ToArray();

        foreach (var expected in sharedArea)
        {
            // Unresolved legendaries keep triggering on repeated visits — the read is stateless.
            Assert.NotNull(session.TryTriggerLegendaryEncounter(expected.AreaId, returnState));

            var request = session.TryTriggerLegendaryEncounter(expected.AreaId, returnState);
            Assert.NotNull(request);
            Assert.Same(expected, request!.Legendary);
            Assert.Equal(expected.SpeciesId, request.Battle.SpeciesId);
            Assert.Equal(expected.Level, request.Battle.Level);
            Assert.Equal(returnState, request.ReturnState);

            Assert.False(session.HasResolvedLegendary(expected.SpeciesId));
            session.MarkLegendaryResolved(expected.SpeciesId);
            Assert.True(session.HasResolvedLegendary(expected.SpeciesId));
        }

        Assert.Null(session.TryTriggerLegendaryEncounter(sharedArea[0].AreaId, returnState));
    }

    [Fact]
    public void FossilsAreCollectedOnceIntoTheParty()
    {
        var region = Generate(31337);
        var session = new OverworldSession(
            region, NewGameIdentity.Create(CharacterChoice.CharacterA, "Player"));
        var fossilAreaId = region.Dex.FossilAreaId;
        var otherAreaId = region.Graph.Areas.First(area => area.Id != fossilAreaId).Id;
        var expectedLevel = region.Encounters.Of(fossilAreaId)!.BaseLevel;

        Assert.Empty(session.TryCollectFossils(otherAreaId));

        var firstVisit = session.TryCollectFossils(fossilAreaId);
        Assert.Equal(region.Special.Fossils.Count, firstVisit.Count);
        Assert.Equal(
            region.Special.Fossils.Select(fossil => fossil.SpeciesId).OrderBy(id => id),
            firstVisit.Select(pickup => pickup.SpeciesId).OrderBy(id => id));
        Assert.All(firstVisit, pickup => Assert.Equal(expectedLevel, pickup.Level));
        Assert.All(firstVisit, pickup => Assert.True(session.HasCollectedFossil(pickup.SpeciesId)));

        Assert.Empty(session.TryCollectFossils(fossilAreaId));
    }

    [Fact]
    public void FossilsAreSkippedRatherThanOverflowingAFullParty()
    {
        var region = Generate(31337);
        var session = new OverworldSession(
            region, NewGameIdentity.Create(CharacterChoice.CharacterA, "Player"));
        for (var i = 0; i < Party.MaxSize; i++)
            session.Party.Add(BattleProgressionFactory.AtLevel(TestData.Data, 1, 5));

        var pickups = session.TryCollectFossils(region.Dex.FossilAreaId);

        Assert.Empty(pickups);
        Assert.All(region.Special.Fossils, fossil => Assert.False(session.HasCollectedFossil(fossil.SpeciesId)));
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

    [Fact]
    public void WhiteoutReturnsToTheLastVisitedPokemonCenter()
    {
        var region = Generate(31337);
        var town = region.Graph.Areas.First(area => area.Archetype == AreaArchetype.Town);
        var route = region.Graph.Areas.First(area => area.Archetype == AreaArchetype.Route);
        var session = new OverworldSession(
            region, NewGameIdentity.Create(CharacterChoice.CharacterA, "Player"));

        session.VisitArea(town.Id);
        session.VisitArea(route.Id);
        var result = session.ApplyWhiteout();

        Assert.Equal(route.Id, result.FromAreaId);
        Assert.Equal(town.Id, result.DestinationAreaId);
        Assert.False(result.WasAlreadyAtCenter);
        Assert.Equal(town.Id, session.CurrentAreaId);
        Assert.Equal(town.Id, session.LastPokemonCenterAreaId);
    }

    [Fact]
    public void WhiteoutResolverRejectsNonCenterReturnLocations()
    {
        var region = Generate(8080);
        var route = region.Graph.Areas.First(area => area.Archetype == AreaArchetype.Route);

        Assert.Throws<ArgumentException>(() => WhiteoutResolver.Resolve(
            region, region.Graph.StartAreaId, route.Id));
    }
}
