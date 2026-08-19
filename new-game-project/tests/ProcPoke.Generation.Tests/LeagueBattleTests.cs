using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
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
    public void LeagueRequiresBadgesAndThenEnforcesAnInOrderGauntletThroughCredits()
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
            // A stage already won cannot be re-fought out of turn, the Champion included.
            Assert.Null(session.TryStartTrainerBattle(post, returnState));
        }

        Assert.Equal(money, session.Money);
        Assert.Equal(Enumerable.Range(0, posts.Length).ToHashSet(), session.DefeatedLeagueStages);
        Assert.True(session.CreditsReached);
        // Beating the Champion ends the run, so the ladder is climbable again from its first stage.
        Assert.NotNull(session.TryStartTrainerBattle(posts[0], returnState));
    }

    /// <summary>
    /// GDD §11a: beating the Champion reopens the League at +10-15 levels, which is what makes the
    /// legendary respawn rule (§5.5) functional. The rematch is the same generated teams, levelled up —
    /// no new content, and no way to skip the ladder.
    /// </summary>
    [Fact]
    public void BeatingTheChampionReopensTheLadderAtRematchLevels()
    {
        var region = Region(7);
        var posts = InteractionRegistry.Build(region).Of(region.Graph.LeagueAreaId)
            .Where(interaction => interaction.LeagueStage is not null)
            .OrderBy(interaction => interaction.LeagueStage)
            .ToArray();
        var session = Session(region);
        var returnState = new PlayerState(new GridPosition(1, 1));
        session.SetBadges(region.Identity.GymTypes.Count);

        var firstRunLevels = new List<IReadOnlyList<int>>();
        foreach (var post in posts)
        {
            var request = session.TryStartTrainerBattle(post, returnState)!;
            firstRunLevels.Add(request.Roster.Select(member => member.Level).ToArray());
            Assert.True(session.CompleteTrainerBattle(request));
        }
        Assert.True(session.LeagueRematch);

        // The ladder starts over: stage 0 first, and the Champion is out of reach again until then.
        Assert.Null(session.TryStartTrainerBattle(posts[1], returnState));
        for (var stage = 0; stage < posts.Length; stage++)
        {
            var rematch = session.TryStartTrainerBattle(posts[stage], returnState)!;
            Assert.Equal(
                firstRunLevels[stage].Select(level =>
                    Math.Min(100, level + OverworldSession.LeagueRematchLevelBonus)),
                rematch.Roster.Select(member => member.Level));
            Assert.True(session.CompleteTrainerBattle(rematch));
        }
    }

    /// <summary>A legendary the player beat comes back with the next Champion victory; one they caught
    /// does not (GDD §5.5).</summary>
    [Fact]
    public void ChampionVictoryWakesBeatenLegendariesButNotCaughtOnes()
    {
        var region = Region(7);
        Assert.True(region.Special.Legendaries.Count >= 2);
        var beaten = region.Special.Legendaries[0].SpeciesId;
        var caught = region.Special.Legendaries[1].SpeciesId;
        var posts = InteractionRegistry.Build(region).Of(region.Graph.LeagueAreaId)
            .Where(interaction => interaction.LeagueStage is not null)
            .OrderBy(interaction => interaction.LeagueStage)
            .ToArray();
        var session = Session(region);
        session.SetBadges(region.Identity.GymTypes.Count);

        session.MarkLegendaryDefeated(beaten);
        session.MarkLegendaryCaptured(caught);
        Assert.True(session.HasResolvedLegendary(beaten));
        Assert.True(session.HasResolvedLegendary(caught));

        foreach (var post in posts)
            Assert.True(session.CompleteTrainerBattle(
                session.TryStartTrainerBattle(post, new PlayerState(new GridPosition(1, 1)))!));

        Assert.False(session.HasResolvedLegendary(beaten));
        Assert.True(session.HasResolvedLegendary(caught));
    }

    /// <summary>The post-game cave stays shut until the credits roll, and nothing the run needs is inside
    /// it (GDD §11a) — the gating passes keep keys, hints and Fly out.</summary>
    [Fact]
    public void ThePostGameCaveOpensOnlyForAChampion()
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var region = Region(seed);
            var postGame = region.Graph.Areas.Where(area => area.Archetype.IsPostGame()).ToList();
            Assert.NotEmpty(postGame);
            var session = Session(region);

            foreach (var area in postGame)
            {
                Assert.False(session.CanEnterArea(area.Id));
                Assert.NotEmpty(session.AreaEntryRefusal(area.Id));
                Assert.All(region.Gating.Gates, gate =>
                {
                    Assert.NotEqual(area.Id, gate.KeyAreaId);
                    Assert.NotEqual(area.Id, gate.HintAreaId);
                });
                Assert.NotEqual(area.Id, region.Gating.Fly.AreaId);
            }

            // Every other area is enterable from the start; only the post-game one is not.
            Assert.All(
                region.Graph.Areas.Where(area => !area.Archetype.IsPostGame()),
                area => Assert.True(session.CanEnterArea(area.Id)));
            checkedCount++;
        }
        Assert.True(checkedCount > 0);
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
