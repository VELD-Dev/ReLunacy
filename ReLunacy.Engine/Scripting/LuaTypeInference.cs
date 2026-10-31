namespace ReLunacy.Engine.Scripting;

/// <summary>
/// Works out the documented type of an expression, so member completion after <c>obj:</c> knows
/// what to offer.
///
/// It is deliberately shallow. The sources carry no type information at all - these games' Lua is
/// untyped and the engine's API is undeclared - so every answer comes from either a literal or the
/// documentation database, following assignments one hop at a time. Where nothing says what
/// something is, the answer is null and completion falls back to offering globals rather than
/// guessing members.
/// </summary>
public sealed class LuaTypeInference(
    LuaApiDatabase api, LuaScopeAnalysis scopes, LuaScriptIndex? scripts = null,
    LuaTableShapes? shapes = null, IReadOnlyList<LuaCast>? casts = null)
{
    // Assignment chains can be circular (`local a = b` / `local b = a`), so recursion is bounded.
    private const int MaxDepth = 8;

    /// <summary>The type name of an expression, or null when nothing documents it.</summary>
    public string? TypeOf(LuaExpr? expression, int depth = 0)
    {
        if (expression is null || depth > MaxDepth) return null;

        switch (expression)
        {
            case LuaStringExpr: return "string";
            case LuaNumberExpr: return "number";
            case LuaBoolExpr: return "boolean";
            case LuaTableExpr: return "table";
            case LuaFunctionExpr: return "function";
            case LuaParenExpr paren: return TypeOf(paren.Inner, depth + 1);

            case LuaNameExpr name: return TypeOfName(name, depth);

            case LuaMethodCallExpr call:
            {
                string? receiver = TypeOf(call.Receiver, depth + 1);
                return receiver is null ? null : FirstReturn(FindMethod(receiver, call.Method));
            }

            case LuaCallExpr { Target: LuaNameExpr target }:
                // The documentation first, then the scripts' own declarations: a `---@return Moby`
                // written above a function in the file is what makes `local m = find_moby()` offer
                // Moby's members afterwards.
                if (api.Functions.TryGetValue(target.Name, out var function)) return FirstReturn(function);
                return FirstReturn(scripts?.Find(target.Name)?.Function);

            // string.find(...) and the like: a call on a dotted path.
            case LuaCallExpr { Target: LuaIndexExpr { Key: LuaStringExpr key } index }:
            {
                string? owner = TypeOf(index.Target, depth + 1);
                return owner is null ? null : FirstReturn(FindMethod(owner, key.Value));
            }

            case LuaIndexExpr { Dotted: true, Key: LuaStringExpr field } dotted:
            {
                string? owner = TypeOf(dotted.Target, depth + 1);
                if (owner is not null)
                {
                    // A field, or a dot-called method on the type.
                    var match = api.FieldsOf(owner).FirstOrDefault(f => f.Name == field.Value);
                    if (match is not null) return api.ResolveAlias(match.Type);
                    if (FirstReturn(FindMethod(owner, field.Value)) is { } returned) return returned;
                }

                // Nothing documented says what it is, so try a table the script declared: this is
                // what carries a documented handle through a key, making `state.boss:` work after
                // `state.boss = moby_handle:new(7)`.
                return TypeOfShapeMember(dotted, field.Value, depth);
            }

            default: return null;
        }
    }

    private string? TypeOfName(LuaNameExpr name, int depth)
    {
        var symbol = scopes.Locals.FirstOrDefault(s => s.References.Any(r => r == name.Span));

        // A cast outranks everything: it is someone stating what they know about an API that
        // documents nothing, which is more than anything here can work out for itself.
        if (Cast(symbol, name.Name, name.Span.Start) is { } cast) return cast;

        // A local: follow what it was assigned, one hop.
        if (symbol is { Initializer: { } initializer }) return TypeOf(initializer, depth + 1);
        if (symbol is not null) return null; // a local with no initialiser says nothing

        // A documented global variable.
        if (api.Variables.TryGetValue(name.Name, out var variable)) return api.ResolveAlias(variable.Type);

        // A carrier: `moby_handle` holds the class, so `moby_handle:new` is valid. Distinguishing
        // the class object from an instance of it would need static-vs-instance tracking the
        // annotations do not express, so both resolve to the class.
        if (api.Carriers.TryGetValue(name.Name, out string? carried)) return carried;
        if (api.Types.ContainsKey(name.Name)) return name.Name;
        return null;
    }

    /// <summary>
    /// The type of a key of a declared table, by typing whatever that key was assigned. The path
    /// is rebuilt from the expression rather than passed in, because inference is also handed
    /// reconstructed nodes from a half-typed line.
    /// </summary>
    private string? TypeOfShapeMember(LuaIndexExpr expression, string key, int depth)
    {
        if (shapes is null) return null;

        var keys = new List<string>();
        LuaExpr current = expression.Target;
        while (current is LuaIndexExpr index)
        {
            if (index.Key is not LuaStringExpr literal) return null;
            keys.Insert(0, literal.Value);
            current = index.Target;
        }
        if (current is not LuaNameExpr root) return null;

        var shape = shapes.For(root.Name, root.Span.Start, keys);
        return shape?.Find(key) is { Value: { } assigned } ? TypeOf(assigned, depth + 1) : null;
    }

    /// <summary>
    /// A binding's type at a position: what a cast says, or what it was initialised from.
    ///
    /// Takes the position because a cast only applies after it is written - the same name can be
    /// one thing above the cast and another below it, which is the point of casting.
    /// </summary>
    public string? TypeOfSymbolAt(LuaSymbol symbol, LuaPosition at) =>
        Cast(symbol, symbol.Name, at) ?? TypeOf(symbol.Initializer);

    /// <summary>
    /// The nearest cast above a position for this name, or null.
    ///
    /// Matched by binding when both ends resolved to one, and by name otherwise - the fallback
    /// being for a half-typed line, which does not parse and so has no binding to resolve against.
    /// </summary>
    /// <summary>
    /// The type a cast establishes at a position, already reduced to something members can be
    /// looked up on - or null when no cast applies, or when the one that does only subtracts.
    /// </summary>
    private string? Cast(LuaSymbol? symbol, string name, LuaPosition at)
    {
        if (CastFor(symbol, name, at) is not { } written) return null;

        // `-X` removes X from the union rather than replacing it: `---@cast m -nil` says m is no
        // longer nil, and what it IS stays whatever it was. Since nil-ness is not tracked here,
        // the honest answer is the type the binding already had, so the cast is stood down.
        if (written.StartsWith('-')) return null;

        return NarrowType(written);
    }

    /// <summary>
    /// A type expression reduced to the single class a lookup can be done against: a cast's
    /// leading <c>+</c> dropped, a union's first non-nil member taken, an optional's <c>?</c>
    /// removed.
    ///
    /// Shared with return types, which needed exactly the same reduction - <c>Moby|nil</c> is the
    /// common documented shape for a lookup, and completing on the useful half is more helpful
    /// than refusing because the union is not a single class.
    /// </summary>
    private string NarrowType(string type)
    {
        string text = type.Trim();
        if (text.StartsWith('+')) text = text[1..];
        string first = text.Split('|', StringSplitOptions.TrimEntries)
                           .FirstOrDefault(t => t != "nil") ?? text;
        return api.ResolveAlias(first.TrimEnd('?'));
    }

    private string? CastFor(LuaSymbol? symbol, string name, LuaPosition at)
    {
        string? type = null;
        int nearest = -1;
        foreach (var cast in casts ?? [])
        {
            if (cast.At.Line >= at.Line || cast.At.Line <= nearest) continue;
            bool matches = symbol is not null && cast.Binding is not null
                ? ReferenceEquals(cast.Binding, symbol)
                : cast.Name == name;
            if (!matches) continue;
            type = cast.Type;
            nearest = cast.At.Line;
        }
        return type;
    }

    private LuaApiFunction? FindMethod(string type, string name) =>
        api.MethodsOf(api.ResolveAlias(type)).FirstOrDefault(m => m.Name == name);

    /// <summary>A function's first return type - what an assignment from a call receives.</summary>
    private string? FirstReturn(LuaApiFunction? function)
    {
        string? type = function?.Returns.FirstOrDefault()?.Type;
        return type is null ? null : NarrowType(type);
    }
}
