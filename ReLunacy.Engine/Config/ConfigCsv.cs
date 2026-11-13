using System.Text;

namespace ReLunacy.Engine.Config;

/// <summary>
/// One of the games' configuration tables: /data/configs/*.csv in packed/game/global_cached.psarc (A Crack in
/// Time: 18, All 4 One: 13, Into the Nexus: 25). The game loads each as a Lua table of rows of strings
/// (CONFIG_TABLE) and the matching /data/configs/*.lua parses it - so the CSV is the data, read at boot,
/// with nothing to compile. They are a designer's spreadsheet export: ragged rows, sections opened by a name
/// in the first column (LEVEL_SPACE_SECTOR_1), values in the columns after, CRLF line ends, a few quoted cells.
///
/// The model keeps every cell's text and whether it was quoted, and the file's line ending, so a table that
/// was not edited writes back to exactly the bytes it was read from (checked over every shipped table), and
/// an edit changes only the cells it touches. Text is Latin-1: one char per byte, nothing re-encoded.
/// </summary>
public sealed class ConfigCsv
{
    public sealed class Cell(string text, bool quoted = false)
    {
        public string Text { get; set; } = text;

        /// <summary>Written between double quotes. Kept from the file, and forced when the text needs it.</summary>
        public bool Quoted { get; set; } = quoted;
    }

    public List<List<Cell>> Rows { get; } = [];

    /// <summary>What lines are joined with: the file's first line break.</summary>
    public string LineEnding { get; private set; } = "\r\n";

    /// <summary>Whether the last row is followed by a line break.</summary>
    public bool EndsWithLineBreak { get; private set; }

    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Count);

    public static ConfigCsv Parse(ReadOnlySpan<byte> bytes) => Parse(Encoding.Latin1.GetString(bytes));

    public static ConfigCsv Parse(string text)
    {
        var csv = new ConfigCsv();
        int lf = text.IndexOf('\n');
        csv.LineEnding = lf > 0 && text[lf - 1] == '\r' ? "\r\n" : lf >= 0 ? "\n" : "\r\n";
        csv.EndsWithLineBreak = text.EndsWith('\n');

        var row = new List<Cell>();
        var cell = new StringBuilder();
        bool quoted = false, inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c != '"') cell.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else inQuotes = false;
                continue;
            }
            switch (c)
            {
                case '"' when cell.Length == 0 && !quoted:
                    quoted = inQuotes = true;
                    break;
                case ',':
                    row.Add(new Cell(cell.ToString(), quoted));
                    cell.Clear();
                    quoted = false;
                    break;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    break;
                case '\n':
                    row.Add(new Cell(cell.ToString(), quoted));
                    csv.Rows.Add(row);
                    row = [];
                    cell.Clear();
                    quoted = false;
                    break;
                default:
                    cell.Append(c);
                    break;
            }
        }
        // The last line, when the file does not end with a break.
        if (!csv.EndsWithLineBreak && (cell.Length > 0 || row.Count > 0 || quoted))
        {
            row.Add(new Cell(cell.ToString(), quoted));
            csv.Rows.Add(row);
        }
        return csv;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        for (int r = 0; r < Rows.Count; r++)
        {
            if (r > 0) sb.Append(LineEnding);
            var row = Rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0) sb.Append(',');
                var cell = row[c];
                bool quote = cell.Quoted || cell.Text.IndexOfAny([',', '"', '\n', '\r']) >= 0;
                if (quote) sb.Append('"').Append(cell.Text.Replace("\"", "\"\"")).Append('"');
                else sb.Append(cell.Text);
            }
        }
        if (EndsWithLineBreak && Rows.Count > 0) sb.Append(LineEnding);
        return sb.ToString();
    }

    public byte[] ToBytes() => Encoding.Latin1.GetBytes(ToText());

    /// <summary>A cell's text, or "" past the end of its row.</summary>
    public string Get(int row, int column) =>
        row < Rows.Count && column < Rows[row].Count ? Rows[row][column].Text : "";

    /// <summary>Sets a cell, growing a short row with empty cells to reach it. Setting a cell past the end of
    /// its row to "" changes nothing.</summary>
    public void Set(int row, int column, string text)
    {
        var cells = Rows[row];
        if (column >= cells.Count)
        {
            if (text.Length == 0) return;
            while (cells.Count <= column) cells.Add(new Cell(""));
        }
        cells[column].Text = text;
    }

    /// <summary>
    /// The block a section name opens: the row whose first cell is <paramref name="name"/>, and every row after
    /// it up to the next row whose first cell is set (level.csv: LEVEL_SPACE_SECTOR_1 ... LEVEL_GALACTON_SHIP).
    /// Null when no row opens it.
    /// </summary>
    public (int Start, int End)? Section(string name)
    {
        int start = Rows.FindIndex(r => r.Count > 0 && string.Equals(r[0].Text, name, StringComparison.OrdinalIgnoreCase));
        if (start < 0) return null;
        int end = start + 1;
        while (end < Rows.Count && (Rows[end].Count == 0 || Rows[end][0].Text.Length == 0)) end++;
        return (start, end);
    }
}
