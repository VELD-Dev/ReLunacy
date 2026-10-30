namespace ReLunacy.Utility.CodeEditor;

/// <summary>How a search compares.</summary>
public readonly record struct TextSearchOptions(bool MatchCase = false, bool WholeWord = false);

/// <summary>
/// Finds every occurrence of a string in a document.
///
/// Line by line rather than over the joined text, because a match has to come back as a line and a
/// column - the editor has no notion of an absolute offset - and because a needle containing a
/// newline is not something a find bar is for.
/// </summary>
public static class TextSearch
{
    /// <summary>
    /// Every match, in document order. An empty needle matches nothing: a find bar with nothing
    /// typed in it should highlight nothing, not every position in the file.
    /// </summary>
    public static List<TextRange> FindAll(TextDocument document, string needle, TextSearchOptions options)
    {
        var matches = new List<TextRange>();
        if (needle.Length == 0) return matches;

        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (int line = 0; line < document.LineCount; line++)
        {
            string text = document[line];
            int at = 0;
            while (at <= text.Length - needle.Length)
            {
                int found = text.IndexOf(needle, at, comparison);
                if (found < 0) break;
                if (!options.WholeWord || IsWholeWord(text, found, needle.Length))
                    matches.Add(new TextRange(new TextPosition(line, found),
                                              new TextPosition(line, found + needle.Length)));
                // Advance by one, not by the needle's length, so overlapping matches are all found
                // - searching `aa` in `aaa` is two hits, and a count that says one is wrong.
                at = found + 1;
            }
        }
        return matches;
    }

    /// <summary>
    /// Which match to go to from <paramref name="from"/>, wrapping around the end.
    ///
    /// Forwards takes the first match that STARTS after the position, so pressing next from inside
    /// a hit moves on rather than finding the same one again. Backwards takes the last that ends at
    /// or before it, by the same reasoning. -1 when there are no matches at all.
    /// </summary>
    public static int Nearest(List<TextRange> matches, TextPosition from, bool forward)
    {
        if (matches.Count == 0) return -1;

        if (forward)
        {
            for (int i = 0; i < matches.Count; i++)
                if (matches[i].Start > from) return i;
            return 0;
        }

        for (int i = matches.Count - 1; i >= 0; i--)
            if (matches[i].End <= from) return i;
        return matches.Count - 1;
    }

    /// <summary>Whether a match is bounded by something other than a word character on both
    /// sides. Uses the coarse definition - an identifier is a whole word, not its camelCase
    /// pieces - because that is what someone ticking "whole word" means.</summary>
    private static bool IsWholeWord(string line, int at, int length)
    {
        bool before = at == 0 || !IsWordChar(line[at - 1]);
        bool after = at + length >= line.Length || !IsWordChar(line[at + length]);
        return before && after;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
