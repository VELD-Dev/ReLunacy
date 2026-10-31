using ReLunacy.Engine.Scripting;

namespace ReLunacy.Utility.CodeEditor;

/// <summary>The two characters of a delimiter pair, as positions in the document.</summary>
public readonly record struct DelimiterPair(TextPosition Open, TextPosition Close);

/// <summary>
/// Finds the innermost delimiter pair a position sits inside: <c>()</c>, <c>[]</c>, <c>{}</c>, or a
/// quoted string's own two quotes.
///
/// It lexes rather than counting characters, for the same reason the indent rules do: a bracket in
/// a comment or inside a string is not a bracket. That falls out of the token stream for free -
/// a comment is one token and a string is one token, so neither can contribute a delimiter, and a
/// string contributes exactly one pair, its own.
/// </summary>
public static class LuaDelimiterRules
{
    private static readonly Dictionary<string, string> Pairs =
        new() { ["("] = ")", ["["] = "]", ["{"] = "}" };

    /// <summary>
    /// The innermost pair enclosing <paramref name="at"/>, or null when it is not inside one.
    ///
    /// Inside means strictly past the opener and no further than the closer, so <c>f(|)</c> is
    /// inside and <c>f()|</c> is not. Being just outside a pair is deliberately not a match: the
    /// point is to show what encloses the caret, and a caret that has left a bracket is no longer
    /// enclosed by it.
    /// </summary>
    public static DelimiterPair? Enclosing(string text, TextPosition at)
    {
        var tokens = LuaLexer.Tokenize(text);
        string[] lines = text.Split('\n');

        // A string's quotes enclose everything in it, and nothing inside can be a closer, so a
        // match here is final - no bracket pair can be more inner than the string it is written in.
        //
        // Which quote opened it has to be read from the source: a string token's Text is the
        // DECODED value, so it holds neither the quotes nor the backslashes, and its length says
        // nothing about how wide the literal is.
        foreach (var token in tokens)
        {
            if (token.Kind != LuaTokenKind.String) continue;
            // A quoted string cannot span lines in Lua 5.0, so anything that does is a long string.
            if (token.Start.Line != token.End.Line) continue;
            // And a long string's delimiters are two characters each, which is not what this shows.
            if (CharAt(lines, token.Start) is not ('"' or '\'')) continue;

            var open = At(token.Start);
            var close = Before(token.End);
            if (close > open && Inside(open, close, at)) return new DelimiterPair(open, close);
        }

        // Brackets, innermost first: a pair closes before any pair that contains it, so the first
        // enclosing pair to be completed while scanning forward is the deepest one.
        var openers = new Stack<(string Text, TextPosition Position)>();
        foreach (var token in tokens)
        {
            if (token.Kind != LuaTokenKind.Symbol) continue;

            if (Pairs.ContainsKey(token.Text)) { openers.Push((token.Text, At(token.Start))); continue; }
            if (openers.Count == 0) continue;
            if (Pairs[openers.Peek().Text] != token.Text) continue;  // a stray closer closes nothing

            var open = openers.Pop().Position;
            var close = At(token.Start);
            if (Inside(open, close, at)) return new DelimiterPair(open, close);
        }
        return null;
    }

    private static TextPosition At(LuaPosition position) => new(position.Line, position.Column);

    private static char CharAt(string[] lines, LuaPosition position) =>
        position.Line >= 0 && position.Line < lines.Length
        && position.Column >= 0 && position.Column < lines[position.Line].Length
            ? lines[position.Line][position.Column]
            : '\0';

    /// <summary>A token's last character, its End being exclusive. Only meaningful for a token that
    /// does not span lines, which a quoted string cannot.</summary>
    private static TextPosition Before(LuaPosition end) => new(end.Line, end.Column - 1);

    private static bool Inside(TextPosition open, TextPosition close, TextPosition at) =>
        at > open && at <= close;
}
