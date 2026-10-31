namespace ReLunacy.Engine.Scripting;

/// <summary>A source location. Line and column are both zero-based.</summary>
public readonly record struct LuaPosition(int Line, int Column)
{
    public override string ToString() => $"{Line + 1}:{Column + 1}";
}

public enum LuaTokenKind
{
    EndOfFile,
    Name, Number, String,
    Keyword,
    /// <summary>Operators and punctuation.</summary>
    Symbol,
    Comment,
    /// <summary>Something that is not valid Lua. The lexer emits these and carries on rather than
    /// failing, because an editor has to colour a file that is halfway through being typed.</summary>
    Error,
}

/// <summary>One token, spanning <see cref="Start"/> up to but excluding <see cref="End"/>. Long
/// strings and long comments span lines, which is why the end is a position and not a length.</summary>
public readonly record struct LuaToken(LuaTokenKind Kind, LuaPosition Start, LuaPosition End, string Text)
{
    public bool Is(string text) => Text == text;
    public override string ToString() => $"{Kind} {Start} {Text}";
}

/// <summary>
/// Lua 5.0 lexer.
///
/// The dialect is 5.0, confirmed against the 524 shipped sources rather than assumed: no <c>#</c>
/// length operator and no <c>%</c> modulo (5.1 additions - every occurrence in the corpus is inside
/// a comment or a string literal), no <c>[=[</c> level brackets, and no goto/labels. Long strings
/// are 5.0's nestable <c>[[ ]]</c>.
///
/// It never throws. Unterminated strings, stray characters and malformed numbers become
/// <see cref="LuaTokenKind.Error"/> tokens and lexing continues, so the editor can highlight and the
/// parser can report several problems in one pass instead of stopping at the first.
/// </summary>
public static class LuaLexer
{
    private static readonly HashSet<string> Keywords =
    [
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
    ];

    /// <summary>Longest first: the scanner takes the first match, so "..." must be tried before
    /// "..", and "==" before "=".</summary>
    private static readonly string[] Symbols =
    [
        "...", "..", "==", "~=", "<=", ">=",
        "+", "-", "*", "/", "^", "=", "<", ">", "(", ")", "{", "}", "[", "]", ";", ":", ",", ".",
    ];

    public static bool IsKeyword(string text) => Keywords.Contains(text);

    /// <summary>Tokenizes the whole source. Comments are included: the editor colours them, and the
    /// parser skips them.</summary>
    public static List<LuaToken> Tokenize(string source)
    {
        var tokens = new List<LuaToken>();
        var s = new Scanner(source);

        while (true)
        {
            s.SkipWhitespace();
            if (s.AtEnd)
            {
                tokens.Add(new LuaToken(LuaTokenKind.EndOfFile, s.Position, s.Position, ""));
                return tokens;
            }

            var start = s.Position;
            char c = s.Peek();

            // Comment, or the subtraction operator.
            if (c == '-' && s.Peek(1) == '-')
            {
                s.Advance(2);
                if (TryLongBracket(ref s, out string? body))
                    tokens.Add(new LuaToken(LuaTokenKind.Comment, start, s.Position, body ?? ""));
                else
                {
                    string line = s.TakeToEndOfLine();
                    tokens.Add(new LuaToken(LuaTokenKind.Comment, start, s.Position, line));
                }
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                string name = s.TakeWhile(ch => char.IsLetterOrDigit(ch) || ch == '_');
                tokens.Add(new LuaToken(Keywords.Contains(name) ? LuaTokenKind.Keyword : LuaTokenKind.Name,
                                        start, s.Position, name));
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && char.IsDigit(s.Peek(1))))
            {
                tokens.Add(ReadNumber(ref s, start));
                continue;
            }

            if (c is '"' or '\'')
            {
                tokens.Add(ReadQuotedString(ref s, start));
                continue;
            }

            // A long string. Only reachable when it really opens one, so "[" stays an index.
            if (c == '[' && s.Peek(1) == '[')
            {
                if (TryLongBracket(ref s, out string? body))
                {
                    tokens.Add(new LuaToken(LuaTokenKind.String, start, s.Position, body ?? ""));
                    continue;
                }
                s.Advance(2);
                tokens.Add(new LuaToken(LuaTokenKind.Error, start, s.Position, "unterminated long string"));
                continue;
            }

            string? symbol = Symbols.FirstOrDefault(s.StartsWith);
            if (symbol != null)
            {
                s.Advance(symbol.Length);
                tokens.Add(new LuaToken(LuaTokenKind.Symbol, start, s.Position, symbol));
                continue;
            }

            s.Advance(1);
            tokens.Add(new LuaToken(LuaTokenKind.Error, start, s.Position, $"unexpected character '{c}'"));
        }
    }

    private static LuaToken ReadNumber(ref Scanner s, LuaPosition start)
    {
        // 5.0 read numerals through strtod, so a hex literal is accepted even though none appears in
        // the corpus; being permissive here costs nothing and rejecting it would be a guess.
        if (s.Peek() == '0' && (s.Peek(1) is 'x' or 'X'))
        {
            s.Advance(2);
            string hex = s.TakeWhile(Uri.IsHexDigit);
            return new LuaToken(hex.Length == 0 ? LuaTokenKind.Error : LuaTokenKind.Number,
                                start, s.Position, hex.Length == 0 ? "malformed hex number" : "0x" + hex);
        }

        string digits = s.TakeWhile(char.IsDigit);
        string fraction = "";
        if (s.Peek() == '.')
        {
            s.Advance(1);
            fraction = "." + s.TakeWhile(char.IsDigit);
        }
        string exponent = "";
        if (s.Peek() is 'e' or 'E')
        {
            s.Advance(1);
            string sign = s.Peek() is '+' or '-' ? s.Take(1) : "";
            string exponentDigits = s.TakeWhile(char.IsDigit);
            if (exponentDigits.Length == 0)
                return new LuaToken(LuaTokenKind.Error, start, s.Position, "malformed number exponent");
            exponent = "e" + sign + exponentDigits;
        }
        return new LuaToken(LuaTokenKind.Number, start, s.Position, digits + fraction + exponent);
    }

    private static LuaToken ReadQuotedString(ref Scanner s, LuaPosition start)
    {
        char quote = s.Take(1)[0];
        var text = new System.Text.StringBuilder();
        while (true)
        {
            if (s.AtEnd || s.Peek() == '\n')
                return new LuaToken(LuaTokenKind.Error, start, s.Position, "unterminated string");

            char c = s.Take(1)[0];
            if (c == quote) return new LuaToken(LuaTokenKind.String, start, s.Position, text.ToString());
            if (c != '\\') { text.Append(c); continue; }

            if (s.AtEnd) return new LuaToken(LuaTokenKind.Error, start, s.Position, "unterminated escape");
            char e = s.Take(1)[0];
            switch (e)
            {
                case 'a': text.Append('\a'); break;
                case 'b': text.Append('\b'); break;
                case 'f': text.Append('\f'); break;
                case 'n': text.Append('\n'); break;
                case 'r': text.Append('\r'); break;
                case 't': text.Append('\t'); break;
                case 'v': text.Append('\v'); break;
                // A backslash before a real newline embeds that newline.
                case '\n': text.Append('\n'); break;
                default:
                    if (char.IsDigit(e))
                    {
                        // \ddd, up to three decimal digits.
                        int value = e - '0';
                        for (int i = 0; i < 2 && char.IsDigit(s.Peek()); i++) value = value * 10 + (s.Take(1)[0] - '0');
                        text.Append((char)value);
                    }
                    else text.Append(e); // \\ \" \' and anything else stands for itself
                    break;
            }
        }
    }

    /// <summary>
    /// Reads a 5.0 long bracket, assuming the caller is positioned at <c>[[</c>. Nesting is 5.0
    /// behaviour: <c>[[ a [[ b ]] c ]]</c> is one string. A newline straight after the opening
    /// bracket is skipped, as the reference lexer does.
    /// </summary>
    private static bool TryLongBracket(ref Scanner s, out string? body)
    {
        body = null;
        if (!s.StartsWith("[[")) return false;
        int startIndex = s.Index;
        s.Advance(2);
        if (s.Peek() == '\n') s.Advance(1);

        int depth = 1;
        var text = new System.Text.StringBuilder();
        while (depth > 0)
        {
            if (s.AtEnd)
            {
                s.Reset(startIndex);
                return false;
            }
            if (s.StartsWith("[[")) { depth++; text.Append(s.Take(2)); continue; }
            if (s.StartsWith("]]"))
            {
                depth--;
                if (depth == 0) { s.Advance(2); break; }
                text.Append(s.Take(2));
                continue;
            }
            text.Append(s.Take(1));
        }
        body = text.ToString();
        return true;
    }

    /// <summary>Character cursor that tracks line and column so tokens carry real positions.</summary>
    private struct Scanner(string source)
    {
        private readonly string _source = source;
        public int Index { get; private set; }
        private int _line, _column;

        public LuaPosition Position => new(_line, _column);
        public bool AtEnd => Index >= _source.Length;

        public char Peek(int offset = 0) =>
            Index + offset < _source.Length ? _source[Index + offset] : '\0';

        public bool StartsWith(string text) =>
            Index + text.Length <= _source.Length && _source.AsSpan(Index, text.Length).SequenceEqual(text);

        public void Advance(int count)
        {
            for (int i = 0; i < count && Index < _source.Length; i++)
            {
                if (_source[Index] == '\n') { _line++; _column = 0; }
                else _column++;
                Index++;
            }
        }

        public string Take(int count)
        {
            int from = Index;
            Advance(count);
            return _source[from..Index];
        }

        public string TakeWhile(Func<char, bool> predicate)
        {
            int from = Index;
            while (!AtEnd && predicate(_source[Index])) Advance(1);
            return _source[from..Index];
        }

        public string TakeToEndOfLine() => TakeWhile(c => c != '\n');

        public void SkipWhitespace() => TakeWhile(char.IsWhiteSpace);

        /// <summary>Rewinds to a byte index, recomputing line and column - used when a long bracket
        /// turns out to be unterminated and has to be reinterpreted.</summary>
        public void Reset(int index)
        {
            Index = 0;
            _line = 0;
            _column = 0;
            Advance(index);
        }
    }
}
