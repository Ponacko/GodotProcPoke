using ProcPoke.Generation.Bosses;

namespace ProcPoke.Generation.Rivals;

/// <summary>The four points at which a rival team is encountered.</summary>
public enum RivalBeat
{
    PostStarter,
    EarlyRoute,
    Midpoint,
    PreLeague,
}

/// <summary>One rival team at one progression beat.</summary>
public sealed record RivalTeam(
    RivalBeat Beat,
    int AceLevel,
    int RivalStarterSpeciesId,
    IReadOnlyList<BossMember> Members);

/// <summary>The rival variant for one possible player starter choice.</summary>
public sealed record RivalVariant(
    int PlayerCornerIndex,
    int RivalCornerIndex,
    IReadOnlyList<RivalTeam> Beats,
    BossTeam? ChampionTeam)
{
    public RivalTeam TeamAt(RivalBeat beat)
        => Beats.Single(team => team.Beat == beat);

    // Friendly aliases keep the plan readable at call sites and in debug/test code.
    public IReadOnlyList<RivalTeam> Teams => Beats;
    public BossTeam? Champion => ChampionTeam;
}

/// <summary>All three rival teams, one for each possible player starter corner.</summary>
public sealed record RivalPlan
{
    public required IReadOnlyList<RivalVariant> Variants { get; init; }

    public RivalVariant ForPlayerCorner(int playerCornerIndex)
        => Variants.Single(variant => variant.PlayerCornerIndex == playerCornerIndex);

    public IReadOnlyDictionary<int, RivalVariant> ByPlayerCorner
        => Variants.ToDictionary(variant => variant.PlayerCornerIndex);
}
