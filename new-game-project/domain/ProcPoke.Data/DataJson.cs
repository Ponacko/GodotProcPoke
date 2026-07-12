using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcPoke.Data;

/// <summary>
/// The one JSON configuration shared by the bake tool (writer) and the loader (reader), so committed
/// <c>data/</c> round-trips exactly. Indented + string enums keep the files diff-reviewable like code.
/// </summary>
public static class DataJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Serializes with <see cref="Options"/> and normalizes line endings to '\n', so a bake produces
    /// byte-identical files regardless of host OS (the reproducibility promise for committed data).
    /// </summary>
    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, Options).Replace("\r\n", "\n");
}
