namespace ProcPoke.Generation.Gating;

/// <summary>
/// A placed gate: an obstacle on a critical-path chokepoint, its key's location, and — for HMs — the
/// badge that must be earned before its field use (§4.3). Every gate is solvable by construction: its
/// key sits in an area reachable before the gate, and its badge is earnable before it.
/// </summary>
public sealed record Gate
{
    public required int Id { get; init; }
    public required ObstacleClass Obstacle { get; init; }
    public required string KeyName { get; init; }
    public required KeyKind KeyKind { get; init; }

    /// <summary>The gate sits on the spine edge between critical-path areas <c>BlockPathIndex</c> and the next.</summary>
    public required int BlockPathIndex { get; init; }
    public required double TimingFraction { get; init; }

    /// <summary>Area where the key is obtained — off-spine where possible (§4.3 rule 2), else a town NPC gift.</summary>
    public required int KeyAreaId { get; init; }

    /// <summary>Area (reachable before the gate) that hosts a Hint NPC naming the key's location (§4.3 rule 8).</summary>
    public required int HintAreaId { get; init; }

    /// <summary>Badge required for the HM's field use; null for item-keys, which carry no prerequisite.</summary>
    public int? BadgePrerequisite { get; init; }

    /// <summary>Biome Requirement emitted for a terrain-bound obstacle; null for portable obstacles.</summary>
    public string? TerrainTag { get; init; }

    /// <summary>Area the terrain requirement applies to (the far side of a terrain-bound gate); null if portable.</summary>
    public int? TerrainAreaId { get; init; }
}

/// <summary>Fly's placement: a travel utility, not a gate — off-spine, badge-gated, gating nothing (§4.3 rule 9).</summary>
public sealed record FlyPlacement(int AreaId, int BadgePrerequisite);

/// <summary>The output of the gating pass: the gates and the Fly placement for a region.</summary>
public sealed record GatingPlan
{
    public required IReadOnlyList<Gate> Gates { get; init; }
    public required FlyPlacement Fly { get; init; }

    /// <summary>Biome Requirements this gating implies, as (areaId, tag) pairs for the biome pass.</summary>
    public IEnumerable<(int AreaId, string Tag)> BiomeRequirements
        => Gates.Where(g => g.TerrainTag is not null && g.TerrainAreaId is not null)
                .Select(g => (g.TerrainAreaId!.Value, g.TerrainTag!));
}
