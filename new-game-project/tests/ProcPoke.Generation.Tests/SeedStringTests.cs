using ProcPoke.Generation;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// The Seed String (CONTEXT.md): a region's shareable identity. Determinism is promised per generator
/// version only, so the version travels inside the string and a decoder can say so before generating.
/// </summary>
public sealed class SeedStringTests
{
    [Theory]
    [InlineData(0UL, 1, 16, 1)]
    [InlineData(42UL, 8, 150, 5)]
    [InlineData(ulong.MaxValue, 16, 493, 5)]
    public void EverySettingSurvivesAnEncodeDecodeRoundTrip(ulong seed, int badges, int dex, int cap)
    {
        var settings = new GenerationSettings
        {
            Seed = seed, BadgeCount = badges, DexSize = dex, RosterCap = cap,
        };

        var decoded = SeedString.Decode(SeedString.Encode(settings));

        Assert.Equal(settings, decoded.Settings);
        Assert.Equal(GeneratorVersion.Current, decoded.GeneratorVersion);
        Assert.True(decoded.MatchesCurrentVersion);
    }

    [Fact]
    public void TheSameSettingsAlwaysEncodeToTheSameString()
    {
        var settings = new GenerationSettings { Seed = 4242, BadgeCount = 8 };

        Assert.Equal(SeedString.Encode(settings), SeedString.Encode(settings));
        Assert.NotEqual(
            SeedString.Encode(settings), SeedString.Encode(settings with { Seed = 4243 }));
        Assert.NotEqual(
            SeedString.Encode(settings), SeedString.Encode(settings with { BadgeCount = 4 }));
    }

    [Fact]
    public void AStringFromAnotherGeneratorVersionDecodesButSaysSo()
    {
        var settings = new GenerationSettings { Seed = 7, BadgeCount = 8 };
        var current = SeedString.Encode(settings);
        var older = current.Replace($"PP{GeneratorVersion.Current}", "PP1", StringComparison.Ordinal);
        Assert.NotEqual(current, older); // guard: this test means nothing once the version is 1 again

        var decoded = SeedString.Decode(older);

        // Usable settings, with the warning the Generation Screen shows — never a refusal.
        Assert.Equal(settings, decoded.Settings);
        Assert.Equal(1, decoded.GeneratorVersion);
        Assert.False(decoded.MatchesCurrentVersion);
    }

    [Fact]
    public void CaseAndSurroundingSpaceDoNotMatter()
    {
        var settings = new GenerationSettings { Seed = 123456789, BadgeCount = 12, RosterCap = 3 };
        var encoded = SeedString.Encode(settings);

        Assert.Equal(settings, SeedString.Decode($"  {encoded.ToUpperInvariant()}  ").Settings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("PP3-2a-8-150")]              // too few parts
    [InlineData("PP3-2a-8-150-5-9")]          // too many parts
    [InlineData("XX3-2a-8-150-5")]            // no PP prefix
    [InlineData("PP-2a-8-150-5")]             // no version
    [InlineData("PP3-!!-8-150-5")]            // seed is not base 36
    [InlineData("PP3-zzzzzzzzzzzzzzzz-8-150-5")] // seed overflows 64 bits
    [InlineData("PP3-2a-0-150-5")]            // badge count out of range
    [InlineData("PP3-2a-8-150-9")]            // roster cap out of range
    [InlineData("PP3-2a-8-4-5")]              // dex too small
    public void NonsenseIsRejectedRatherThanGuessedAt(string input)
    {
        Assert.False(SeedString.TryDecode(input, out var decoded));
        Assert.Null(decoded);
    }

    [Fact]
    public void TryDecodeReturnsTheSameResultAsDecodeWhenItSucceeds()
    {
        var encoded = SeedString.Encode(new GenerationSettings { Seed = 99, BadgeCount = 4 });

        Assert.True(SeedString.TryDecode(encoded, out var decoded));
        Assert.Equal(SeedString.Decode(encoded), decoded);
    }

    /// <summary>A shared string has to be typeable and quotable, which a 20-digit seed is not.</summary>
    [Fact]
    public void TheEncodingStaysShortEvenForTheLargestSeed()
    {
        var encoded = SeedString.Encode(
            new GenerationSettings { Seed = ulong.MaxValue, BadgeCount = 16, DexSize = 493 });

        Assert.True(encoded.Length <= 32, encoded);
        Assert.DoesNotContain(' ', encoded);
    }
}
