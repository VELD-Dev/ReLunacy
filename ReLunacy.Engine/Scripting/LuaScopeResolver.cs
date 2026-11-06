namespace ReLunacy.Engine.Scripting;

public enum LuaSymbolKind { Local, Parameter, LoopVariable, Global }

/// <summary>The source region a binding is visible in.</summary>
internal sealed class LuaScopeRegion(LuaSpan span)
{
    public LuaSpan Span { get; } = span;
}

/// <summary>
/// One binding and every place it is mentioned. A rename rewrites exactly
/// <see cref="References"/> - which is the whole point of resolving scopes rather than matching
/// text: two locals in different functions can share a name, and a local can shadow a global.
/// </summary>
public sealed class LuaSymbol
{
    public required string Name { get; init; }
    public required LuaSymbolKind Kind { get; init; }

    /// <summary>Where the binding is introduced; null for globals, which are never declared.</summary>
    public LuaSpan? Definition { get; init; }

    /// <summary>Every occurrence, the declaration included for locals, in source order.</summary>
    public List<LuaSpan> References { get; } = [];

    public bool IsGlobal => Kind == LuaSymbolKind.Global;

    /// <summary>What this binding was initialised from, when it was. Completion infers a local's
    /// type from it - `local m = moby_handle:new(1)` is the only thing that makes `m:` meaningful,
    /// since the sources never declare types.</summary>
    public LuaExpr? Initializer { get; init; }

    /// <summary>The block this binding lives in, used to tell whether it is visible at a position.
    /// Opaque on purpose: the scope tree is the resolver's business, not a consumer's.</summary>
    internal LuaScopeRegion? Scope { get; init; }

    /// <summary>Whether renaming this is safe to do from one file. A global is referenced by other
    /// scripts and by the engine, so the editor must not quietly rewrite it.</summary>
    public bool IsRenameSafe => !IsGlobal;

    public override string ToString() => $"{Kind} {Name} ({References.Count} ref)";
}

/// <summary>Resolved names for one parsed file.</summary>
public sealed class LuaScopeAnalysis
{
    public required List<LuaSymbol> Locals { get; init; }
    public required Dictionary<string, LuaSymbol> Globals { get; init; }

    public IEnumerable<LuaSymbol> AllSymbols => Locals.Concat(Globals.Values);

    /// <summary>The symbol mentioned at a position, or null. What F2 asks.</summary>
    public LuaSymbol? FindAt(LuaPosition position) =>
        AllSymbols.FirstOrDefault(s => s.References.Any(r => r.Contains(position)));

    /// <summary>Locals visible at a position, innermost first - the local half of completion.
    /// A binding declared later in the file is not in scope yet, so it is excluded.</summary>
    public IEnumerable<LuaSymbol> VisibleAt(LuaPosition position) =>
        Locals.Where(s => s.Scope?.Span.Contains(position) == true
                       && s.Definition is { } d
                       && (d.Start.Line < position.Line
                           || (d.Start.Line == position.Line && d.Start.Column <= position.Column)))
              .Reverse();
}

/// <summary>
/// Binds every identifier in a parsed file to the local it refers to, or records it as a global.
///
/// Lua's scoping has four rules that a naive walker gets wrong, and each one changes what a rename
/// would rewrite:
///   - a local is in scope only AFTER its statement, so <c>local x = x</c> initialises from the
///     outer x, and the two xs are different symbols;
///   - <c>local function f</c> is in scope inside its own body, so recursion resolves;
///   - <c>repeat ... until cond</c> lets the condition see the body's locals;
///   - a second <c>local x</c> in one block shadows the first from that point on, so later
///     references belong to the newer binding.
/// </summary>
public static class LuaScopeResolver
{
    public static LuaScopeAnalysis Resolve(LuaBlock chunk)
    {
        var state = new State();
        // The file's own chunk is a function body: a vararg one, and its locals are not globals.
        state.PushScope(chunk.Span);
        state.VisitBlock(chunk);
        state.PopScope();
        return new LuaScopeAnalysis { Locals = state.Locals, Globals = state.Globals };
    }

    private sealed class Scope(LuaSpan span, Scope? parent)
    {
        public LuaSpan Span { get; } = span;
        public LuaScopeRegion Region { get; } = new(span);
        public Scope? Parent { get; } = parent;
        /// <summary>Declared in order; a later entry of the same name shadows an earlier one, so
        /// lookups walk backwards.</summary>
        public List<LuaSymbol> Declared { get; } = [];
    }

    private sealed class State
    {
        public List<LuaSymbol> Locals { get; } = [];
        public Dictionary<string, LuaSymbol> Globals { get; } = [];
        private Scope? _scope;

        public void PushScope(LuaSpan span) => _scope = new Scope(span, _scope);
        public void PopScope() => _scope = _scope?.Parent;

        private LuaSymbol Declare(LuaNameDefinition name, LuaSymbolKind kind, LuaExpr? initializer = null)
        {
            var symbol = new LuaSymbol
            {
                Name = name.Name, Kind = kind, Definition = name.Span,
                Scope = _scope?.Region, Initializer = initializer,
            };
            symbol.References.Add(name.Span);
            _scope?.Declared.Add(symbol);
            Locals.Add(symbol);
            return symbol;
        }

        private void Reference(string name, LuaSpan span)
        {
            // Innermost scope outwards, newest declaration first, so shadowing resolves correctly.
            for (var scope = _scope; scope != null; scope = scope.Parent)
                for (int i = scope.Declared.Count - 1; i >= 0; i--)
                    if (scope.Declared[i].Name == name)
                    {
                        scope.Declared[i].References.Add(span);
                        return;
                    }

            if (!Globals.TryGetValue(name, out var global))
                Globals[name] = global = new LuaSymbol { Name = name, Kind = LuaSymbolKind.Global };
            global.References.Add(span);
        }

        public void VisitBlock(LuaBlock block)
        {
            foreach (var statement in block.Statements) VisitStat(statement);
        }

        private void VisitScopedBlock(LuaBlock block)
        {
            PushScope(block.Span);
            VisitBlock(block);
            PopScope();
        }

        private void VisitStat(LuaStat statement)
        {
            switch (statement)
            {
                case LuaLocalStat s:
                    // Values first: the new bindings are not visible to their own initialisers.
                    foreach (var value in s.Values) VisitExpr(value);
                    for (int i = 0; i < s.Names.Count; i++)
                        // Positional pairing, as Lua assigns them. A name past the value list has no
                        // single initialiser (it may take an extra return from the last call), so it
                        // gets none rather than a wrong one.
                        Declare(s.Names[i], LuaSymbolKind.Local, i < s.Values.Count ? s.Values[i] : null);
                    break;

                case LuaLocalFunctionStat s:
                    // Declared before the body so the function can call itself.
                    Declare(s.Name, LuaSymbolKind.Local, s.Function);
                    VisitFunction(s.Function);
                    break;

                case LuaFunctionStat s:
                    VisitExpr(s.Target);
                    VisitFunction(s.Function);
                    break;

                case LuaAssignStat s:
                    foreach (var value in s.Values) VisitExpr(value);
                    foreach (var target in s.Targets) VisitExpr(target);
                    break;

                case LuaCallStat s: VisitExpr(s.Call); break;
                case LuaDoStat s: VisitScopedBlock(s.Body); break;

                case LuaWhileStat s:
                    VisitExpr(s.Condition);
                    VisitScopedBlock(s.Body);
                    break;

                case LuaRepeatStat s:
                    // One scope spanning body and condition: `until` can read the body's locals.
                    PushScope(s.Span);
                    VisitBlock(s.Body);
                    VisitExpr(s.Condition);
                    PopScope();
                    break;

                case LuaIfStat s:
                    foreach (var clause in s.Clauses)
                    {
                        VisitExpr(clause.Condition);
                        VisitScopedBlock(clause.Body);
                    }
                    if (s.ElseBody is { } elseBody) VisitScopedBlock(elseBody);
                    break;

                case LuaNumericForStat s:
                    // Bounds are evaluated outside; the control variable belongs to the body.
                    VisitExpr(s.Start);
                    VisitExpr(s.Limit);
                    if (s.Step is { } step) VisitExpr(step);
                    // The statement's span, not the body's: while the body is being typed it has
                    // no terminator and stops at the last token, which would put the caret outside.
                    PushScope(s.Span);
                    Declare(s.Variable, LuaSymbolKind.LoopVariable);
                    VisitBlock(s.Body);
                    PopScope();
                    break;

                case LuaGenericForStat s:
                    foreach (var e in s.Expressions) VisitExpr(e);
                    PushScope(s.Span);
                    foreach (var name in s.Names) Declare(name, LuaSymbolKind.LoopVariable);
                    VisitBlock(s.Body);
                    PopScope();
                    break;

                case LuaReturnStat s:
                    foreach (var value in s.Values) VisitExpr(value);
                    break;

                case LuaBreakStat or LuaErrorStat: break;
            }
        }

        private void VisitFunction(LuaFunctionExpr function)
        {
            PushScope(function.Span);
            foreach (var parameter in function.Parameters) Declare(parameter, LuaSymbolKind.Parameter);
            VisitBlock(function.Body);
            PopScope();
        }

        private void VisitExpr(LuaExpr expression)
        {
            switch (expression)
            {
                case LuaNameExpr e: Reference(e.Name, e.Span); break;

                // Only the target is a name: `a.b` never references a binding called b, which is why
                // a dotted key is held as a string and not visited as an expression.
                case LuaIndexExpr e:
                    VisitExpr(e.Target);
                    if (!e.Dotted) VisitExpr(e.Key);
                    break;

                case LuaCallExpr e:
                    VisitExpr(e.Target);
                    foreach (var a in e.Arguments) VisitExpr(a);
                    break;

                case LuaMethodCallExpr e:
                    VisitExpr(e.Receiver);
                    foreach (var a in e.Arguments) VisitExpr(a);
                    break;

                case LuaFunctionExpr e: VisitFunction(e); break;

                case LuaTableExpr e:
                    foreach (var item in e.Items)
                    {
                        // A `name =` key is a field, not a reference, same as a dotted index.
                        if (item.Key is { } key && !item.KeyIsName) VisitExpr(key);
                        VisitExpr(item.Value);
                    }
                    break;

                case LuaBinaryExpr e: VisitExpr(e.Left); VisitExpr(e.Right); break;
                case LuaUnaryExpr e: VisitExpr(e.Operand); break;
                case LuaParenExpr e: VisitExpr(e.Inner); break;
            }
        }
    }
}
