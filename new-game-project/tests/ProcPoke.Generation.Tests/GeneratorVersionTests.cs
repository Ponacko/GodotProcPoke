using ProcPoke.Generation;
using Xunit;

namespace ProcPoke.Generation.Tests;

public class GeneratorVersionTests
{
    [Fact]
    public void VersionIsPositive()
    {
        Assert.True(GeneratorVersion.Current >= 1);
    }
}
