namespace ProcPoke.Generation.Rng;

/// <summary>
/// The hierarchical named-stream factory (ADR-0005). Every pass — and every area within a pass — asks for
/// its own stream by name (<c>"topology"</c>, <c>"gates"</c>, <c>"carve/area-17"</c>); each is seeded by
/// hashing the master seed with that name. A draw-count change in one pass therefore perturbs nothing
/// else, and a single area can be regenerated in isolation for debugging.
/// </summary>
public sealed class RngStreams(ulong masterSeed)
{
    public ulong MasterSeed { get; } = masterSeed;

    /// <summary>Returns a fresh generator for the named stream. Same (master seed, name) ⇒ same sequence.</summary>
    public Pcg32 Stream(string name)
    {
        var seed = StableHash.Of(MasterSeed, name);
        // A second hash (of the reversed-role inputs) selects the PCG sequence, so two differently-named
        // streams that happen to collide on state still advance on independent sequences.
        var sequence = StableHash.Of(seed, name);
        return new Pcg32(seed, sequence);
    }

    /// <summary>Convenience for per-item streams, e.g. <c>Stream("carve", areaId)</c> → <c>"carve/17"</c>.</summary>
    public Pcg32 Stream(string name, int index) => Stream($"{name}/{index}");
}
