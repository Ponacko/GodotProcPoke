using ProcPoke.Data;

namespace ProcPoke.Generation.Bosses;

/// <summary>A species and level in a boss roster. Movesets are deferred beyond Phase 2.</summary>
public sealed record BossMember(int SpeciesId, int Level);

/// <summary>One gym leader, Elite Four member, or Champion team.</summary>
public sealed record BossTeam(
    string Name,
    PokeType? AssignedType,
    IReadOnlyList<BossMember> Members);

/// <summary>All boss teams generated for a region. The Champion property is intentionally replaceable by 7c.</summary>
public sealed record BossPlan
{
    public required IReadOnlyDictionary<int, BossTeam> GymLeaders { get; init; }
    public required IReadOnlyList<BossTeam> EliteFour { get; init; }
    public required BossTeam Champion { get; init; }
}
