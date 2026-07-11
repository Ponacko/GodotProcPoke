using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Data.Tests;

public class PokeTypeTests
{
    [Fact]
    public void TypeChartHasExactlySeventeenTypes()
    {
        Assert.Equal(17, Enum.GetValues<PokeType>().Length);
    }

    [Fact]
    public void FairyDoesNotExist()
    {
        Assert.DoesNotContain("Fairy", Enum.GetNames<PokeType>());
    }
}
