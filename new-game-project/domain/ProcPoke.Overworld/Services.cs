using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Services;

namespace ProcPoke.Overworld;

/// <summary>
/// Binds a generated <see cref="ServicePlacement"/> to the house interior it was seated in. A house with no
/// service registers nothing — its NPC post stays empty, exactly as it was before the services pass — so a
/// town's ordinary houses are unaffected.
/// </summary>
public static class ServiceInteractions
{
    public static IReadOnlyList<InteractionRegistration> Build(GeneratedRegion region, ActiveInterior interior)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(interior);
        if (interior.Template.Kind != InteriorKind.House) return [];

        var houseIndex = HouseIndexOf(region, interior);
        if (houseIndex < 0) return [];
        var services = region.Services.InHouse(interior.OverworldAreaId, houseIndex);
        if (services.Count == 0) return [];

        var posts = TileGridQuery.PositionsOf(interior.Template.Grid, LogicalTile.NpcPost);
        if (services.Count > posts.Count)
            throw new InvalidOperationException(
                $"house template has {posts.Count} NPC posts but {services.Count} services were seated in it");

        return services
            .Select(placement => new InteractionRegistration
            {
                Id = $"service:{interior.OverworldAreaId}:{houseIndex}:{placement.PostIndex}",
                AreaId = interior.OverworldAreaId,
                Position = posts[placement.PostIndex],
                Kind = InteractionKind.Service,
                Service = placement.Kind,
                Text = ServiceDialogue.Greeting(placement.Kind),
            })
            .ToArray();
    }

    /// <summary>Which of this town's houses the player walked into, in the same
    /// <see cref="CarvedArea.BuildingEntrances"/> order the services pass counted them.</summary>
    private static int HouseIndexOf(GeneratedRegion region, ActiveInterior interior)
        => region.Carved[interior.OverworldAreaId].BuildingEntrances
            .Where(entrance => entrance.Kind == BuildingKind.House)
            .ToList()
            .IndexOf(interior.Entrance);
}

public static class ServiceDialogue
{
    public static string Greeting(ServiceKind kind) => kind switch
    {
        ServiceKind.FossilRevival => "I can restore a fossil to life. Found any?",
        ServiceKind.MoveDeleter => "I can make a Pokémon forget a move — even one it can't normally lose.",
        ServiceKind.NameRater => "Let me see your Pokémon's name.",
        ServiceKind.MoveTutor => "I teach moves no Pokémon learns on its own.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

/// <summary>What a service did, as one line the overworld can print. <see cref="Changed"/> separates "the
/// service acted" from "it had nothing to do", so a caller can refresh party UI only when it must.</summary>
public sealed record ServiceOutcome(ServiceKind Kind, bool Changed, string Message);

/// <summary>
/// Nicknames are player-authored text, so they get the same normalization the player's own name does
/// (<see cref="NewGameIdentity"/>) rather than reaching the party as arbitrary input.
/// </summary>
public static class Nicknames
{
    public const int MaxLength = 12;

    public static bool TryNormalize(string? nickname, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(nickname)) return false;
        var trimmed = nickname.Trim();
        if (trimmed.Length > MaxLength || trimmed.Any(char.IsControl)) return false;
        normalized = trimmed;
        return true;
    }

    /// <summary>What to call a party member: its nickname if it has one, else its species name.</summary>
    public static string DisplayName(GameData data, BattleProgressionState member)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(member);
        return member.Nickname ?? data.Species[member.SpeciesId].Name;
    }
}

/// <summary>
/// The Move Deleter's rule (GDD §8a): any move can go, including a permanent field move, but never the
/// last one — a Pokémon with no moves cannot battle at all.
/// </summary>
public static class MoveDeleter
{
    public static bool CanDelete(BattleProgressionState member, int moveId)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.MoveIds.Count > 1 && member.MoveIds.Contains(moveId);
    }

    public static BattleProgressionState Delete(BattleProgressionState member, int moveId)
    {
        if (!CanDelete(member, moveId))
            throw new ArgumentException(
                $"move {moveId} cannot be deleted from this Pokémon", nameof(moveId));
        return member with { MoveIds = member.MoveIds.Where(id => id != moveId).ToArray() };
    }
}

/// <summary>
/// A move tutor teaches from the baked <see cref="LearnMethod.Tutor"/> learnset — the moves a species can
/// only ever get this way. Slot rules match level-up learning: four moves maximum, and a full moveset needs
/// the player to delete something first (the Move Deleter is the deliberate pairing).
/// </summary>
public static class MoveTutor
{
    public const int MoveSlotCount = 4;

    /// <summary>Every tutor move this Pokémon could still learn, strongest first — so a caller with no
    /// move-picking UI can take the head of the list and still teach something worth having.</summary>
    public static IReadOnlyList<int> OffersFor(GameData data, BattleProgressionState member)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(member);
        var known = member.MoveIds.ToHashSet();
        return data.Learnsets.GetValueOrDefault(member.SpeciesId, [])
            .Where(entry => entry.Method == LearnMethod.Tutor
                && !known.Contains(entry.MoveId)
                && data.Moves.ContainsKey(entry.MoveId))
            .Select(entry => entry.MoveId)
            .Distinct()
            .OrderByDescending(moveId => data.Moves[moveId].Power ?? 0)
            .ThenBy(moveId => moveId)
            .ToArray();
    }

    public static BattleProgressionState Teach(GameData data, BattleProgressionState member, int moveId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(member);
        if (!OffersFor(data, member).Contains(moveId))
            throw new ArgumentException($"no tutor teaches move {moveId} to this Pokémon", nameof(moveId));
        if (member.MoveIds.Count >= MoveSlotCount)
            throw new InvalidOperationException("this Pokémon has no free move slot");
        return member with { MoveIds = member.MoveIds.Append(moveId).ToArray() };
    }
}
