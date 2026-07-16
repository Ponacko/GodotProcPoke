using ProcPoke.Data;

namespace ProcPoke.Generation.Tests;

/// <summary>Loads the baked <see cref="GameData"/> once per test run, mirroring ProcPoke.Data.Tests'
/// BakedDataFixture — every test that calls <c>RegionGenerator.Generate</c> needs one.</summary>
internal static class TestData
{
    public static readonly GameData Data = GameDataLoader.Load(FindDataDir());

    private static string FindDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ProcPoke.slnx")))
                return Path.Combine(dir.FullName, "data");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("could not locate repo root (ProcPoke.slnx) from test assembly.");
    }
}
