using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class GymBattleTests
{
    private static GeneratedRegion Region(ulong seed = 42)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);

    private static OverworldSession Session(GeneratedRegion region)
        => new(region, new PlayerIdentity(CharacterChoice.CharacterA, "Leaf"));

    [Fact]
    public void GymInteriorBindsTwoGeneratedTrainersAndTheLeaderToItsThreePosts()
    {
        var region = Region();
        var areaId = FirstGymArea(region);
        var template = InteriorTemplateCatalog.For(BuildingKind.Gym);

        var interactions = GymInteriorBattles.Build(region, areaId, template);

        Assert.Equal(3, interactions.Count);
        Assert.Equal(3, interactions.Select(item => item.Position).Distinct().Count());
        Assert.Equal(2, interactions.Count(item => item.Trainer?.IsGymTrainer == true));
        var leader = Assert.Single(interactions, item => item.GymLeader is not null);
        Assert.False(leader.StartsOnSight);
        Assert.Equal(region.Bosses.GymLeaders[areaId], leader.GymLeader);
        Assert.All(interactions, item =>
            Assert.Equal(LogicalTile.TrainerPost, template.Grid[item.Position.X, item.Position.Y]));
    }

    [Fact]
    public void LeaderRequestUsesBossRosterAndAwardsItsOrderedBadgeOnce()
    {
        var region = Region(7);
        var areaId = FirstGymArea(region);
        var template = InteriorTemplateCatalog.For(BuildingKind.Gym);
        var leader = Assert.Single(
            GymInteriorBattles.Build(region, areaId, template), item => item.GymLeader is not null);
        var session = Session(region);

        var request = Assert.IsType<GeneratedTrainerBattleTransitionRequest>(
            session.TryStartTrainerBattle(leader, new PlayerState(template.Arrival)));

        var boss = region.Bosses.GymLeaders[areaId];
        Assert.Equal(boss.Members.Select(member => (member.SpeciesId, member.Level)),
            request.Roster.Select(member => (member.SpeciesId, member.Level)));
        Assert.NotNull(request.GymReward);
        Assert.Equal(areaId, request.GymReward!.AreaId);
        Assert.Equal(1, request.GymReward.BadgeNumber);
        Assert.Equal(0, session.Badges);

        Assert.True(session.CompleteTrainerBattle(request));
        Assert.Equal(1, session.Badges);
        Assert.True(session.HasDefeatedGymLeader(areaId));
        Assert.Null(session.TryStartTrainerBattle(leader, request.ReturnState));
        Assert.False(session.CompleteTrainerBattle(request));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void EveryGeneratedGymHasAnInteriorLeaderWithItsPathOrderedBadge(int badgeCount)
    {
        var region = RegionGenerator.Generate(
            new GenerationSettings { Seed = 123, BadgeCount = badgeCount }, TestData.Data);
        var orderedGyms = region.Identity.GymTypes.Keys
            .OrderBy(id => region.Graph[id].PathIndex)
            .ToArray();
        var session = Session(region);
        var template = InteriorTemplateCatalog.For(BuildingKind.Gym);

        for (var index = 0; index < orderedGyms.Length; index++)
        {
            var areaId = orderedGyms[index];
            Assert.Contains(region.Carved[areaId].BuildingEntrances,
                entrance => entrance.Kind == BuildingKind.Gym);
            var leader = Assert.Single(
                GymInteriorBattles.Build(region, areaId, template), item => item.GymLeader is not null);
            var request = Assert.IsType<GeneratedTrainerBattleTransitionRequest>(
                session.TryStartTrainerBattle(leader, new PlayerState(template.Arrival)));

            Assert.Equal(index + 1, request.GymReward!.BadgeNumber);
        }
    }

    [Fact]
    public void BadgeCountReflectsLeadersDefeatedEvenIfDebugAccessesOneOutOfOrder()
    {
        var region = Region(500);
        var lastAreaId = region.Identity.GymTypes.Keys.MaxBy(id => region.Graph[id].PathIndex);
        var template = InteriorTemplateCatalog.For(BuildingKind.Gym);
        var leader = Assert.Single(
            GymInteriorBattles.Build(region, lastAreaId, template), item => item.GymLeader is not null);
        var session = Session(region);
        var request = Assert.IsType<GeneratedTrainerBattleTransitionRequest>(
            session.TryStartTrainerBattle(leader, new PlayerState(template.Arrival)));

        session.CompleteTrainerBattle(request);

        Assert.Equal(1, session.Badges);
        Assert.True(session.HasDefeatedGymLeader(lastAreaId));
    }

    [Fact]
    public void LeaderWaitsForContactWhileGymTrainersUseSightlines()
    {
        var region = Region(99);
        var areaId = FirstGymArea(region);
        var template = InteriorTemplateCatalog.For(BuildingKind.Gym);
        var interactions = GymInteriorBattles.Build(region, areaId, template);
        var leader = Assert.Single(interactions, item => item.GymLeader is not null);

        Assert.Null(TrainerSightlines.FindEngaging(
            [leader], template.Grid, template.Arrival, new HashSet<string>()));
        Assert.Equal(leader, TrainerSightlines.FindEngaging(
            [leader], template.Grid, leader.Position, new HashSet<string>()));
    }

    [Fact]
    public void EnteredInteriorPersistsAcrossBattleAndCanBeExited()
    {
        var region = Region(777);
        var areaId = FirstGymArea(region);
        var carved = region.Carved[areaId];
        var entrance = carved.BuildingEntrances.Single(item => item.Kind == BuildingKind.Gym);
        var streetFacing = InteriorTransitionResolver.FacingFor(entrance.StreetEdge);
        var transition = Assert.IsType<InteriorTransition>(InteriorTransitionResolver.Enter(
            carved, entrance, new GridPosition(entrance.X, entrance.Y),
            InteriorTransitionResolver.Opposite(streetFacing)));
        var session = Session(region);
        session.VisitArea(areaId);

        session.EnterInterior(transition);

        Assert.NotNull(session.ActiveInterior);
        Assert.Equal(areaId, session.ActiveInterior!.OverworldAreaId);
        Assert.Equal(InteriorKind.Gym, session.ActiveInterior.Template.Kind);

        session.ExitInterior();

        Assert.Null(session.ActiveInterior);
        Assert.Equal(areaId, session.CurrentAreaId);
    }

    private static int FirstGymArea(GeneratedRegion region)
        => region.Identity.GymTypes.Keys.OrderBy(id => region.Graph[id].PathIndex).First();
}
