namespace ProcPoke.Generation.Rng;

/// <summary>
/// A small, fast, version-stable pseudo-random generator (PCG-XSH-RR 64/32, O'Neill 2014). Its algorithm
/// is fixed in our own code, so its sequence never changes across .NET runtime upgrades — the guarantee
/// <see cref="System.Random"/> cannot make, and the reason that type is banned from generation (ADR-0005).
/// </summary>
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _increment;

    /// <summary>
    /// Seeds the generator. <paramref name="sequence"/> selects an independent stream; two generators with
    /// the same state seed but different sequences never overlap.
    /// </summary>
    public Pcg32(ulong seed, ulong sequence = 0)
    {
        _increment = (sequence << 1) | 1UL;
        _state = 0UL;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    /// <summary>The next 32-bit value in the sequence.</summary>
    public uint NextUInt()
    {
        var old = _state;
        _state = old * Multiplier + _increment;
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
    }

    /// <summary>A uniform int in [minInclusive, maxExclusive), free of modulo bias.</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentException($"empty range [{minInclusive}, {maxExclusive}).");

        var range = (uint)(maxExclusive - minInclusive);
        // Rejection sampling: discard the biased tail so every value is equally likely.
        var limit = uint.MaxValue - (uint.MaxValue % range);
        uint value;
        do { value = NextUInt(); } while (value >= limit);
        return minInclusive + (int)(value % range);
    }

    /// <summary>A uniform int in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive) => NextInt(0, maxExclusive);

    /// <summary>An inclusive uniform int in [min, max].</summary>
    public int NextIntInclusive(int min, int max) => NextInt(min, max + 1);

    /// <summary>A double in [0, 1).</summary>
    public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

    /// <summary>True with probability <paramref name="probability"/> (clamped to [0, 1]).</summary>
    public bool Chance(double probability) => NextDouble() < probability;

    /// <summary>Uniformly picks one element.</summary>
    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0) throw new ArgumentException("cannot pick from an empty list.");
        return items[NextInt(items.Count)];
    }

    /// <summary>In-place Fisher–Yates shuffle.</summary>
    public void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
