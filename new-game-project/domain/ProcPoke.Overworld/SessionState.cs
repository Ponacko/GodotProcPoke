using ProcPoke.Generation;
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

    public OverworldSession(GeneratedRegion region, PlayerIdentity player)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(player);
        Region = region;
        Player = player;
        CurrentAreaId = region.Graph.StartAreaId;
        Map = new RegionMapState(CurrentAreaId);
        Time = TimeOfDayClock.FromMinutes(12 * 60);
        Interactions = new InteractionSession();
    }

    public GeneratedRegion Region { get; }
    public PlayerIdentity Player { get; }
    public InteractionSession Interactions { get; }
    public RegionMapState Map { get; }
    public TimeOfDayState Time { get; private set; }
    public int CurrentAreaId { get; private set; }
    public int Badges { get; private set; }
    public bool HasFly => _hasFly || Interactions.DebugUnlock;
    public bool DebugUnlock => Interactions.DebugUnlock;

    public void VisitArea(int areaId)
    {
        if (!Region.Graph.Areas.Any(area => area.Id == areaId))
            throw new ArgumentOutOfRangeException(nameof(areaId), areaId, "area is not in this region");
        CurrentAreaId = areaId;
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

    public void EnableDebugUnlock() => Interactions.EnableDebugUnlock();

    public IReadOnlyList<int> FlyTargets()
        => Map.FlyTargets(Region, _hasFly, Badges, DebugUnlock);

    public bool TryFlyTo(int areaId)
    {
        if (!Map.CanFlyTo(Region, areaId, _hasFly, Badges, DebugUnlock)) return false;
        CurrentAreaId = areaId;
        return true;
    }
}
