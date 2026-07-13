namespace ProcPoke.Generation.Rng;

/// <summary>
/// A version-stable 64-bit hash (FNV-1a). Used to derive child RNG seeds from a master seed and a stream
/// name. Deliberately not <see cref="object.GetHashCode"/> — that is randomized per process and would
/// break the per-generator-version determinism promise (GDD §11, ADR-0005).
/// </summary>
internal static class StableHash
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>Hashes a master seed together with a stream name into a stable 64-bit value.</summary>
    public static ulong Of(ulong seed, string streamName)
    {
        var hash = FnvOffsetBasis;

        // Fold in the 8 bytes of the seed first, then the UTF-8 bytes of the name.
        for (var i = 0; i < 8; i++)
        {
            hash ^= (byte)(seed >> (i * 8));
            hash *= FnvPrime;
        }

        foreach (var b in System.Text.Encoding.UTF8.GetBytes(streamName))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return hash;
    }
}
