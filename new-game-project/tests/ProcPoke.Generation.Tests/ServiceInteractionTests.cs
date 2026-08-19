using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Services;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// The overworld half of the services pass: a seated service becomes an interaction in the house the plan
/// put it in, and talking to it does what that service does.
/// </summary>
public sealed class ServiceInteractionTests
{
    private static GeneratedRegion Generate(ulong seed = 42, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    private static OverworldSession NewSession(GeneratedRegion region)
        => new(region, NewGameIdentity.Create(CharacterChoice.CharacterA, "Player"));

    /// <summary>Walks into the house a placement names, the same way the overworld does.</summary>
    private static ActiveInterior EnterHouse(GeneratedRegion region, ServicePlacement placement)
    {
        var entrance = region.Carved[placement.AreaId].BuildingEntrances
            .Where(candidate => candidate.Kind == BuildingKind.House)
            .ElementAt(placement.HouseIndex);
        return new ActiveInterior(
            placement.AreaId, entrance, InteriorTemplateCatalog.For(BuildingKind.House));
    }

    [Fact]
    public void EverySeatedServiceBecomesAnInteractionInItsOwnHouse()
    {
        var region = Generate();
        var housesVisited = 0;

        foreach (var group in region.Services.Placements.GroupBy(
            placement => (placement.AreaId, placement.HouseIndex)))
        {
            var interior = EnterHouse(region, group.First());
            var interactions = ServiceInteractions.Build(region, interior);

            Assert.Equal(
                group.OrderBy(placement => placement.PostIndex)
                    .Select(placement => (ServiceKind?)placement.Kind),
                interactions.Select(interaction => interaction.Service));
            Assert.All(interactions, interaction =>
            {
                Assert.Equal(InteractionKind.Service, interaction.Kind);
                Assert.Equal(group.Key.AreaId, interaction.AreaId);
                Assert.Equal(
                    LogicalTile.NpcPost,
                    interior.Template.Grid[interaction.Position.X, interaction.Position.Y]);
            });
            // Two services in one house must not end up on the same tile.
            Assert.Equal(
                interactions.Count,
                interactions.Select(interaction => interaction.Position).Distinct().Count());
            housesVisited++;
        }

        Assert.True(housesVisited > 0);
    }

    [Fact]
    public void AHouseWithNoServiceRegistersNothing()
    {
        var region = Generate();
        var serviceHouses = region.Services.Placements
            .Select(placement => (placement.AreaId, placement.HouseIndex))
            .ToHashSet();

        var emptyHouse = region.Graph.Areas
            .SelectMany(area => region.Carved[area.Id].BuildingEntrances
                .Where(entrance => entrance.Kind == BuildingKind.House)
                .Select((entrance, index) => (AreaId: area.Id, Index: index, Entrance: entrance)))
            .First(house => !serviceHouses.Contains((house.AreaId, house.Index)));

        var interior = new ActiveInterior(
            emptyHouse.AreaId, emptyHouse.Entrance, InteriorTemplateCatalog.For(BuildingKind.House));

        Assert.Empty(ServiceInteractions.Build(region, interior));
    }

    [Fact]
    public void FossilRevivalBringsOnePendingFossilToLifePerVisit()
    {
        var region = Generate();
        var session = NewSession(region);
        var pending = session.CollectFossils(region.Dex.FossilAreaId);
        Assert.NotEmpty(pending);

        var first = session.UseService(TestData.Data, ServiceKind.FossilRevival);

        Assert.True(first.Changed);
        Assert.Contains(TestData.Data.Species[pending[0].SpeciesId].Name, first.Message);
        Assert.Equal(pending[0].SpeciesId, session.Party.Members[0].SpeciesId);
        Assert.Equal(pending.Count - 1, session.PendingFossilRevivals.Count);

        // Nothing left to revive is a message, not a change.
        while (session.PendingFossilRevivals.Count > 0)
            Assert.True(session.UseService(TestData.Data, ServiceKind.FossilRevival).Changed);
        Assert.False(session.UseService(TestData.Data, ServiceKind.FossilRevival).Changed);
    }

    [Fact]
    public void AMoveTutorFillsAFreeSlotAndStopsAtAFullMoveset()
    {
        var region = Generate();
        var session = NewSession(region);
        // A species that has tutor moves on offer and a free slot to put one in.
        var member = TutorableMember(out var speciesId);
        session.Party.Add(member);

        var offers = MoveTutor.OffersFor(TestData.Data, member);
        var outcome = session.UseService(TestData.Data, ServiceKind.MoveTutor);

        Assert.True(outcome.Changed);
        Assert.Equal(member.MoveIds.Count + 1, session.Party.Members[0].MoveIds.Count);
        Assert.Contains(offers[0], session.Party.Members[0].MoveIds);
        Assert.Contains(TestData.Data.Moves[offers[0]].Name, outcome.Message);
        Assert.Equal(speciesId, session.Party.Members[0].SpeciesId);

        // Fill the moveset; the tutor then refuses rather than overwriting anything.
        var full = session.Party.Members[0];
        while (full.MoveIds.Count < MoveTutor.MoveSlotCount)
        {
            var next = MoveTutor.OffersFor(TestData.Data, full)[0];
            full = MoveTutor.Teach(TestData.Data, full, next);
        }
        session.Party.Replace(0, full);

        var refused = session.UseService(TestData.Data, ServiceKind.MoveTutor);
        Assert.False(refused.Changed);
        Assert.Equal(full.MoveIds, session.Party.Members[0].MoveIds);
    }

    [Fact]
    public void TheMoveDeleterDropsAChosenMoveButNeverTheLastOne()
    {
        var region = Generate();
        var session = NewSession(region);
        var member = BattleProgressionFactory.AtLevel(TestData.Data, 1, 30);
        Assert.True(member.MoveIds.Count > 1);
        session.Party.Add(member);

        var doomed = member.MoveIds[0];
        Assert.True(session.DeleteMove(0, doomed));
        Assert.DoesNotContain(doomed, session.Party.Members[0].MoveIds);
        Assert.False(session.DeleteMove(0, doomed), "a move it no longer knows cannot be deleted");

        while (session.Party.Members[0].MoveIds.Count > 1)
            Assert.True(session.DeleteMove(0, session.Party.Members[0].MoveIds[0]));

        var last = session.Party.Members[0].MoveIds[0];
        Assert.False(session.DeleteMove(0, last), "the last move must survive");
        Assert.Equal([last], session.Party.Members[0].MoveIds);
    }

    [Fact]
    public void TheNameRaterRenamesAndRejectsUnusableNames()
    {
        var region = Generate();
        var session = NewSession(region);
        session.Party.Add(BattleProgressionFactory.AtLevel(TestData.Data, 1, 5));

        Assert.False(session.RenamePartyMember(0, "   "));
        Assert.False(session.RenamePartyMember(0, new string('x', Nicknames.MaxLength + 1)));
        Assert.False(session.RenamePartyMember(0, "bad\nname"));
        Assert.Null(session.Party.Members[0].Nickname);

        Assert.True(session.RenamePartyMember(0, "  Sprout  "));
        Assert.Equal("Sprout", session.Party.Members[0].Nickname);
        Assert.Equal("Sprout", Nicknames.DisplayName(TestData.Data, session.Party.Members[0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => { session.RenamePartyMember(1, "Nope"); });
    }

    /// <summary>A nickname is not battle state; winning a battle (and evolving) must not wipe it.</summary>
    [Fact]
    public void ANicknameSurvivesBattleRewards()
    {
        var named = BattleProgressionFactory.AtLevel(TestData.Data, 1, 5) with { Nickname = "Sprout" };
        var reward = BattleRewardResolver.ApplyDefeat(
            TestData.Data, named, defeatedSpeciesId: 16, defeatedLevel: 30, survivingHp: 5);

        Assert.Equal("Sprout", reward.Progression.Nickname);
    }

    [Fact]
    public void ServicesTurnAnEmptyPartyAwayInsteadOfThrowing()
    {
        var region = Generate();
        var session = NewSession(region);

        foreach (var kind in new[] { ServiceKind.MoveDeleter, ServiceKind.NameRater, ServiceKind.MoveTutor })
        {
            var outcome = session.UseService(TestData.Data, kind);
            Assert.False(outcome.Changed);
            Assert.NotEmpty(outcome.Message);
        }
        // Fossil revival is the exception: it has something to say with no Pokémon in the party at all.
        Assert.False(session.UseService(TestData.Data, ServiceKind.FossilRevival).Changed);
    }

    /// <summary>A baked species with tutor moves and a single known move, so a tutor has both an offer to
    /// make and a slot to put it in.</summary>
    private static BattleProgressionState TutorableMember(out int speciesId)
    {
        foreach (var (candidate, entries) in TestData.Data.Learnsets.OrderBy(pair => pair.Key))
        {
            if (!entries.Any(entry => entry.Method == LearnMethod.Tutor)) continue;
            if (!TestData.Data.Species.ContainsKey(candidate)) continue;
            var member = BattleProgressionFactory.AtLevel(TestData.Data, candidate, 5);
            if (member.MoveIds.Count >= MoveTutor.MoveSlotCount) continue;
            if (MoveTutor.OffersFor(TestData.Data, member).Count == 0) continue;
            speciesId = candidate;
            return member;
        }
        throw new InvalidOperationException("baked data has no species with a free slot and a tutor move");
    }
}
