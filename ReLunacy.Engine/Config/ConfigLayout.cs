using System.Globalization;
using System.Text.RegularExpressions;

namespace ReLunacy.Engine.Config;

/// <summary>What a field's values look like, from every value it holds across the table.</summary>
public enum ConfigValueKind
{
    /// <summary>No value anywhere.</summary>
    Empty,
    /// <summary>Every value is a number.</summary>
    Number,
    /// <summary>Every value is a constant name (SEG_MOON, WPN_ZURKON, LEVEL_TOMBLI, GEN_KEY_WEAP_...): the
    /// values seen are offered as choices.</summary>
    Name,
    Text,
}

/// <summary>A section of a sectioned table: a name alone in the first column, then its field rows.</summary>
public sealed class ConfigSection
{
    public required string Name { get; init; }

    /// <summary>The row naming it.</summary>
    public required int Row { get; init; }

    /// <summary>One past its last row.</summary>
    public required int End { get; init; }

    /// <summary>Column names this section gives its own value columns, over the table's: level.csv's Pod row
    /// (column 3 on: SEGMENT_SS1_SPACE, SEGMENT_SS1_MOON1A...).</summary>
    public Dictionary<int, string> ColumnNames { get; } = [];
}

/// <summary>
/// The structure of a configuration table (<see cref="ConfigCsv"/>), read from its rows. The tables are exports
/// of one spreadsheet template, in two shapes:
///
///  - The template's preamble: "Notes: (Stay within yellow box...)", "CTRL-E Export", "View help on editing this
///    page at:", "RCF2 Config Reference Wiki". Not data.
///  - Header rows naming the columns, up to the first section: one ("ArmorType,DamageMult,NameTag,DescTag",
///    ",Variable:,Comments:,Value:") or two (enemy.csv's ",,,V1,V2,...", weapon.csv's per-level "1..10",
///    hazard.csv's "LEVEL,,,LEVEL_NONE,LEVEL_GREAT_CLOCK_E,..."); the deeper row names the columns it fills.
///  - Sectioned (level, enemy, weapon, char, space, radar, skillpoint, hazard, fixtimegame, upgrades): a name alone in
///    column 0 opens a section, then ",Field,Comment,values..." rows. level.csv names its value columns per level
///    with the section's Pod row; its ",,Totals:" row only marks where the per-segment rows begin.
///  - Records (armor, custom, vendor, zoni, hero, difficulty, arena): one row per entry under the header (arena's
///    entries are followed by ",time,125"-style rows of their own).
///
/// A field's kind is judged from every value it holds anywhere in the table (a field is a column 1 name in a
/// sectioned table, a column in a record table), so a typo shows against the values the game's other rows use.
/// </summary>
public sealed class ConfigLayout
{
    private static readonly string[] PreambleMarks =
        ["yellow box", "CTRL-E Export", "View help on editing", "RCF2 Config Reference Wiki"];

    private static readonly Regex NameValue = new(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

    /// <summary>Rows before this are the template's preamble.</summary>
    public int PreambleEnd { get; private set; }

    public List<int> HeaderRows { get; } = [];

    /// <summary>The table's name for each column, from its header rows ("" where none).</summary>
    public string[] ColumnNames { get; private set; } = [];

    public bool Sectioned => Sections.Count > 0;

    public List<ConfigSection> Sections { get; } = [];

    /// <summary>A record table's data rows, or a sectioned table's rows that precede its first section.</summary>
    public List<int> RecordRows { get; } = [];

    /// <summary>The column holding a field's name: 1 in a sectioned table.</summary>
    public int FieldColumn => Sectioned ? 1 : -1;

    /// <summary>The first value column of a sectioned table: after the field name and, when the header has one,
    /// the comments column ("Comments:" / "Comment").</summary>
    public int FirstValueColumn { get; private set; } = 2;

    private readonly Dictionary<string, (ConfigValueKind Kind, List<string> Choices)> _fields = new(StringComparer.Ordinal);

    public static ConfigLayout Of(ConfigCsv table)
    {
        var layout = new ConfigLayout();
        layout.Read(table);
        return layout;
    }

    private static bool IsBlank(List<ConfigCsv.Cell> row) => row.All(c => c.Text.Trim().Length == 0);

    /// <summary>A name alone in the first column (a comment beside it allowed), followed - past blank rows - by a
    /// field row: first column empty, a name in the second. A list of names (scaleformstats.csv) or numbers above
    /// a header (Into the Nexus' weapon.csv: 600, 2500, 4200, 9700) opens nothing.</summary>
    private static bool OpensSection(ConfigCsv table, int r)
    {
        var row = table.Rows[r];
        if (row.Count == 0 || row[0].Text.Length == 0) return false;
        if (!row.Skip(1).All(c => c.Text.Length == 0 || IsAnnotation(row, c))) return false;
        for (int next = r + 1; next < table.Rows.Count; next++)
        {
            if (IsBlank(table.Rows[next])) continue;
            return table.Get(next, 0).Length == 0 && table.Get(next, 1).Length > 0;
        }
        return false;
    }

    /// <summary>weapon.csv's "WPN_WRENCH,,Given at game start": a comment beside a section name.</summary>
    private static bool IsAnnotation(List<ConfigCsv.Cell> row, ConfigCsv.Cell cell) => row.IndexOf(cell) == 2;

    private void Read(ConfigCsv table)
    {
        int rows = table.Rows.Count;

        // The preamble: up to the last template line among the first few rows.
        for (int r = 0; r < Math.Min(rows, 8); r++)
        {
            string line = string.Join(",", table.Rows[r].Select(c => c.Text));
            if (PreambleMarks.Any(m => line.Contains(m, StringComparison.Ordinal))) PreambleEnd = r + 1;
        }
        int start = PreambleEnd;
        while (start < rows && IsBlank(table.Rows[start])) start++;

        int firstSection = -1;
        for (int r = start; r < rows; r++)
            if (OpensSection(table, r)) { firstSection = r; break; }

        // Header rows: everything between the preamble and the first section; a record table's is its first row.
        int headerEnd = firstSection >= 0 ? firstSection : Math.Min(rows, start + 1);
        for (int r = start; r < headerEnd; r++) if (!IsBlank(table.Rows[r])) HeaderRows.Add(r);

        int width = table.ColumnCount;
        ColumnNames = new string[width];
        for (int c = 0; c < width; c++)
        {
            string name = "";
            foreach (int r in HeaderRows)
                if (table.Get(r, c).Trim() is { Length: > 0 } text) name = text.TrimEnd(':');
            ColumnNames[c] = name;
        }

        if (firstSection >= 0)
        {
            if (ColumnNames.Length > 2 && ColumnNames[2].StartsWith("Comment", StringComparison.OrdinalIgnoreCase)) FirstValueColumn = 3;
            int r = firstSection;
            while (r < rows)
            {
                if (!OpensSection(table, r)) { RecordRows.Add(r); r++; continue; }
                int end = r + 1;
                while (end < rows && !OpensSection(table, end) && !(table.Rows[end].Count > 0 && table.Rows[end][0].Text.Length > 0)) end++;
                var section = new ConfigSection { Name = table.Get(r, 0), Row = r, End = end };
                for (int f = r + 1; f < end; f++)
                    if (table.Get(f, 1) == "Pod")
                        for (int c = 3; c < table.Rows[f].Count; c++)
                            if (table.Get(f, c) is { Length: > 0 } pod) section.ColumnNames[c] = pod;
                Sections.Add(section);
                r = end;
            }
        }
        else
        {
            for (int r = headerEnd; r < rows; r++) if (!IsBlank(table.Rows[r])) RecordRows.Add(r);
        }

        // Field kinds, from every value a field holds.
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string key, string value)
        {
            if (value.Length == 0) return;
            if (!values.TryGetValue(key, out var list)) values[key] = list = [];
            list.Add(value);
        }
        if (Sectioned)
        {
            foreach (var s in Sections)
                for (int f = s.Row + 1; f < s.End; f++)
                {
                    string field = table.Get(f, 1);
                    if (field.Length == 0) continue;
                    for (int c = FirstValueColumn; c < table.Rows[f].Count; c++) Add(field, table.Get(f, c));
                }
        }
        else
        {
            foreach (int r in RecordRows)
                for (int c = 0; c < table.Rows[r].Count; c++) Add(ColumnKey(c), table.Get(r, c));
        }
        foreach (var (key, list) in values) _fields[key] = Judge(list);
    }

    private static string ColumnKey(int column) => "#" + column.ToString(CultureInfo.InvariantCulture);

    private static (ConfigValueKind, List<string>) Judge(List<string> values)
    {
        if (values.Count == 0) return (ConfigValueKind.Empty, []);
        if (values.All(IsNumber)) return (ConfigValueKind.Number, []);
        var distinct = values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        // A placeholder counts as a name: vendor.csv's "XXXXXXXXXX", and "0" for none (level.csv's NameTag).
        if (values.Any(v => NameValue.IsMatch(v)) && values.All(v => NameValue.IsMatch(v) || v.All(ch => ch == 'X') || v == "0"))
            return (ConfigValueKind.Name, distinct);
        return (ConfigValueKind.Text, []);
    }

    public static bool IsNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>The kind and seen values of a sectioned table's field (by name) or a record table's column.</summary>
    public (ConfigValueKind Kind, IReadOnlyList<string> Choices) FieldOf(string field) =>
        _fields.TryGetValue(field, out var f) ? (f.Kind, f.Choices) : (ConfigValueKind.Empty, []);

    public (ConfigValueKind Kind, IReadOnlyList<string> Choices) ColumnOf(int column) => FieldOf(ColumnKey(column));

    /// <summary>What a value column is called inside a section: the section's own name for it, else the table's.</summary>
    public string ColumnName(ConfigSection? section, int column)
    {
        if (section != null && section.ColumnNames.TryGetValue(column, out var own)) return own;
        return column < ColumnNames.Length ? ColumnNames[column] : "";
    }

    /// <summary>Whether a value fits its field: a Number field's value must be a number.</summary>
    public static bool Fits(ConfigValueKind kind, string value) =>
        value.Length == 0 || kind != ConfigValueKind.Number || IsNumber(value);
}
