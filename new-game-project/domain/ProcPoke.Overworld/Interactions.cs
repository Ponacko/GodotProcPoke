using ProcPoke.Generation;
using ProcPoke.Generation.Bosses;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Npcs;
using ProcPoke.Generation.Topology;
using ProcPoke.Generation.Trainers;

namespace ProcPoke.Overworld;

public enum InteractionKind
{
    Npc,
    Sign,
    Trainer,
    Item,
    Gate,
    Shop,
}

/// <summary>A stable, map-local interaction assembled from generation output.</summary>
public sealed record InteractionRegistration
{
    public required string Id { get; init; }
    public required int AreaId { get; init; }
    public required GridPosition Position { get; init; }
    public required InteractionKind Kind { get; init; }
    public required string Text { get; init; }
    public int? ItemId { get; init; }
    public int? GateId { get; init; }
    public string? KeyName { get; init; }
    /// <summary>Badge required for this gate's HM field use; null for item-keys and non-gate interactions.</summary>
    public int? BadgePrerequisite { get; init; }
    public ObstacleClass? Obstacle { get; init; }
    public string? TrainerClass { get; init; }
    public TrainerEncounter? Trainer { get; init; }
    /// <summary>A generated gym, Elite Four, or Champion roster attached to this post.</summary>
    public BossTeam? Boss { get; init; }
    /// <summary>Zero-based League stage; four Elite Four stages followed by the Champion.</summary>
    public int? LeagueStage { get; init; }
    /// <summary>Gym leaders retain this named field for compatibility with gym interior bindings.</summary>
    public BossTeam? GymLeader { get; init; }
    public bool StartsOnSight { get; init; } = true;
}

public enum InteractionOutcomeKind
{
    Dialogue,
    TrainerPrompt,
    ItemCollected,
    AlreadyCollected,
    GateBlocked,
    GateUnlocked,
}

public sealed record InteractionOutcome(
    InteractionOutcomeKind Kind,
    InteractionRegistration Interaction,
    string Message,
    int? ItemId = null,
    int? GateId = null);

/// <summary>
/// Materializes all interactive tiles without changing generated maps. NPC posts are paired with their
/// graph-level dialogue in row-major tile order because the placement pass intentionally does not store a
/// random position in the plan.
/// </summary>
public sealed class InteractionRegistry
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<InteractionRegistration>> _byArea;
    private readonly IReadOnlyDictionary<(int AreaId, GridPosition Position), InteractionRegistration> _byPosition;

    private InteractionRegistry(
        IReadOnlyDictionary<int, IReadOnlyList<InteractionRegistration>> byArea,
        IReadOnlyDictionary<(int AreaId, GridPosition Position), InteractionRegistration> byPosition)
    {
        _byArea = byArea;
        _byPosition = byPosition;
    }

    public IReadOnlyList<InteractionRegistration> Of(int areaId)
        => _byArea.TryGetValue(areaId, out var interactions) ? interactions : [];

    public InteractionRegistration? At(int areaId, GridPosition position)
        => _byPosition.TryGetValue((areaId, position), out var interaction) ? interaction : null;

    public IEnumerable<InteractionRegistration> GatesOf(int areaId)
        => Of(areaId).Where(interaction => interaction.Kind == InteractionKind.Gate);

    public IReadOnlySet<GridPosition> ClearedGatePositions(
        int areaId, IReadOnlySet<int> clearedGateIds)
        => GatesOf(areaId)
            .Where(interaction => interaction.GateId is not null
                && clearedGateIds.Contains(interaction.GateId.Value))
            .Select(interaction => interaction.Position)
            .ToHashSet();

    /// <summary>The stable id a generated trainer's post gets, wherever it's carved. Shared with
    /// <see cref="OverworldSession"/> so it can look up a specific trainer (e.g. a hideout boss) without
    /// re-deriving this format.</summary>
    public static string TrainerId(int areaId, (int X, int Y) position)
        => $"trainer:{areaId}:{position.X}:{position.Y}";

    public static InteractionRegistry Build(GeneratedRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);

        var byArea = region.Graph.Areas.ToDictionary(
            area => area.Id, _ => (IReadOnlyList<InteractionRegistration>)[]);
        var all = new Dictionary<(int AreaId, GridPosition Position), InteractionRegistration>();

        foreach (var area in region.Graph.Areas.OrderBy(area => area.Id))
        {
            var interactions = new List<InteractionRegistration>();
            var carved = region.Carved[area.Id];
            var npcPositions = TileGridQuery.PositionsOf(carved.Grid, LogicalTile.NpcPost);
            var npcPosts = region.Npcs.Of(area.Id);
            if (npcPositions.Count != npcPosts.Count)
                throw new InvalidOperationException(
                    $"area {area.Id} has {npcPositions.Count} NPC tiles but {npcPosts.Count} planned posts");

            for (var i = 0; i < npcPosts.Count; i++)
            {
                var post = npcPosts[i];
                var position = npcPositions[i];
                Add(interactions, all, new InteractionRegistration
                {
                    Id = $"npc:{area.Id}:{position.X}:{position.Y}",
                    AreaId = area.Id,
                    Position = position,
                    Kind = post.Kind == NpcKind.Sign ? InteractionKind.Sign : InteractionKind.Npc,
                    Text = post.Text,
                });
            }

            // League posts are reserved for the generated Elite Four and Champion below; ordinary
            // population must not claim those same tiles as generic trainers.
            foreach (var trainer in region.Trainers.TrainersOf(area.Id).Where(_ => area.Archetype != AreaArchetype.League))
            {
                if (trainer.Position is not { } position) continue;
                var gridPosition = new GridPosition(position.X, position.Y);
                RequireTile(carved, gridPosition, LogicalTile.TrainerPost, "trainer");
                Add(interactions, all, new InteractionRegistration
                {
                    Id = TrainerId(area.Id, position),
                    AreaId = area.Id,
                    Position = gridPosition,
                    Kind = InteractionKind.Trainer,
                    Text = $"{trainer.DisplayClass} challenges you.",
                    TrainerClass = trainer.DisplayClass,
                    Trainer = trainer,
                });
            }

            if (area.Archetype == AreaArchetype.League)
            {
                var posts = TileGridQuery.PositionsOf(carved.Grid, LogicalTile.TrainerPost);
                var bosses = region.Bosses.EliteFour.Append(region.Bosses.Champion).ToArray();
                if (posts.Count != bosses.Length)
                    throw new InvalidOperationException(
                        $"League area {area.Id} has {posts.Count} trainer posts but {bosses.Length} boss teams");

                for (var i = 0; i < bosses.Length; i++)
                {
                    var isChampion = i == region.Bosses.EliteFour.Count;
                    var boss = bosses[i];
                    Add(interactions, all, new InteractionRegistration
                    {
                        Id = $"league:{area.Id}:{(isChampion ? "champion" : $"elite-four:{i}")}",
                        AreaId = area.Id,
                        Position = posts[i],
                        Kind = InteractionKind.Trainer,
                        Text = isChampion ? "The Champion awaits your challenge." : $"{boss.Name} challenges you.",
                        TrainerClass = isChampion ? "Champion" : "Elite Four",
                        Boss = boss,
                        LeagueStage = i,
                        StartsOnSight = !isChampion,
                    });
                }
            }

            foreach (var item in region.Trainers.ItemsOf(area.Id))
            {
                var position = new GridPosition(item.Position.X, item.Position.Y);
                RequireTile(carved, position, LogicalTile.ItemBall, "item ball");
                Add(interactions, all, new InteractionRegistration
                {
                    Id = $"item:{area.Id}:{position.X}:{position.Y}",
                    AreaId = area.Id,
                    Position = position,
                    Kind = InteractionKind.Item,
                    Text = $"You found item {item.ItemId}.",
                    ItemId = item.ItemId,
                });
            }

            byArea[area.Id] = interactions;
        }

        foreach (var gate in region.Gating.Gates.OrderBy(gate => gate.Id))
        {
            var area = region.Graph.Areas.FirstOrDefault(candidate =>
                candidate.OnCriticalPath && candidate.PathIndex == gate.BlockPathIndex)
                ?? throw new InvalidOperationException(
                    $"gate {gate.Id} has no critical-path area at index {gate.BlockPathIndex}");
            var carved = region.Carved[area.Id];
            if (carved.GateTiles.Count == 0)
                throw new InvalidOperationException($"gate {gate.Id} has no realized tiles in area {area.Id}");

            foreach (var (x, y) in carved.GateTiles.OrderBy(position => position.Y).ThenBy(position => position.X))
            {
                var position = new GridPosition(x, y);
                var registration = new InteractionRegistration
                {
                    Id = $"gate:{gate.Id}:{area.Id}:{x}:{y}",
                    AreaId = area.Id,
                    Position = position,
                    Kind = InteractionKind.Gate,
                    Text = $"The way is blocked by {gate.Obstacle}. It requires {gate.KeyName}.",
                    GateId = gate.Id,
                    KeyName = gate.KeyName,
                    Obstacle = gate.Obstacle,
                    BadgePrerequisite = gate.BadgePrerequisite,
                };
                var areaInteractions = byArea[area.Id].ToList();
                Add(areaInteractions, all, registration);
                byArea[area.Id] = areaInteractions;
            }
        }

        return new InteractionRegistry(byArea, all);
    }

    private static void RequireTile(CarvedArea area, GridPosition position, LogicalTile expected, string label)
    {
        if (!area.Grid.InBounds(position.X, position.Y) || area.Grid[position.X, position.Y] != expected)
            throw new InvalidOperationException(
                $"area {area.AreaId} {label} at ({position.X},{position.Y}) is not a {expected} tile");
    }

    private static void Add(
        ICollection<InteractionRegistration> areaInteractions,
        IDictionary<(int AreaId, GridPosition Position), InteractionRegistration> all,
        InteractionRegistration interaction)
    {
        if (!all.TryAdd((interaction.AreaId, interaction.Position), interaction))
            throw new InvalidOperationException(
                $"area {interaction.AreaId} has overlapping interactions at {interaction.Position}");
        areaInteractions.Add(interaction);
    }
}

/// <summary>Session-owned interaction state. Unlocks live here, never in the generated tile grid.</summary>
public sealed class InteractionSession
{
    private readonly HashSet<string> _collectedItems = new(StringComparer.Ordinal);
    private readonly HashSet<int> _unlockedGates = [];
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public Bag Bag { get; } = new();
    public bool DebugUnlock { get; private set; }
    public bool HasFlash => _keys.Contains("Flash") || DebugUnlock;

    public void EnableDebugUnlock() => DebugUnlock = true;

    public void GrantKey(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        _keys.Add(keyName);
    }

    public bool HasKey(string keyName) => DebugUnlock || _keys.Contains(keyName);

    public bool IsGateUnlocked(int gateId) => DebugUnlock || _unlockedGates.Contains(gateId);

    public VisionState VisionFor(bool darkArea, int radius = 3)
        => DarkCaveVision.For(darkArea, HasFlash, DebugUnlock, radius);

    public MovementCapabilities ForMovement(
        int areaId, MovementCapabilities capabilities, InteractionRegistry registry, GatingPlan gating, int badges)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(gating);
        var cleared = capabilities.ClearedGatePositions.ToHashSet();
        cleared.UnionWith(registry.ClearedGatePositions(areaId, _unlockedGates));
        return capabilities with
        {
            DebugUnlock = capabilities.DebugUnlock || DebugUnlock,
            ClearedGatePositions = cleared,
            HasSurf = capabilities.HasSurf || FieldMoveReady(gating, "Surf", badges),
            HasStrength = capabilities.HasStrength || FieldMoveReady(gating, "Strength", badges),
            HasCut = capabilities.HasCut || FieldMoveReady(gating, "Cut", badges),
            CanBicycle = capabilities.CanBicycle || HasKey("Bicycle"),
        };
    }

    /// <summary>An HM's field use additionally needs its gate's badge prerequisite (§4.3); item-keys never
    /// gate this way, so a missing gate (or one with no prerequisite) requires only the key itself.</summary>
    private bool FieldMoveReady(GatingPlan gating, string keyName, int badges)
    {
        if (!HasKey(keyName)) return false;
        var required = gating.Gates.FirstOrDefault(gate => gate.KeyName == keyName)?.BadgePrerequisite;
        return BadgeRequirementMet(required, badges);
    }

    private bool BadgeRequirementMet(int? required, int badges) => DebugUnlock || badges >= (required ?? 0);

    public InteractionOutcome Interact(InteractionRegistration interaction, int badges = 0)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        return interaction.Kind switch
        {
            InteractionKind.Npc or InteractionKind.Sign or InteractionKind.Shop => new(
                InteractionOutcomeKind.Dialogue, interaction, interaction.Text),
            InteractionKind.Trainer => new(
                InteractionOutcomeKind.TrainerPrompt, interaction, interaction.Text),
            InteractionKind.Item => CollectItem(interaction),
            InteractionKind.Gate => ResolveGate(interaction, badges),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private InteractionOutcome CollectItem(InteractionRegistration interaction)
    {
        if (!_collectedItems.Add(interaction.Id))
            return new(InteractionOutcomeKind.AlreadyCollected, interaction, "You already collected this item.",
                interaction.ItemId);
        if (interaction.ItemId is int itemId) Bag.Add(itemId);
        return new(InteractionOutcomeKind.ItemCollected, interaction, interaction.Text, interaction.ItemId);
    }

    private InteractionOutcome ResolveGate(InteractionRegistration interaction, int badges)
    {
        var gateId = interaction.GateId
            ?? throw new InvalidOperationException($"gate interaction {interaction.Id} has no gate id");
        var hasKey = HasKey(interaction.KeyName ?? string.Empty);
        var badgeMet = BadgeRequirementMet(interaction.BadgePrerequisite, badges);
        if (!hasKey || !badgeMet)
            return new(InteractionOutcomeKind.GateBlocked, interaction, interaction.Text, GateId: gateId);

        _unlockedGates.Add(gateId);
        return new(InteractionOutcomeKind.GateUnlocked, interaction,
            $"You cleared the way with {interaction.KeyName}.", GateId: gateId);
    }
}

public readonly record struct VisionState(bool IsDark, int Radius);

/// <summary>Pure dark-area visibility; rendering can consume this without owning gameplay state.</summary>
public static class DarkCaveVision
{
    public static VisionState For(bool darkArea, bool hasFlash, bool debugUnlock, int radius = 3)
        => darkArea && !hasFlash && !debugUnlock
            ? new VisionState(true, Math.Max(0, radius))
            : new VisionState(false, int.MaxValue);

    public static bool IsVisible(VisionState state, GridPosition player, GridPosition tile)
        => !state.IsDark
            || Math.Abs(player.X - tile.X) + Math.Abs(player.Y - tile.Y) <= state.Radius;
}
