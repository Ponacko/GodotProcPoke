using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class BagTests
{
    [Fact]
    public void AddAccumulatesCountsPerItem()
    {
        var bag = new Bag();

        bag.Add(1);
        bag.Add(1, 2);
        bag.Add(2);

        Assert.Equal(3, bag.Count(1));
        Assert.Equal(1, bag.Count(2));
        Assert.True(bag.Has(1));
        Assert.False(bag.Has(3));
        Assert.Equal(0, bag.Count(3));
    }

    [Fact]
    public void RemoveFailsWithoutMutatingWhenCountIsInsufficient()
    {
        var bag = new Bag();
        bag.Add(1, 2);

        Assert.False(bag.Remove(1, 3));
        Assert.Equal(2, bag.Count(1));

        Assert.True(bag.Remove(1, 2));
        Assert.Equal(0, bag.Count(1));
        Assert.False(bag.Has(1));
    }

    [Fact]
    public void AddAndRemoveRejectNonPositiveCounts()
    {
        var bag = new Bag();
        Assert.Throws<ArgumentOutOfRangeException>(() => bag.Add(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => bag.Remove(1, 0));
    }
}
