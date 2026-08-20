using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Overworld.Save;

/// <summary>
/// How a save file is written. Almost every generated type is a plain record and needs nothing here; the
/// converters below cover the four shapes System.Text.Json cannot round-trip on its own — the tile grid
/// (a class over a flat array), coordinate tuples (fields, not properties, so they would serialize as
/// <c>{}</c>), read-only sets of them (an interface it cannot construct), and the region graph (a class
/// that builds its own adjacency index).
/// </summary>
public static class SaveJson
{
    public static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions
        {
            // Enums by name: a save must survive someone adding a value in the middle of an enum, which
            // ordinal encoding would silently reinterpret.
            Converters =
            {
                new JsonStringEnumConverter(),
                new PointConverter(),
                new NullablePointConverter(),
                new PointSetConverter(),
                new PointListConverter(),
                new TileGridConverter(),
                new RegionGraphConverter(),
            },
            WriteIndented = false,
        };
        return options;
    }

    /// <summary><c>(int X, int Y)</c> as <c>[x, y]</c>. A save holds tens of thousands of these, so the
    /// short form is worth it.</summary>
    private sealed class PointConverter : JsonConverter<(int X, int Y)>
    {
        public override (int X, int Y) Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => ReadPoint(ref reader);

        public override void Write(
            Utf8JsonWriter writer, (int X, int Y) value, JsonSerializerOptions options)
            => WritePoint(writer, value);
    }

    private sealed class NullablePointConverter : JsonConverter<(int X, int Y)?>
    {
        public override (int X, int Y)? Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Null ? null : ReadPoint(ref reader);

        public override void Write(
            Utf8JsonWriter writer, (int X, int Y)? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue();
            else WritePoint(writer, value.Value);
        }
    }

    private sealed class PointSetConverter : JsonConverter<IReadOnlySet<(int X, int Y)>>
    {
        public override IReadOnlySet<(int X, int Y)> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var set = new HashSet<(int X, int Y)>();
            ExpectStartArray(ref reader);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) set.Add(ReadPoint(ref reader));
            return set;
        }

        public override void Write(
            Utf8JsonWriter writer, IReadOnlySet<(int X, int Y)> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            // Sorted, so the same region always produces byte-identical bytes — a save diff should reflect
            // play, not hash-set iteration order.
            foreach (var point in value.OrderBy(p => p.Y).ThenBy(p => p.X)) WritePoint(writer, point);
            writer.WriteEndArray();
        }
    }

    private sealed class PointListConverter : JsonConverter<IReadOnlyList<(int X, int Y)>>
    {
        public override IReadOnlyList<(int X, int Y)> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var list = new List<(int X, int Y)>();
            ExpectStartArray(ref reader);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) list.Add(ReadPoint(ref reader));
            return list;
        }

        public override void Write(
            Utf8JsonWriter writer, IReadOnlyList<(int X, int Y)> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var point in value) WritePoint(writer, point);
            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Width, height, and the tiles as one run-length-encoded string (<c>Ground:30;Wall:2;…</c>). A carved
    /// grid is mostly long runs of one tile and is by far the biggest thing in a save, so the encoding
    /// matters; tiles are named rather than numbered for the same reason enums are — a save has to survive
    /// someone adding a tile in the middle of the enum.
    /// </summary>
    private sealed class TileGridConverter : JsonConverter<TileGrid>
    {
        public override TileGrid Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var snapshot = JsonSerializer.Deserialize<GridDto>(ref reader, options)
                ?? throw new JsonException("a tile grid cannot be null.");
            var grid = new TileGrid(snapshot.W, snapshot.H);
            var index = 0;
            foreach (var run in snapshot.Tiles.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = run.Split(':');
                if (!Enum.TryParse<LogicalTile>(parts[0], out var tile))
                    throw new JsonException($"'{parts[0]}' is not a logical tile.");
                var count = parts.Length > 1
                    ? int.Parse(parts[1], CultureInfo.InvariantCulture)
                    : 1;
                for (var i = 0; i < count; i++, index++)
                {
                    if (index >= snapshot.W * snapshot.H)
                        throw new JsonException("a tile grid described more tiles than it has room for.");
                    grid[index % snapshot.W, index / snapshot.W] = tile;
                }
            }
            if (index != snapshot.W * snapshot.H)
                throw new JsonException(
                    $"a tile grid claimed {snapshot.W}x{snapshot.H} but described {index} tiles.");
            return grid;
        }

        public override void Write(Utf8JsonWriter writer, TileGrid value, JsonSerializerOptions options)
        {
            var runs = new List<string>();
            LogicalTile runTile = default;
            var runLength = 0;
            for (var y = 0; y < value.Height; y++)
            for (var x = 0; x < value.Width; x++)
            {
                var tile = value[x, y];
                if (runLength > 0 && tile == runTile) { runLength++; continue; }
                if (runLength > 0) runs.Add(Run(runTile, runLength));
                runTile = tile;
                runLength = 1;
            }
            if (runLength > 0) runs.Add(Run(runTile, runLength));

            JsonSerializer.Serialize(
                writer, new GridDto(value.Width, value.Height, string.Join(';', runs)), options);
        }

        private static string Run(LogicalTile tile, int length)
            => length == 1 ? tile.ToString() : $"{tile}:{length}";

        private sealed record GridDto(int W, int H, string Tiles);
    }

    /// <summary>The graph as its five constructor arguments; the adjacency index it keeps is rebuilt from
    /// them, never stored.</summary>
    private sealed class RegionGraphConverter : JsonConverter<RegionGraph>
    {
        public override RegionGraph Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var snapshot = JsonSerializer.Deserialize<GraphDto>(ref reader, options)
                ?? throw new JsonException("a region graph cannot be null.");
            return new RegionGraph(
                snapshot.Areas, snapshot.Connections,
                snapshot.StartAreaId, snapshot.LeagueAreaId, snapshot.BadgeCount);
        }

        public override void Write(Utf8JsonWriter writer, RegionGraph value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(
                writer,
                new GraphDto(
                    value.Areas, value.Connections, value.StartAreaId, value.LeagueAreaId, value.BadgeCount),
                options);

        private sealed record GraphDto(
            IReadOnlyList<Area> Areas,
            IReadOnlyList<Connection> Connections,
            int StartAreaId,
            int LeagueAreaId,
            int BadgeCount);
    }

    private static void ExpectStartArray(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"expected an array, found {reader.TokenType}.");
    }

    private static (int X, int Y) ReadPoint(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"expected a [x, y] point, found {reader.TokenType}.");
        reader.Read();
        var x = reader.GetInt32();
        reader.Read();
        var y = reader.GetInt32();
        reader.Read(); // EndArray
        return (x, y);
    }

    private static void WritePoint(Utf8JsonWriter writer, (int X, int Y) point)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(point.X);
        writer.WriteNumberValue(point.Y);
        writer.WriteEndArray();
    }
}
