using ProcPoke.Battle;
using ProcPoke.Data;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class PartyTests
{
    [Fact]
    public void StartsEmpty()
    {
        var party = new Party();

        Assert.Empty(party.Members);
    }

    [Fact]
    public void AddAppendsInOrder()
    {
        var party = new Party();

        party.Add(Member(1));
        party.Add(Member(2));

        Assert.Equal(2, party.Members.Count);
        Assert.Equal(1, party.Members[0].SpeciesId);
        Assert.Equal(2, party.Members[1].SpeciesId);
    }

    [Fact]
    public void AddRejectsASeventhMember()
    {
        var party = new Party();
        for (var i = 0; i < Party.MaxSize; i++) party.Add(Member(i + 1));

        Assert.Throws<InvalidOperationException>(() => party.Add(Member(100)));
        Assert.Equal(Party.MaxSize, party.Members.Count);
    }

    [Fact]
    public void ReplaceUpdatesTheMemberAtThatIndexOnly()
    {
        var party = new Party();
        party.Add(Member(1));
        party.Add(Member(2));

        party.Replace(1, Member(99));

        Assert.Equal(1, party.Members[0].SpeciesId);
        Assert.Equal(99, party.Members[1].SpeciesId);
    }

    [Fact]
    public void ReplaceRejectsAnOutOfRangeIndex()
    {
        var party = new Party();
        party.Add(Member(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => party.Replace(1, Member(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => party.Replace(-1, Member(2)));
    }

    private static BattleProgressionState Member(int speciesId) => new()
    {
        SpeciesId = speciesId,
        Level = 5,
        TotalExperience = 0,
        EffortValues = new StatBlock(0, 0, 0, 0, 0, 0),
        MoveIds = [1],
    };
}
