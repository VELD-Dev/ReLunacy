namespace ReLunacy.Utility.CodeEditor;

/// <summary>A delimiter that closes itself. Quotes have the same character both ends.</summary>
public readonly record struct AutoPair(char Open, char Close)
{
    public bool IsQuote => Open == Close;
}

/// <summary>
/// When typing an opening delimiter should insert its closer too.
///
/// Separated from the editor because the rule is the whole feature: auto-closing unconditionally is
/// what makes the behaviour hated rather than helpful. Typing <c>(</c> to the left of existing text
/// must not wrap it - <c>(</c> before <c>foo</c> becoming <c>()foo</c> leaves a bracket to delete
/// every time - and an apostrophe after a letter is punctuation in a comment, not a string opening,
/// so <c>don't</c> must not become <c>don''t</c>.
/// </summary>
public static class AutoPairRules
{
    /// <summary>
    /// Lua's delimiters. Long brackets need no entry of their own: typing <c>[</c> twice pairs each
    /// one, giving <c>[[|]]</c> - an empty long string with the caret inside it.
    /// </summary>
    public static IReadOnlyList<AutoPair> Default { get; } =
        [new('(', ')'), new('{', '}'), new('[', ']'), new('"', '"'), new('\'', '\'')];

    public static AutoPair? OpenerFor(char c, IReadOnlyList<AutoPair> pairs)
    {
        foreach (var pair in pairs)
            if (pair.Open == c) return pair;
        return null;
    }

    /// <summary>
    /// Whether to insert the closer, given the line and where the caret is on it.
    ///
    /// The closer goes in only when what follows the caret is nothing, whitespace, or a delimiter
    /// that is itself closing - so typing into empty space pairs up, and typing in front of a word
    /// does not.
    /// </summary>
    public static bool ShouldClose(AutoPair pair, string line, int column)
    {
        if (column > line.Length) return false;

        char next = column < line.Length ? line[column] : '\0';
        bool roomAfter = next is '\0' or ')' or ']' or '}' or ',' or ';' || char.IsWhiteSpace(next);
        if (!roomAfter) return false;

        if (pair.IsQuote)
        {
            char previous = column > 0 ? line[column - 1] : '\0';
            // An apostrophe or quote directly after a word is punctuation, or closes a string the
            // person is typing by hand. Either way a second one is wrong.
            if (IsWordCharacter(previous) || previous == pair.Open) return false;
        }

        return true;
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';
}
