using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class LeagueBattleTests
{
    private static GeneratedRegion Region(ulong seed = 42)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);

    private static OverworldSession Session(GeneratedRegion region)
        => new(region, new PlayerIdentity(CharacterChoice.CharacterA, "Leaf"));

    [Fact]
    public void LeaguePostsBindTheFourEliteFourTeamsAndChampionInCarvedOrder()
    {
        var region = Region();
        var registry = InteractionRegistry.Build(region);
        var posts = registry.Of(region.Graph.LeagueAreaId)
            .Where(interaction => interaction.LeagueStage is not null)
            .OrderBy(interaction => interaction.LeagueStage)
            .ToArray();

        Assert.Equal(region.Bosses.EliteFour.Count + 1, posts.Length);
        Assert.Equal(region.Bosses.EliteFour.Select(team => team.Name)
            .Append(region.Bosses.Champion.Name), posts.Select(post => post.Boss!.Name));
        Assert.All(posts, post => Assert.Equal(InteractionKind.Trainer, post.Kind));
        Assert.False(posts[^1].StartsOnSight);
    }

    [Fact]
    public void LeagueRequiresBadgesAndThenEnforcesAOneTimeGauntletThroughCredits()
    {
        var region = Region(7);
        var leagueId = region.Graph.LeagueAreaId;
        var posts = InteractionRegistry.Build(region).Of(leagueId)
            .Where(interaction => interaction.LeagueStage is not null)
            .OrderBy(interaction => interaction.LeagueStage)
            .ToArray();
        var session = Session(region);
        var returnState = new PlayerState(new GridPosition(1, 1));

        Assert.Null(session.TryStartTrainerBattle(posts[0], returnState));
        session.SetBadges(region.Identity.GymTypes.Count);

        var money = 0;
        foreach (var post in posts)
        {
            var request = Assert.IsType<GeneratedTrainerBattleTransitionRequest>(
                session.TryStartTrainerBattle(post, returnState));
            Assert.Equal(post.LeagueStage, request.LeagueReward!.Stage);
            Assert.Equal(leagueId, request.LeagueReward.AreaId);
            money += request.PrizeMoney;
            Assert.True(session.CompleteTrainerBattle(request));
            Assert.Null(session.TryStartTrainerBattle(post, returnState));
        }

        Assert.Equal(money, session.Money);
        Assert.Equal(Enumerable.Range(0, posts.Length).ToHashSet(), session.DefeatedLeagueStages);
        Assert.True(session.CreditsReached);
    }

    [Fact]
    public void LeagueRewardCannotBeCompletedOutOfOrder()
    {
        var region = Region(99);
        var posts = InteractionRegistry.Build(region).Of(region.Graph.LeagueAreaId)
            .Where(interaction => interaction.LeagueStage is not null)
            .OrderBy(interaction => interaction.LeagueStage)
            .ToArray();
        var session = Session(region);
        session.SetBadges(region.Identity.GymTypes.Count);

        var champion = Assert.Single(posts, post => post.LeagueStage == region.Bosses.EliteFour.Count);
        Assert.Null(session.TryStartTrainerBattle(champion, new PlayerState(new GridPosition(1, 1))));
        Assert.Null(TrainerSightlines.FindEngaging(
            [champion], region.Carved[region.Graph.LeagueAreaId].Grid,
            champion.Position with { X = champion.Position.X - 1 }, new HashSet<string>()));
    }

    [Fact]
    public void RivalChampionSeedsUseTheSelectedPlayersRivalChampionTeam()
    {
        GeneratedRegion? region = null;
        for (ulong seed = 1; seed < 500 && (region is null || !region.Identity.ChampionIsRival); seed++)
        {
            var candidate = Region(seed);
            if (candidate.Identity.ChampionIsRival) region = candidate;
        }

        Assert.NotNull(region);
        var resolved = region!;
        var posts = InteractionRegistry.Build(resolved).Of(resolved.Graph.LeagueAreaId)
            .Where(interaction => interaction.LeagueStage is not null)
            .OrderBy(interaction => interaction.LeagueStage)
            .ToArray();
        var session = Session(resolved);
        session.SetStarterCorner(0);
        session.SetBadges(resolved.Identity.GymTypes.Count);
        for (var stage = 0; stage < resolved.Bosses.EliteFour.Count; stage++)
        {
            var request = Assert.IsType<GeneratedTrainerBattleTransitionRequest>(
                session.TryStartTrainerBattle(posts[stage], new PlayerState(new GridPosition(1, 1))));
            session.CompleteTrainerBattle(request);
        }

        var championRequest = Assert.IsType<GeneratedTrainerBattleTransitionRequest>(
            session.TryStartTrainerBattle(posts[^1], new PlayerState(new GridPosition(1, 1))));
        var rivalChampion = resolved.Rivals.ForPlayerCorner(0).ChampionTeam;
        Assert.NotNull(rivalChampion);
        Assert.Equal(rivalChampion!.Members.Select(member => (member.SpeciesId, member.Level)),
            championRequest.Roster.Select(member => (member.SpeciesId, member.Level)));
    }
}
