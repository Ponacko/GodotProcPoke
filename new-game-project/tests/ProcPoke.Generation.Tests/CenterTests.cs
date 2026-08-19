using ProcPoke.Generation.Carving;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class CenterTests
{
    [Fact]
    public void BuildBindsExactlyOneHealInteractionAtTheClerkPost()
    {
        var template = InteriorTemplateCatalog.For(BuildingKind.Center);

        var interactions = CenterInteractions.Build(areaId: 7, template);

        var interaction = Assert.Single(interactions);
        Assert.Equal(InteractionKind.Heal, interaction.Kind);
        Assert.Equal(7, interaction.AreaId);
        Assert.Equal(LogicalTile.NpcPost, template.Grid[interaction.Position.X, interaction.Position.Y]);
    }

    [Fact]
    public void BuildRejectsATemplateThatIsNotACenter()
    {
        var template = InteriorTemplateCatalog.For(BuildingKind.Mart);

        Assert.Throws<ArgumentException>(() => CenterInteractions.Build(areaId: 7, template));
    }
}
