using ProcPoke.Generation.Rng;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// The determinism guarantees the whole generator rests on (ADR-0005): same seed ⇒ same sequence, named
/// streams are independent, and the PRNG is unbiased.
/// </summary>
public class RngTests
{
    [Fact]
    public void SameSeedProducesSameSequence()
    {
        var a = new Pcg32(12345);
        var b = new Pcg32(12345);
        for (var i = 0; i < 1000; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void DifferentSeedsDiverge()
    {
        var a = new Pcg32(1);
        var b = new Pcg32(2);
        var same = 0;
        for (var i = 0; i < 1000; i++)
            if (a.NextUInt() == b.NextUInt()) same++;
        Assert.True(same < 5, $"sequences suspiciously similar ({same} collisions)");
    }

    [Fact]
    public void NextIntStaysInRange()
    {
        var rng = new Pcg32(99);
        for (var i = 0; i < 100_000; i++)
        {
            var v = rng.NextInt(10, 20);
            Assert.InRange(v, 10, 19);
        }
    }

    [Fact]
    public void NextIntIsReasonablyUniform()
    {
        var rng = new Pcg32(7);
        var buckets = new int[6];
        const int draws = 600_000;
        for (var i = 0; i < draws; i++)
            buckets[rng.NextInt(6)]++;

        // Each bucket should land near draws/6; allow ±5%.
        foreach (var b in buckets)
            Assert.InRange(b, draws / 6 * 0.95, draws / 6 * 1.05);
    }

    [Fact]
    public void ShuffleIsDeterministicAndAPermutation()
    {
        int[] Make() => Enumerable.Range(0, 50).ToArray();
        var x = Make();
        var y = Make();
        new Pcg32(555).Shuffle(x);
        new Pcg32(555).Shuffle(y);
        Assert.Equal(x, y);
        Assert.Equal(Enumerable.Range(0, 50), x.OrderBy(v => v));
    }

    [Fact]
    public void NamedStreamsAreReproducible()
    {
        var s1 = new RngStreams(0xABCDEF);
        var s2 = new RngStreams(0xABCDEF);
        Assert.Equal(s1.Stream("topology").NextUInt(), s2.Stream("topology").NextUInt());
        Assert.Equal(s1.Stream("carve", 17).NextUInt(), s2.Stream("carve", 17).NextUInt());
    }

    [Fact]
    public void NamedStreamsAreIndependent()
    {
        var s = new RngStreams(42);
        // Different names must give different sequences, or a change in one pass would echo in another.
        Assert.NotEqual(s.Stream("topology").NextUInt(), s.Stream("gates").NextUInt());
        Assert.NotEqual(s.Stream("carve", 1).NextUInt(), s.Stream("carve", 2).NextUInt());
    }

    [Fact]
    public void DifferentMasterSeedsGiveDifferentStreams()
    {
        var a = new RngStreams(1).Stream("topology").NextUInt();
        var b = new RngStreams(2).Stream("topology").NextUInt();
        Assert.NotEqual(a, b);
    }
}
