using System.Globalization;

namespace ProcPoke.Generation;

/// <summary>A decoded Seed String: the settings it names, and the generator version that produced it.</summary>
/// <param name="MatchesCurrentVersion">False when the string was made by another generator version.
/// Determinism is promised per version only (GDD §11), so the region may come out different — the caller
/// warns rather than refuses, because the settings themselves are still perfectly usable.</param>
public sealed record DecodedSeedString(
    GenerationSettings Settings, int GeneratorVersion, bool MatchesCurrentVersion);

/// <summary>
/// The shareable identity of a region (CONTEXT.md's Seed String): raw seed + generation settings +
/// generator version, encoded as one string. Save files never depend on it — they store the fully
/// generated region — so this is purely for sharing a region with another player.
/// </summary>
public static class SeedString
{
    private const string Prefix = "PP";
    private const int Digits = 36;
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// <c>PP&lt;version&gt;-&lt;seed base36&gt;-&lt;badges&gt;-&lt;dex&gt;-&lt;cap&gt;</c>. Base 36 keeps a
    /// 64-bit seed to 13 characters, and the leading version is what lets a decoder warn before generating.
    /// </summary>
    public static string Encode(GenerationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        return string.Join('-',
            $"{Prefix}{GeneratorVersion.Current}",
            Base36(settings.Seed),
            settings.BadgeCount.ToString(CultureInfo.InvariantCulture),
            settings.DexSize.ToString(CultureInfo.InvariantCulture),
            settings.RosterCap.ToString(CultureInfo.InvariantCulture));
    }

    public static DecodedSeedString Decode(string seedString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seedString);
        var parts = seedString.Trim().ToLowerInvariant().Split('-');
        if (parts.Length != 5)
            throw new FormatException($"'{seedString}' is not a Seed String (expected 5 dash-separated parts).");
        if (!parts[0].StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[0][Prefix.Length..], CultureInfo.InvariantCulture, out var version)
            || version < 1)
            throw new FormatException($"'{seedString}' has no generator version.");

        var settings = new GenerationSettings
        {
            Seed = FromBase36(parts[1]),
            BadgeCount = ParseInt(parts[2], "badge count"),
            DexSize = ParseInt(parts[3], "dex size"),
            RosterCap = ParseInt(parts[4], "roster cap"),
        };
        settings.Validate();
        return new DecodedSeedString(settings, version, version == GeneratorVersion.Current);
    }

    /// <summary>Whether a string can be decoded at all — for a Generation Screen's input validation, which
    /// wants to grey a button out rather than catch an exception per keystroke.</summary>
    public static bool TryDecode(string? seedString, out DecodedSeedString? decoded)
    {
        decoded = null;
        if (string.IsNullOrWhiteSpace(seedString)) return false;
        try
        {
            decoded = Decode(seedString);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static int ParseInt(string text, string label)
        => int.TryParse(text, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"'{text}' is not a valid {label}.");

    private static string Base36(ulong value)
    {
        if (value == 0) return "0";
        var digits = new Stack<char>();
        while (value > 0)
        {
            digits.Push(Alphabet[(int)(value % Digits)]);
            value /= Digits;
        }
        return new string(digits.ToArray());
    }

    private static ulong FromBase36(string text)
    {
        if (text.Length == 0) throw new FormatException("a Seed String's seed cannot be empty.");
        var value = 0UL;
        foreach (var character in text)
        {
            var digit = Alphabet.IndexOf(character, StringComparison.Ordinal);
            if (digit < 0) throw new FormatException($"'{character}' is not a base-36 digit.");
            // Rejects a seed too long to be a real 64-bit value rather than wrapping into a different region.
            if (value > (ulong.MaxValue - (ulong)digit) / Digits)
                throw new FormatException("the seed in this Seed String does not fit in 64 bits.");
            value = value * Digits + (ulong)digit;
        }
        return value;
    }
}
