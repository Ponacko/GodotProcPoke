namespace ProcPoke.Bake;

/// <summary>
/// A parsed veekun CSV table: a header row plus data rows, addressable by column name. Handles quoted
/// fields (embedded commas, quotes, newlines) per RFC 4180, which name tables need.
/// </summary>
public sealed class CsvTable
{
    private readonly Dictionary<string, int> _columns;
    public IReadOnlyList<string[]> Rows { get; }

    private CsvTable(Dictionary<string, int> columns, List<string[]> rows)
    {
        _columns = columns;
        Rows = rows;
    }

    /// <summary>Reads and parses a CSV file from disk (UTF-8, header row required).</summary>
    public static CsvTable Load(string path)
    {
        var records = Parse(File.ReadAllText(path));
        if (records.Count == 0)
            throw new InvalidDataException($"{path} is empty.");

        var header = records[0];
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++)
            columns[header[i]] = i;

        return new CsvTable(columns, records.GetRange(1, records.Count - 1));
    }

    /// <summary>Cell as a string; empty string for absent/blank cells.</summary>
    public string Str(string[] row, string column)
    {
        var i = _columns[column];
        return i < row.Length ? row[i] : string.Empty;
    }

    /// <summary>Cell as int; throws if blank (use <see cref="IntOrNull"/> for optional numbers).</summary>
    public int Int(string[] row, string column) => int.Parse(Str(row, column));

    /// <summary>Cell as int?, null when the cell is blank.</summary>
    public int? IntOrNull(string[] row, string column)
    {
        var s = Str(row, column);
        return string.IsNullOrEmpty(s) ? null : int.Parse(s);
    }

    /// <summary>Boolean stored as 0/1.</summary>
    public bool Bool(string[] row, string column) => Str(row, column) == "1";

    public bool HasColumn(string column) => _columns.ContainsKey(column);

    private static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var field = new System.Text.StringBuilder();
        var record = new List<string>();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        record.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        break; // fold CRLF -> LF
                    case '\n':
                        record.Add(field.ToString());
                        field.Clear();
                        rows.Add(record.ToArray());
                        record.Clear();
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }
        }

        // trailing record without a final newline
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            rows.Add(record.ToArray());
        }

        return rows;
    }
}
