using ProcPoke.Battle;
using ProcPoke.Generation;
using ProcPoke.Generation.Bosses;
using ProcPoke.Generation.Rivals;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Overworld;

public enum CharacterChoice
{
    CharacterA,
    CharacterB,
}

/// <summary>Player-owned identity captured before the generated StartTown becomes playable.</summary>
public sealed record PlayerIdentity(CharacterChoice Character, string Name);

public static class NewGameIdentity
{
    public const int MaxNameLength = 12;

    public static PlayerIdentity Create(CharacterChoice character, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > MaxNameLength)
            throw new ArgumentException($"player name cannot exceed {MaxNameLength} characters", nameof(name));
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("player name cannot contain control characters", nameof(name));
        return new PlayerIdentity(character, normalized);
    }
}

/// <summary>Visited locations and Fly targeting are session state, never properties of generated maps.</summary>
public sealed class RegionMapState
{
    private readonly HashSet<int> _visited = [];

    public RegionMapState(int startAreaId) => Visit(startAreaId);

    public IReadOnlySet<int> VisitedAreaIds => _visited;

    public bool IsVisited(int areaId) => _visited.Contains(areaId);

    public void Visit(int areaId) => _visited.Add(areaId);

    public IReadOnlyList<int> FlyTargets(
        GeneratedRegion region, bool hasFly, int badges, bool debugUnlock = false)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (!CanUseFly(region, hasFly, badges, debugUnlock)) return [];
        return region.Graph.Areas
            .Where(area => IsCity(area) && _visited.Contains(area.Id))
            .OrderBy(area => area.PathIndex < 0 ? int.MaxValue : area.PathIndex)
            .ThenBy(area => area.Id)
            .Select(area => area.Id)
            .ToArray();
    }

    public bool CanFlyTo(
        GeneratedRegion region, int areaId, bool hasFly, int badges, bool debugUnlock = false)
    {
        ArgumentNullException.ThrowIfNull(region);
        return CanUseFly(region, hasFly, badges, debugUnlock)
            && _visited.Contains(areaId)
            && region.Graph.Areas.Any(area => area.Id == areaId && IsCity(area));
    }

    private static bool CanUseFly(GeneratedRegion region, bool hasFly, int badges, bool debugUnlock)
        => debugUnlock || (hasFly && badges >= region.Gating.Fly.BadgePrerequisite);

    private static bool IsCity(Area area)
        => area.Archetype is AreaArchetype.StartTown or AreaArchetype.Town;
}

public enum TimeOfDayPhase
{
    Night,
    Dawn,
    Day,
    Dusk,
}

/// <summary>Engine-independent tint data; Godot converts this to its Color type at the presentation edge.</summary>
public readonly record struct TintColor(float Red, float Green, float Blue, float Alpha = 1f);

public readonly record struct TimeOfDayState(
    int MinutesSinceMidnight, TimeOfDayPhase Phase, TintColor Tint)
{
    public bool IsNight => Phase == TimeOfDayPhase.Night;
}

/// <summary>Maps a presentation clock to stable periods without touching generation RNG or region data.</summary>
public static class TimeOfDayClock
{
    public const int MinutesPerDay = 24 * 60;

    public static TimeOfDayState FromMinutes(int minutesSinceMidnight)
    {
        var minute = ((minutesSinceMidnight % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
        var phase = minute switch
        {
            < 5 * 60 => TimeOfDayPhase.Night,
            < 7 * 60 => TimeOfDayPhase.Dawn,
            < 18 * 60 => TimeOfDayPhase.Day,
            < 20 * 60 => TimeOfDayPhase.Dusk,
            _ => TimeOfDayPhase.Night,
        };
        return new TimeOfDayState(minute, phase, TintFor(phase));
    }

    private static TintColor TintFor(TimeOfDayPhase phase) => phase switch
    {
        TimeOfDayPhase.Night => new TintColor(0.62f, 0.70f, 0.92f),
        TimeOfDayPhase.Dawn => new TintColor(1.00f, 0.88f, 0.78f),
        TimeOfDayPhase.Dusk => new TintColor(1.00f, 0.78f, 0.68f),
        _ => new TintColor(1.00f, 1.00f, 1.00f),
    };
}

/// <summary>Stable debug facts shown alongside a generated region for reproducible map reports.</summary>
public sealed record GenerationDebugInfo(
    ulong Seed, int DexSize, int RosterCap, int BadgeCount, int GeneratorVersion)
{
    public static GenerationDebugInfo From(GeneratedRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return new GenerationDebugInfo(
            region.Settings.Seed,
            region.Settings.DexSize,
            region.Settings.RosterCap,
            region.Settings.BadgeCount,
            ProcPoke.Generation.GeneratorVersion.Current);
    }
}

/// <summary>Small in-memory Phase 3 session tying identity, map state, Fly, and presentation time together.</summary>
public sealed class OverworldSession
{
    private bool _hasFly;
    private int? _starterCornerIndex;
    private readonly HashSet<RivalBeat> _defeatedRivalBeats = [];
    private readonly HashSet<string> _defeatedTrainerIds = new(StringComparer.Ordinal);
    private readonly HashSet<int> _defeatedGymAreaIds = [];
    private readonly HashSet<int> _defeatedLeagueStages = [];
    private bool _creditsReached;

    public OverworldSession(GeneratedRegion region, PlayerIdentity player)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(player);
        Region = region;
        Player = player;
        CurrentAreaId = region.Graph.StartAreaId;
        LastPokemonCenterAreaId = CurrentAreaId;
        Map = new RegionMapState(CurrentAreaId);
        Time = TimeOfDayClock.FromMinutes(12 * 60);
        Interactions = new InteractionSession();
        Party = new Party();
    }

    public GeneratedRegion Region { get; }
    public PlayerIdentity Player { get; }
    public Party Party { get; }
    public InteractionSession Interactions { get; }
    public RegionMapState Map { get; }
    public TimeOfDayState Time { get; private set; }
    public int CurrentAreaId { get; private set; }
    public int LastPokemonCenterAreaId { get; private set; }
    public int Badges { get; private set; }
    public int Money { get; private set; }
    public ActiveInterior? ActiveInterior { get; private set; }
    public bool HasFly => _hasFly || Interactions.DebugUnlock;
    public bool DebugUnlock => Interactions.DebugUnlock;

    public void VisitArea(int areaId)
    {
        if (!Region.Graph.Areas.Any(area => area.Id == areaId))
            throw new ArgumentOutOfRangeException(nameof(areaId), areaId, "area is not in this region");
        CurrentAreaId = areaId;
        ActiveInterior = null;
        if (IsPokemonCenter(areaId)) LastPokemonCenterAreaId = areaId;
        Map.Visit(areaId);
    }

    public void SetTime(int minutesSinceMidnight)
        => Time = TimeOfDayClock.FromMinutes(minutesSinceMidnight);

    public void SetBadges(int badges)
    {
        if (badges < 0) throw new ArgumentOutOfRangeException(nameof(badges));
        Badges = badges;
    }

    public void GrantFly() => _hasFly = true;

    /// <summary>Records which <see cref="Generation.Roster.StarterPlan"/> corner the player picked — needed
    /// to look up the matching <see cref="RivalVariant"/> for every later scripted rival beat.</summary>
    public void SetStarterCorner(int cornerIndex)
    {
        if (cornerIndex < 0 || cornerIndex >= Region.Starters.Corners.Count)
            throw new ArgumentOutOfRangeException(nameof(cornerIndex));
        _starterCornerIndex = cornerIndex;
    }

    public bool HasDefeatedRivalBeat(RivalBeat beat) => _defeatedRivalBeats.Contains(beat);

    public void MarkRivalBeatDefeated(RivalBeat beat) => _defeatedRivalBeats.Add(beat);

    public bool HasDefeatedTrainer(string trainerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trainerId);
        return _defeatedTrainerIds.Contains(trainerId);
    }

    public IReadOnlySet<string> DefeatedTrainerIds => _defeatedTrainerIds;

    public bool HasDefeatedGymLeader(int areaId) => _defeatedGymAreaIds.Contains(areaId);
    public bool HasDefeatedLeagueStage(int stage) => _defeatedLeagueStages.Contains(stage);
    public IReadOnlySet<int> DefeatedLeagueStages => _defeatedLeagueStages;
    public bool CreditsReached => _creditsReached;

    public void EnterInterior(InteriorTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        if (transition.Kind != InteriorTransitionKind.Enter)
            throw new ArgumentException("transition does not enter an interior", nameof(transition));
        if (transition.OverworldAreaId != CurrentAreaId)
            throw new ArgumentException("interior belongs to a different overworld area", nameof(transition));

        var entrance = Region.Carved[CurrentAreaId].BuildingEntrances.FirstOrDefault(candidate =>
            candidate.X == transition.OverworldPosition.X
            && candidate.Y == transition.OverworldPosition.Y
            && InteriorTemplateCatalog.For(candidate.Kind).Kind == transition.InteriorKind)
            ?? throw new ArgumentException("transition does not match a generated building entrance", nameof(transition));
        ActiveInterior = new ActiveInterior(CurrentAreaId, entrance, transition.Template);
    }

    public void ExitInterior() => ActiveInterior = null;

    /// <summary>Builds a battle hand-off directly from the immutable trainer plan attached to a map marker.</summary>
    public GeneratedTrainerBattleTransitionRequest? TryStartTrainerBattle(
        InteractionRegistration interaction, PlayerState returnState)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(returnState);
        if (interaction.Kind != InteractionKind.Trainer)
            throw new ArgumentException("interaction is not a trainer", nameof(interaction));
        if (HasDefeatedTrainer(interaction.Id)) return null;

        if (interaction.Trainer is { } trainer)
        {
            var roster = trainer.Roster
                .Select(member => new TrainerRosterMember(member.SpeciesId, member.Level))
                .ToArray();
            var prize = checked(TrainerPayout.BaseRateFor(trainer.ClassName) * trainer.AceLevel);
            return new GeneratedTrainerBattleTransitionRequest(
                interaction.Id, trainer.DisplayClass, returnState, roster, prize);
        }

        if (interaction.Boss is { } interactionBoss && interaction.LeagueStage is int stage)
        {
            if (!CanStartLeagueStage(interaction.AreaId, stage)) return null;
            var boss = ChampionTeamForPlayer(stage, interactionBoss);
            var roster = boss.Members
                .Select(member => new TrainerRosterMember(member.SpeciesId, member.Level))
                .ToArray();
            var aceLevel = boss.Members.Max(member => member.Level);
            var rate = stage == Region.Bosses.EliteFour.Count ? "Champion" : "Elite Four";
            return new GeneratedTrainerBattleTransitionRequest(
                interaction.Id, boss.Name, returnState, roster,
                checked(TrainerPayout.BaseRateFor(rate) * aceLevel),
                LeagueReward: new LeagueVictoryReward(interaction.AreaId, stage));
        }

        if (interaction.GymLeader is { } leader)
        {
            var areaId = interaction.AreaId;
            if (HasDefeatedGymLeader(areaId)) return null;
            var roster = leader.Members
                .Select(member => new TrainerRosterMember(member.SpeciesId, member.Level))
                .ToArray();
            var aceLevel = leader.Members.Max(member => member.Level);
            var badgeNumber = Region.Identity.GymTypes.Keys
                .OrderBy(id => Region.Graph[id].PathIndex)
                .Select((id, index) => (id, Badge: index + 1))
                .Single(pair => pair.id == areaId).Badge;
            return new GeneratedTrainerBattleTransitionRequest(
                interaction.Id, leader.Name, returnState, roster,
                checked(TrainerPayout.BaseRateFor("Gym Leader") * aceLevel),
                new GymVictoryReward(areaId, badgeNumber));
        }

        throw new InvalidOperationException($"trainer interaction {interaction.Id} has no generated encounter");
    }

    /// <summary>Commits the one-time victory flag and prize atomically; duplicate completion is a no-op.</summary>
    public bool CompleteTrainerBattle(GeneratedTrainerBattleTransitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PrizeMoney < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "trainer prize money cannot be negative");
        if (_defeatedTrainerIds.Contains(request.TrainerId)) return false;
        if (request.GymReward is { } gymReward)
        {
            var expectedBadge = Region.Identity.GymTypes.Keys
                .OrderBy(id => Region.Graph[id].PathIndex)
                .Select((id, index) => (id, Badge: index + 1))
                .SingleOrDefault(pair => pair.id == gymReward.AreaId);
            if (expectedBadge == default || expectedBadge.Badge != gymReward.BadgeNumber)
                throw new ArgumentException("gym reward does not match this region", nameof(request));
        }
        if (request.LeagueReward is { } leagueReward)
        {
            if (leagueReward.AreaId != Region.Graph.LeagueAreaId
                || leagueReward.Stage < 0 || leagueReward.Stage > Region.Bosses.EliteFour.Count)
                throw new ArgumentException("League reward does not match this region", nameof(request));
            if (!CanStartLeagueStage(leagueReward.AreaId, leagueReward.Stage))
                throw new ArgumentException("League reward is out of order", nameof(request));
        }
        var updatedMoney = checked(Money + request.PrizeMoney);
        _defeatedTrainerIds.Add(request.TrainerId);
        if (request.GymReward is { } reward)
        {
            _defeatedGymAreaIds.Add(reward.AreaId);
            Badges = Math.Max(Badges, _defeatedGymAreaIds.Count);
        }
        if (request.LeagueReward is { } league)
        {
            _defeatedLeagueStages.Add(league.Stage);
            if (league.Stage == Region.Bosses.EliteFour.Count) _creditsReached = true;
        }
        Money = updatedMoney;
        return true;
    }

    private bool CanStartLeagueStage(int areaId, int stage)
    {
        if (areaId != Region.Graph.LeagueAreaId || stage < 0 || stage > Region.Bosses.EliteFour.Count)
            return false;
        if (!DebugUnlock && Badges < Region.Identity.GymTypes.Count) return false;
        if (_defeatedLeagueStages.Contains(stage)) return false;
        return stage == 0 || _defeatedLeagueStages.Contains(stage - 1);
    }

    private BossTeam ChampionTeamForPlayer(int stage, BossTeam generatedChampion)
    {
        if (stage != Region.Bosses.EliteFour.Count
            || !Region.Identity.ChampionIsRival
            || _starterCornerIndex is not int corner)
            return generatedChampion;
        return Region.Rivals.ForPlayerCorner(corner).ChampionTeam ?? generatedChampion;
    }

    /// <summary>
    /// Checks whether entering <paramref name="areaId"/> should start a scripted rival battle: the starter
    /// corner must be known (debug sessions that never ran the real new-game flow skip rival beats
    /// entirely) and the first undefeated <see cref="RivalBeatTriggers.MapTriggeredBeats"/> whose trigger
    /// area matches wins — losing a beat does not mark it defeated, so re-entering the same area retries it.
    /// </summary>
    public RivalBattleTransitionRequest? TryTriggerRivalBattle(int areaId, PlayerState returnState)
    {
        if (_starterCornerIndex is not int cornerIndex) return null;

        foreach (var beat in RivalBeatTriggers.MapTriggeredBeats)
        {
            if (HasDefeatedRivalBeat(beat)) continue;
            if (RivalBeatTriggers.AreaFor(beat, Region.Graph, Region.Identity) != areaId) continue;

            var roster = Region.Rivals.ForPlayerCorner(cornerIndex).TeamAt(beat).Members
                .Select(member => new TrainerRosterMember(member.SpeciesId, member.Level))
                .ToArray();
            return new RivalBattleTransitionRequest(beat, cornerIndex, returnState, roster);
        }
        return null;
    }

    public void EnableDebugUnlock() => Interactions.EnableDebugUnlock();

    public IReadOnlyList<int> FlyTargets()
        => Map.FlyTargets(Region, _hasFly, Badges, DebugUnlock);

    public bool TryFlyTo(int areaId)
    {
        if (!Map.CanFlyTo(Region, areaId, _hasFly, Badges, DebugUnlock)) return false;
        VisitArea(areaId);
        return true;
    }

    public WhiteoutResult ApplyWhiteout()
    {
        var result = WhiteoutResolver.Resolve(Region, CurrentAreaId, LastPokemonCenterAreaId);
        VisitArea(result.DestinationAreaId);
        return result;
    }

    private bool IsPokemonCenter(int areaId)
        => Region.Graph.Areas.Any(area => area.Id == areaId && WhiteoutResolver.IsPokemonCenter(area));
}
