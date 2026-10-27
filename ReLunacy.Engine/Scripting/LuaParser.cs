using System.Globalization;

namespace ReLunacy.Engine.Scripting;

/// <summary>A syntax problem, with where it is so the editor can mark it.</summary>
public readonly record struct LuaDiagnostic(string Message, LuaSpan Span)
{
    public override string ToString() => $"{Span.Start}: {Message}";
}

/// <summary>
/// Recursive-descent parser for Lua 5.0.
///
/// Binding powers are the reference implementation's, which is what makes the tree match what the
/// compiler must emit: <c>^</c> is 10/9 and <c>..</c> is 5/4 - both right-associative, encoded as a
/// right power one below the left - <c>*</c> and <c>/</c> are 7, <c>+</c> and <c>-</c> are 6,
/// comparisons 3, <c>and</c> 2, <c>or</c> 1, and unary operators bind at 8. There is no <c>%</c> or
/// <c>#</c>: both are 5.1 additions and neither appears in the corpus outside comments and strings.
///
/// It recovers instead of throwing. An unparseable statement becomes a <see cref="LuaErrorStat"/>
/// and parsing resumes at the next statement boundary, so a file being typed still yields a tree
/// for everything around the mistake - which is what completion and rename need to keep working.
/// </summary>
public sealed class LuaParser
{
    private readonly List<LuaToken> _tokens;
    private int _index;

    public List<LuaDiagnostic> Diagnostics { get; } = [];

    private LuaParser(List<LuaToken> tokens) => _tokens = tokens;

    public static LuaBlock Parse(string source, out List<LuaDiagnostic> diagnostics)
    {
        // Comments carry no syntax; dropping them here keeps every lookahead below simple.
        var tokens = LuaLexer.Tokenize(source).Where(t => t.Kind != LuaTokenKind.Comment).ToList();
        var parser = new LuaParser(tokens);
        foreach (var error in tokens.Where(t => t.Kind == LuaTokenKind.Error))
            parser.Diagnostics.Add(new LuaDiagnostic(error.Text, new LuaSpan(error.Start, error.End)));

        var block = parser.ParseBlock();
        if (!parser.AtEnd) parser.Error($"unexpected '{parser.Current.Text}'");
        diagnostics = parser.Diagnostics;
        return block;
    }

    // --- token helpers ------------------------------------------------------------------------

    private LuaToken Current => _tokens[Math.Min(_index, _tokens.Count - 1)];
    private LuaToken Ahead(int n = 1) => _tokens[Math.Min(_index + n, _tokens.Count - 1)];
    private bool AtEnd => Current.Kind == LuaTokenKind.EndOfFile;
    private LuaPosition Here => Current.Start;

    private LuaToken Next() => _tokens[Math.Min(_index++, _tokens.Count - 1)];

    private bool Check(string text) => Current.Text == text
        && Current.Kind is LuaTokenKind.Symbol or LuaTokenKind.Keyword;

    private bool Match(string text)
    {
        if (!Check(text)) return false;
        _index++;
        return true;
    }

    private void Expect(string text)
    {
        if (Match(text)) return;
        Error($"expected '{text}' but found '{(AtEnd ? "<eof>" : Current.Text)}'");
    }

    private void Error(string message) =>
        Diagnostics.Add(new LuaDiagnostic(message, new LuaSpan(Current.Start, Current.End)));

    private LuaSpan From(LuaPosition start) => new(start, _tokens[Math.Max(0, _index - 1)].End);

    // --- blocks and statements ----------------------------------------------------------------

    private static readonly string[] BlockEnders = ["end", "else", "elseif", "until"];

    private LuaBlock ParseBlock()
    {
        var start = Here;
        var statements = new List<LuaStat>();
        while (!AtEnd && !BlockEnders.Contains(Current.Text))
        {
            if (Match(";")) continue;

            int before = _index;
            var statement = ParseStatement();
            statements.Add(statement);

            // `return` and `break` end a block in 5.0; anything after them is unreachable and is a
            // syntax error rather than a statement. The grammar is `[laststat [';']]`, so the
            // terminator takes an optional semicolon - `return count;` appears in 7 of the shipped
            // scripts and breaking before consuming it reported a missing `end`.
            if (statement is LuaReturnStat or LuaBreakStat)
            {
                Match(";");
                break;
            }

            // Guarantees progress: without this, a statement the parser cannot consume would spin.
            if (_index == before)
            {
                Error($"unexpected '{Current.Text}'");
                statements.Add(new LuaErrorStat($"unexpected '{Current.Text}'", new LuaSpan(Current.Start, Current.End)));
                _index++;
            }
        }
        return new LuaBlock(statements, From(start));
    }

    private LuaStat ParseStatement()
    {
        var start = Here;

        if (Match("do")) { var body = ParseBlock(); Expect("end"); return new LuaDoStat(body, From(start)); }
        if (Match("while"))
        {
            var condition = ParseExpr();
            Expect("do");
            var body = ParseBlock();
            Expect("end");
            return new LuaWhileStat(condition, body, From(start));
        }
        if (Match("repeat"))
        {
            var body = ParseBlock();
            Expect("until");
            // The until-condition sees the body's locals, which the resolver has to honour.
            var condition = ParseExpr();
            return new LuaRepeatStat(body, condition, From(start));
        }
        if (Match("if")) return ParseIf(start);
        if (Match("for")) return ParseFor(start);
        if (Match("function")) return ParseFunctionStat(start);
        if (Match("local")) return ParseLocal(start);
        if (Match("return"))
        {
            var values = AtEnd || BlockEnders.Contains(Current.Text) || Check(";")
                ? [] : ParseExprList();
            return new LuaReturnStat(values, From(start));
        }
        if (Match("break")) return new LuaBreakStat(From(start));

        return ParseExprStatement(start);
    }

    private LuaStat ParseIf(LuaPosition start)
    {
        var clauses = new List<LuaIfClause>();
        var clauseStart = start;
        var condition = ParseExpr();
        Expect("then");
        clauses.Add(new LuaIfClause(condition, ParseBlock(), From(clauseStart)));

        while (Check("elseif"))
        {
            clauseStart = Here;
            _index++;
            var elseifCondition = ParseExpr();
            Expect("then");
            clauses.Add(new LuaIfClause(elseifCondition, ParseBlock(), From(clauseStart)));
        }

        LuaBlock? elseBody = Match("else") ? ParseBlock() : null;
        Expect("end");
        return new LuaIfStat(clauses, elseBody, From(start));
    }

    private LuaStat ParseFor(LuaPosition start)
    {
        var first = ParseNameDefinition();

        if (Match("="))
        {
            var from = ParseExpr();
            Expect(",");
            var to = ParseExpr();
            var step = Match(",") ? ParseExpr() : null;
            Expect("do");
            var body = ParseBlock();
            Expect("end");
            return new LuaNumericForStat(first, from, to, step, body, From(start));
        }

        var names = new List<LuaNameDefinition> { first };
        while (Match(",")) names.Add(ParseNameDefinition());
        Expect("in");
        var expressions = ParseExprList();
        Expect("do");
        var genericBody = ParseBlock();
        Expect("end");
        return new LuaGenericForStat(names, expressions, genericBody, From(start));
    }

    private LuaStat ParseFunctionStat(LuaPosition start)
    {
        // funcname: Name {'.' Name} [':' Name]
        var nameStart = Here;
        LuaExpr target = ParseNameExpr();
        bool isMethod = false;
        while (true)
        {
            if (Match("."))
            {
                var key = ParseNameToken();
                target = new LuaIndexExpr(target, new LuaStringExpr(key.Text, new LuaSpan(key.Start, key.End)),
                                          true, From(nameStart));
            }
            else if (Match(":"))
            {
                var key = ParseNameToken();
                target = new LuaIndexExpr(target, new LuaStringExpr(key.Text, new LuaSpan(key.Start, key.End)),
                                          true, From(nameStart));
                isMethod = true;
                break;
            }
            else break;
        }

        var function = ParseFunctionBody(start, implicitSelf: isMethod);
        return new LuaFunctionStat(target, isMethod, function, From(start));
    }

    private LuaStat ParseLocal(LuaPosition start)
    {
        if (Match("function"))
        {
            var name = ParseNameDefinition();
            var function = ParseFunctionBody(start, implicitSelf: false);
            return new LuaLocalFunctionStat(name, function, From(start));
        }

        var names = new List<LuaNameDefinition> { ParseNameDefinition() };
        while (Match(",")) names.Add(ParseNameDefinition());
        var values = Match("=") ? ParseExprList() : [];
        return new LuaLocalStat(names, values, From(start));
    }

    /// <summary>Either an assignment or a call - which one is only known after parsing the first
    /// expression, so the shared prefix is parsed once and then classified.</summary>
    private LuaStat ParseExprStatement(LuaPosition start)
    {
        var first = ParseSuffixedExpr();

        if (Check("=") || Check(","))
        {
            var targets = new List<LuaExpr> { first };
            while (Match(",")) targets.Add(ParseSuffixedExpr());
            Expect("=");
            var values = ParseExprList();
            foreach (var target in targets)
                if (target is not (LuaNameExpr or LuaIndexExpr))
                    Diagnostics.Add(new LuaDiagnostic("cannot assign to this expression", target.Span));
            return new LuaAssignStat(targets, values, From(start));
        }

        if (first is LuaCallExpr or LuaMethodCallExpr) return new LuaCallStat(first, From(start));

        Diagnostics.Add(new LuaDiagnostic("statement must be a call or an assignment", first.Span));
        return new LuaErrorStat("statement must be a call or an assignment", From(start));
    }

    // --- names --------------------------------------------------------------------------------

    private LuaToken ParseNameToken()
    {
        if (Current.Kind == LuaTokenKind.Name) return Next();
        Error($"expected a name but found '{(AtEnd ? "<eof>" : Current.Text)}'");
        // A synthetic empty name keeps the tree shaped correctly so the rest of the file still parses.
        return new LuaToken(LuaTokenKind.Name, Current.Start, Current.Start, "");
    }

    private LuaNameDefinition ParseNameDefinition()
    {
        var token = ParseNameToken();
        return new LuaNameDefinition(token.Text, new LuaSpan(token.Start, token.End));
    }

    private LuaNameExpr ParseNameExpr()
    {
        var token = ParseNameToken();
        return new LuaNameExpr(token.Text, new LuaSpan(token.Start, token.End));
    }

    // --- expressions --------------------------------------------------------------------------

    private List<LuaExpr> ParseExprList()
    {
        var list = new List<LuaExpr> { ParseExpr() };
        while (Match(",")) list.Add(ParseExpr());
        return list;
    }

    /// <summary>Left and right binding powers, straight from lparser.c's priority table. A right
    /// power below the left makes the operator right-associative.</summary>
    private static (int Left, int Right)? BinaryPower(LuaToken token) => token.Text switch
    {
        "or" when token.Kind == LuaTokenKind.Keyword => (1, 1),
        "and" when token.Kind == LuaTokenKind.Keyword => (2, 2),
        "<" or ">" or "<=" or ">=" or "~=" or "==" => (3, 3),
        ".." => (5, 4),
        "+" or "-" => (6, 6),
        "*" or "/" => (7, 7),
        "^" => (10, 9),
        _ => null,
    };

    private const int UnaryPower = 8;

    private LuaExpr ParseExpr(int limit = 0)
    {
        LuaExpr left;
        var start = Here;

        if (Check("not") || Check("-"))
        {
            string op = Next().Text;
            var operand = ParseExpr(UnaryPower);
            left = new LuaUnaryExpr(op, operand, From(start));
        }
        else left = ParseSimpleExpr();

        while (BinaryPower(Current) is { } power && power.Left > limit)
        {
            string op = Next().Text;
            var right = ParseExpr(power.Right);
            left = new LuaBinaryExpr(op, left, right, From(start));
        }
        return left;
    }

    private LuaExpr ParseSimpleExpr()
    {
        var start = Here;
        var token = Current;

        switch (token.Kind)
        {
            case LuaTokenKind.Number:
                _index++;
                return new LuaNumberExpr(token.Text, ParseNumber(token.Text), From(start));
            case LuaTokenKind.String:
                _index++;
                return new LuaStringExpr(token.Text, From(start));
            case LuaTokenKind.Keyword when token.Text == "nil":
                _index++; return new LuaNilExpr(From(start));
            case LuaTokenKind.Keyword when token.Text is "true" or "false":
                _index++; return new LuaBoolExpr(token.Text == "true", From(start));
            case LuaTokenKind.Keyword when token.Text == "function":
                _index++; return ParseFunctionBody(start, implicitSelf: false);
            case LuaTokenKind.Symbol when token.Text == "...":
                _index++; return new LuaVarargExpr(From(start));
            case LuaTokenKind.Symbol when token.Text == "{":
                return ParseTable();
        }
        return ParseSuffixedExpr();
    }

    private static double ParseNumber(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hex) ? hex : 0;
        // Invariant culture: the source is the source regardless of the machine's locale.
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
    }

    /// <summary>A primary expression with any number of index, call and method-call suffixes.</summary>
    private LuaExpr ParseSuffixedExpr()
    {
        var start = Here;
        LuaExpr expr;

        if (Match("("))
        {
            var inner = ParseExpr();
            Expect(")");
            expr = new LuaParenExpr(inner, From(start));
        }
        else if (Current.Kind == LuaTokenKind.Name) expr = ParseNameExpr();
        else
        {
            Error($"expected an expression but found '{(AtEnd ? "<eof>" : Current.Text)}'");
            _index++;
            return new LuaNilExpr(From(start));
        }

        while (true)
        {
            if (Match("."))
            {
                var key = ParseNameToken();
                expr = new LuaIndexExpr(expr, new LuaStringExpr(key.Text, new LuaSpan(key.Start, key.End)),
                                        true, From(start));
            }
            else if (Match("["))
            {
                var key = ParseExpr();
                Expect("]");
                expr = new LuaIndexExpr(expr, key, false, From(start));
            }
            else if (Match(":"))
            {
                var method = ParseNameToken();
                var arguments = ParseCallArguments();
                expr = new LuaMethodCallExpr(expr, method.Text, new LuaSpan(method.Start, method.End),
                                             arguments, From(start));
            }
            else if (Check("(") || Check("{") || Current.Kind == LuaTokenKind.String)
            {
                expr = new LuaCallExpr(expr, ParseCallArguments(), From(start));
            }
            else return expr;
        }
    }

    /// <summary>Call arguments. Lua allows a single string or table literal with no parentheses -
    /// <c>prt"hi"</c> and <c>f{...}</c> - and the corpus uses the parenthesised form throughout, but
    /// accepting all three costs nothing and rejecting them would be wrong.</summary>
    private List<LuaExpr> ParseCallArguments()
    {
        if (Current.Kind == LuaTokenKind.String)
        {
            var token = Next();
            return [new LuaStringExpr(token.Text, new LuaSpan(token.Start, token.End))];
        }
        if (Check("{")) return [ParseTable()];

        Expect("(");
        if (Match(")")) return [];
        var arguments = ParseExprList();
        Expect(")");
        return arguments;
    }

    private LuaExpr ParseTable()
    {
        var start = Here;
        Expect("{");
        var items = new List<LuaTableItem>();

        while (!Check("}") && !AtEnd)
        {
            var itemStart = Here;
            if (Match("["))
            {
                var key = ParseExpr();
                Expect("]");
                Expect("=");
                items.Add(new LuaTableItem(key, false, ParseExpr(), From(itemStart)));
            }
            else if (Current.Kind == LuaTokenKind.Name && Ahead().Text == "=" && Ahead().Kind == LuaTokenKind.Symbol)
            {
                var key = ParseNameToken();
                Expect("=");
                items.Add(new LuaTableItem(new LuaStringExpr(key.Text, new LuaSpan(key.Start, key.End)),
                                           true, ParseExpr(), From(itemStart)));
            }
            else items.Add(new LuaTableItem(null, false, ParseExpr(), From(itemStart)));

            // Both separators are legal, and a trailing one is allowed.
            if (!Match(",") && !Match(";")) break;
        }

        Expect("}");
        return new LuaTableExpr(items, From(start));
    }

    private LuaFunctionExpr ParseFunctionBody(LuaPosition start, bool implicitSelf)
    {
        var parameters = new List<LuaNameDefinition>();
        // A method declaration's receiver is a real local named self, so references to it resolve.
        if (implicitSelf) parameters.Add(new LuaNameDefinition("self", new LuaSpan(start, start)));

        Expect("(");
        bool isVararg = false;
        if (!Check(")"))
        {
            do
            {
                if (Match("...")) { isVararg = true; break; }
                parameters.Add(ParseNameDefinition());
            } while (Match(","));
        }
        Expect(")");

        var body = ParseBlock();
        Expect("end");
        return new LuaFunctionExpr(parameters, isVararg, body, From(start));
    }
}
