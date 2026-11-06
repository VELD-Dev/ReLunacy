namespace ReLunacy.Engine.Scripting;

/// <summary>Source span of a syntax node, for hover, go-to and rename.</summary>
public readonly record struct LuaSpan(LuaPosition Start, LuaPosition End)
{
    public bool Contains(LuaPosition p) =>
        (p.Line > Start.Line || (p.Line == Start.Line && p.Column >= Start.Column)) &&
        (p.Line < End.Line || (p.Line == End.Line && p.Column <= End.Column));

    public override string ToString() => $"{Start}..{End}";
}

public abstract record LuaNode(LuaSpan Span);

/// <summary>A name being introduced - a local, a parameter or a loop variable. Distinct from a
/// reference so scope resolution has something stable to bind references to, and so rename knows
/// which occurrence is the declaration.</summary>
public sealed record LuaNameDefinition(string Name, LuaSpan Span) : LuaNode(Span);

// --- expressions --------------------------------------------------------------------------------

public abstract record LuaExpr(LuaSpan Span) : LuaNode(Span);

public sealed record LuaNilExpr(LuaSpan Span) : LuaExpr(Span);
public sealed record LuaBoolExpr(bool Value, LuaSpan Span) : LuaExpr(Span);
public sealed record LuaVarargExpr(LuaSpan Span) : LuaExpr(Span);
public sealed record LuaNumberExpr(string Text, double Value, LuaSpan Span) : LuaExpr(Span);
public sealed record LuaStringExpr(string Value, LuaSpan Span) : LuaExpr(Span);

/// <summary>A bare identifier. Whether it is a local, an upvalue or a global is decided by
/// <see cref="LuaScopeResolver"/>, not by the parser.</summary>
public sealed record LuaNameExpr(string Name, LuaSpan Span) : LuaExpr(Span);

/// <summary>Both <c>a.b</c> and <c>a[b]</c>. <see cref="Dotted"/> records which form was written so
/// the editor can offer completion after a dot without rewriting the source.</summary>
public sealed record LuaIndexExpr(LuaExpr Target, LuaExpr Key, bool Dotted, LuaSpan Span) : LuaExpr(Span);

public sealed record LuaCallExpr(LuaExpr Target, List<LuaExpr> Arguments, LuaSpan Span) : LuaExpr(Span);

/// <summary>A method call, <c>obj:name(...)</c>. Kept distinct from a call on an index because the
/// receiver is passed implicitly - and because the corpus never defines methods in Lua, only calls
/// them, so these are exactly the sites that need the documentation database to say anything.</summary>
public sealed record LuaMethodCallExpr(LuaExpr Receiver, string Method, LuaSpan MethodSpan,
                                       List<LuaExpr> Arguments, LuaSpan Span) : LuaExpr(Span);

public sealed record LuaFunctionExpr(List<LuaNameDefinition> Parameters, bool IsVararg,
                                     LuaBlock Body, LuaSpan Span) : LuaExpr(Span);

/// <summary>One entry of a table constructor: <c>[k]=v</c>, <c>name=v</c>, or a positional value.</summary>
public sealed record LuaTableItem(LuaExpr? Key, bool KeyIsName, LuaExpr Value, LuaSpan Span) : LuaNode(Span);
public sealed record LuaTableExpr(List<LuaTableItem> Items, LuaSpan Span) : LuaExpr(Span);

public sealed record LuaBinaryExpr(string Operator, LuaExpr Left, LuaExpr Right, LuaSpan Span) : LuaExpr(Span);
public sealed record LuaUnaryExpr(string Operator, LuaExpr Operand, LuaSpan Span) : LuaExpr(Span);

/// <summary>Parentheses are kept rather than folded away: in Lua they truncate a multiple-value
/// expression to one value, so dropping them would change what the code means.</summary>
public sealed record LuaParenExpr(LuaExpr Inner, LuaSpan Span) : LuaExpr(Span);

// --- statements ---------------------------------------------------------------------------------

public abstract record LuaStat(LuaSpan Span) : LuaNode(Span);

public sealed record LuaBlock(List<LuaStat> Statements, LuaSpan Span) : LuaNode(Span);

public sealed record LuaLocalStat(List<LuaNameDefinition> Names, List<LuaExpr> Values, LuaSpan Span) : LuaStat(Span);

/// <summary><c>local function f</c>: the name is in scope inside its own body, so recursion works -
/// which is why it is not just a local plus an assignment.</summary>
public sealed record LuaLocalFunctionStat(LuaNameDefinition Name, LuaFunctionExpr Function, LuaSpan Span) : LuaStat(Span);

/// <summary>A <c>function a.b.c:d()</c> declaration. The target is the assignable path.</summary>
public sealed record LuaFunctionStat(LuaExpr Target, bool IsMethod, LuaFunctionExpr Function, LuaSpan Span) : LuaStat(Span);

public sealed record LuaAssignStat(List<LuaExpr> Targets, List<LuaExpr> Values, LuaSpan Span) : LuaStat(Span);
public sealed record LuaCallStat(LuaExpr Call, LuaSpan Span) : LuaStat(Span);
public sealed record LuaDoStat(LuaBlock Body, LuaSpan Span) : LuaStat(Span);
public sealed record LuaWhileStat(LuaExpr Condition, LuaBlock Body, LuaSpan Span) : LuaStat(Span);
public sealed record LuaRepeatStat(LuaBlock Body, LuaExpr Condition, LuaSpan Span) : LuaStat(Span);

public sealed record LuaIfClause(LuaExpr Condition, LuaBlock Body, LuaSpan Span) : LuaNode(Span);
public sealed record LuaIfStat(List<LuaIfClause> Clauses, LuaBlock? ElseBody, LuaSpan Span) : LuaStat(Span);

public sealed record LuaNumericForStat(LuaNameDefinition Variable, LuaExpr Start, LuaExpr Limit,
                                       LuaExpr? Step, LuaBlock Body, LuaSpan Span) : LuaStat(Span);
public sealed record LuaGenericForStat(List<LuaNameDefinition> Names, List<LuaExpr> Expressions,
                                       LuaBlock Body, LuaSpan Span) : LuaStat(Span);

public sealed record LuaReturnStat(List<LuaExpr> Values, LuaSpan Span) : LuaStat(Span);
public sealed record LuaBreakStat(LuaSpan Span) : LuaStat(Span);

/// <summary>A parse problem. The parser produces these and keeps going so the editor can show
/// several and still have a usable tree for the rest of the file.</summary>
public sealed record LuaErrorStat(string Message, LuaSpan Span) : LuaStat(Span);
