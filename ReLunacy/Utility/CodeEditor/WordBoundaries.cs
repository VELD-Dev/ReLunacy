namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// Where one word ends and the next begins, for word-wise movement and deletion.
///
/// Finer than "a run of letters": punctuation that structures code is its own unit, and a word
/// breaks at an internal capital. So Ctrl+Backspace through <c>obj.getName(a, b);</c> takes it
/// apart in the pieces it was written in - <c>;</c>, <c>)</c>, <c>b</c>, <c>,</c>, <c>a</c>,
/// <c>(</c>, <c>Name</c>, <c>get</c>, <c>.</c>, <c>obj</c> - rather than swallowing the lot in two
/// presses. That is Visual Studio's behaviour, and the reason for it is that a mistake costs one
/// keystroke to fix instead of retyping the line.
///
/// Underscores deliberately do NOT break a word. These scripts are written in snake_case, so
/// treating <c>_</c> as a boundary would make Ctrl+Backspace eat <c>moby_handle</c> in three presses
/// - fine for camelCase, wrong for the code this editor is for.
///
/// Pure string arithmetic so the rules can be checked directly: every one of them is a case
/// somebody only notices when it is in the way.
/// </summary>
public static class WordBoundaries
{
    /// <summary>
    /// Punctuation that stands alone, consumed one at a time. These are the characters that
    /// separate the parts of a statement, so stopping at each is what makes a correction local.
    /// Quotes are here for the same reason: deleting back through a string should stop at its edge
    /// rather than carry on into the code before it.
    /// </summary>
    private const string Standalone = "\"'.;,{}[]()";

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
    private static bool IsSpace(char c) => c is ' ' or '\t';
    private static bool IsStandalone(char c) => Standalone.Contains(c);

    /// <summary>Anything else: operators and the rest of the punctuation, taken as a run, so
    /// <c>==</c> and <c>..</c> are not picked apart a character at a time.</summary>
    private static bool IsOther(char c) => !IsWord(c) && !IsSpace(c) && !IsStandalone(c);

    /// <summary>
    /// Whether a capital starts a new part of a word: after a non-capital (<c>getName</c>), or at
    /// the end of a run of capitals that is followed by lower case (<c>HTTPServer</c>, whose second
    /// part begins at the S, not the final P).
    /// </summary>
    private static bool IsInternalCapital(string line, int at, int wordEnd) =>
        char.IsUpper(line[at])
        && (!char.IsUpper(line[at - 1]) || (at + 1 < wordEnd && char.IsLower(line[at + 1])));

    /// <summary>
    /// Where a word-wise move or delete leaving <paramref name="column"/> backwards should land.
    ///
    /// Whitespace immediately behind the caret comes away with whatever precedes it: stopping on
    /// the spaces alone would mean two presses to undo one word, every time.
    /// </summary>
    public static int StartBefore(string line, int column)
    {
        int i = Math.Clamp(column, 0, line.Length);
        while (i > 0 && IsSpace(line[i - 1])) i--;
        if (i == 0) return 0;

        char previous = line[i - 1];
        if (previous == '.' && i >= 2 && line[i - 2] == '.')
        {
            // `..` and `...` are single tokens in Lua - concatenation and vararg - so a run of dots
            // comes away together even though one dot on its own is a boundary.
            while (i > 0 && line[i - 1] == '.') i--;
            return i;
        }
        if (IsStandalone(previous)) return i - 1;
        if (IsOther(previous))
        {
            while (i > 0 && IsOther(line[i - 1])) i--;
            return i;
        }

        // A word: back to its last internal capital, or to its start when it has none.
        int wordStart = i;
        while (wordStart > 0 && IsWord(line[wordStart - 1])) wordStart--;
        int landing = wordStart;
        for (int k = wordStart + 1; k < i; k++)
            if (IsInternalCapital(line, k, i)) landing = k;
        return landing;
    }

    /// <summary>Where a word-wise move forwards from <paramref name="column"/> should land, by the
    /// same rules read the other way.</summary>
    public static int EndAfter(string line, int column)
    {
        int i = Math.Clamp(column, 0, line.Length);
        while (i < line.Length && IsSpace(line[i])) i++;
        if (i == line.Length) return line.Length;

        char next = line[i];
        if (next == '.' && i + 1 < line.Length && line[i + 1] == '.')
        {
            while (i < line.Length && line[i] == '.') i++;
            return i;
        }
        if (IsStandalone(next)) return i + 1;
        if (IsOther(next))
        {
            while (i < line.Length && IsOther(line[i])) i++;
            return i;
        }

        int wordEnd = i;
        while (wordEnd < line.Length && IsWord(line[wordEnd])) wordEnd++;
        for (int k = i + 1; k < wordEnd; k++)
            if (IsInternalCapital(line, k, wordEnd)) return k;
        return wordEnd;
    }
}
