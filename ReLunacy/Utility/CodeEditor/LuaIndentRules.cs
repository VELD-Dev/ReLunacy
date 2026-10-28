using ReLunacy.Engine.Scripting;

namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// Decides whether the text up to a position leaves a scope open, so Enter can indent into it.
///
/// It lexes rather than matching the end of the line, because the words that open a scope also occur
/// inside comments and strings - a line ending <c>-- do this then</c> must not indent, and the
/// shipped scripts are full of trailing comments. Lexing also handles a line that both opens and
/// closes, like <c>if x then return end</c>, which should not indent at all.
/// </summary>
public static class LuaIndentRules
{
    /// <summary>Keywords and symbols whose appearance leaves a block open.</summary>
    private static readonly string[] Openers = ["then", "do", "else", "repeat", "function", "{", "("];

    /// <summary>And those that close one.</summary>
    private static readonly string[] Closers = ["end", "until", "}", ")"];

    /// <summary>
    /// True when the significant tokens before <paramref name="caret"/> leave more scopes open than
    /// closed on that line.
    ///
    /// Only tokens on the caret's own line count towards the balance, but the whole text before it
    /// is lexed: that is what tells a <c>then</c> inside a long string or block comment - which can
    /// have begun lines earlier - from a real one.
    /// </summary>
    public static bool OpensScope(string text, TextPosition caret)
    {
        // The WHOLE text is lexed, not the prefix: a long comment or long string opened earlier is
        // only recognisable as one if its closing bracket - which is after the caret - is visible.
        // Lexing the prefix alone leaves it unterminated, and the lexer then reasonably reads its
        // following lines as code.
        var tokens = LuaLexer.Tokenize(text);

        int depth = 0;
        foreach (var token in tokens)
        {
            if (token.Kind == LuaTokenKind.EndOfFile) continue;

            // Inside a comment or a string there is no scope to open; Enter should just carry the
            // current indentation.
            if (token.Kind is LuaTokenKind.Comment or LuaTokenKind.String
                && Covers(token, caret)) return false;

            if (token.Kind is not (LuaTokenKind.Keyword or LuaTokenKind.Symbol)) continue;
            // Only this line's tokens, and only those the caret has passed.
            if (token.Start.Line != caret.Line || token.End.Column > caret.Column) continue;

            if (Openers.Contains(token.Text)) depth++;
            else if (Closers.Contains(token.Text)) depth--;
        }
        return depth > 0;
    }

    /// <summary>Whether a token's span contains a position, its end exclusive.</summary>
    private static bool Covers(LuaToken token, TextPosition at)
    {
        bool afterStart = at.Line > token.Start.Line
                       || (at.Line == token.Start.Line && at.Column >= token.Start.Column);
        bool beforeEnd = at.Line < token.End.Line
                      || (at.Line == token.End.Line && at.Column < token.End.Column);
        return afterStart && beforeEnd;
    }
}
