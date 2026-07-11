using ProcPoke.Battle;
using Xunit;

namespace ProcPoke.Battle.Tests;

public class BattleEngineInfoTests
{
    [Fact]
    public void CanonicalRulesetIsGen5()
    {
        Assert.Equal("Gen5-B2W2", BattleEngineInfo.CanonicalRuleset);
    }
}
