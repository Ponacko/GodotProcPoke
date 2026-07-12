using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Data.Tests;

/// <summary>
/// Loads the committed <c>data/</c> once per test run. Locating it the same way the bake tool does
/// (walk up to the repo root) keeps the tests runnable from any working directory.
/// </summary>
public sealed class BakedDataFixture
{
    public GameData Data { get; }
    public string DataDir { get; }

    public BakedDataFixture()
    {
        DataDir = Path.Combine(FindRepoRoot(), "data");
        Data = GameDataLoader.Load(DataDir);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ProcPoke.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("could not locate repo root (ProcPoke.slnx) from test assembly.");
    }
}

[CollectionDefinition("baked-data")]
public sealed class BakedDataCollection : ICollectionFixture<BakedDataFixture>;
