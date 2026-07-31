using System.Text;
using ProcPoke.Generation.Carving;

namespace ProcPoke.Generation.Debug;

/// <summary>Renders a carved tile grid as ASCII — the per-area review artifact until the PNG renderer lands.</summary>
public static class AsciiRenderer
{
    private static char Glyph(LogicalTile t) => t switch
    {
        LogicalTile.Ground => '.',
        LogicalTile.Wall => '#',
        LogicalTile.Tree => 'T',
        LogicalTile.TallGrass => ',',
        LogicalTile.Ledge => '=',
        LogicalTile.Water => '~',
        LogicalTile.Sand => ':',
        LogicalTile.Boulder => 'O',
        LogicalTile.CutTree => 'C',
        LogicalTile.GateObstacle => 'X',
        LogicalTile.Warp => '>',
        LogicalTile.TrainerPost => 'P',
        LogicalTile.ItemBall => 'i',
        LogicalTile.NpcPost => 'n',
        _ => '?',
    };

    public static string Render(CarvedArea area)
    {
        var g = area.Grid;
        var sb = new StringBuilder();
        for (var y = 0; y < g.Height; y++)
        {
            for (var x = 0; x < g.Width; x++)
                sb.Append(Glyph(g[x, y]));
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
