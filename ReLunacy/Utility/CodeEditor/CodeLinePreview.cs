namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// One line of code prepared for display in a list: tabs expanded, leading indentation dropped, and
/// a map from the line's real columns to where they ended up.
///
/// The map is the reason this exists. A results list wants to show the code without the indentation
/// that pushes it off the right edge, but it also has to paint a background behind the exact
/// characters a reference covers - and the columns a reference is reported in are the file's, not
/// the display's. Doing either without the other gives a highlight on the wrong characters, which
/// looks like a bug in the search rather than in the drawing.
/// </summary>
public sealed class CodeLinePreview
{
    /// <summary>The text to draw: tabs expanded, no leading whitespace, no carriage return.</summary>
    public required string Text { get; init; }

    /// <summary>Where each of the source line's columns landed, one entry per column plus the end.
    /// Clamped, so a column past the line's end maps to its end rather than throwing.</summary>
    private int[] Columns { get; init; } = [];

    public int ColumnOf(int sourceColumn) =>
        Columns.Length == 0 ? 0 : Columns[Math.Clamp(sourceColumn, 0, Columns.Length - 1)];

    public static CodeLinePreview Of(string line, int tabWidth = 4)
    {
        line = line.TrimEnd('\r');

        // Expand tabs first, recording where every source column ends up. A tab advances to the
        // next multiple of the tab width, which is what the editor's own column arithmetic does.
        var text = new System.Text.StringBuilder(line.Length);
        var columns = new int[line.Length + 1];
        for (int i = 0; i < line.Length; i++)
        {
            columns[i] = text.Length;
            if (line[i] == '\t') text.Append(' ', tabWidth - text.Length % tabWidth);
            else text.Append(line[i]);
        }
        columns[line.Length] = text.Length;

        // Then drop the indentation, shifting the map with it, so the highlight follows.
        string expanded = text.ToString();
        int indent = expanded.Length - expanded.TrimStart().Length;
        if (indent > 0)
        {
            for (int i = 0; i < columns.Length; i++) columns[i] = Math.Max(0, columns[i] - indent);
            expanded = expanded[indent..];
        }

        return new CodeLinePreview { Text = expanded, Columns = columns };
    }
}
