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
public sealed class LuaTypeInference(LuaApiDatabase api, LuaScopeAnalysis scopes)
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
                return api.Functions.TryGetValue(target.Name, out var function) ? FirstReturn(function) : null;

            // string.find(...) and the like: a call on a dotted path.
            case LuaCallExpr { Target: LuaIndexExpr { Key: LuaStringExpr key } index }:
            {
                string? owner = TypeOf(index.Target, depth + 1);
                return owner is null ? null : FirstReturn(FindMethod(owner, key.Value));
            }

            case LuaIndexExpr { Dotted: true, Key: LuaStringExpr field } dotted:
            {
                string? owner = TypeOf(dotted.Target, depth + 1);
                if (owner is null) return null;
                // A field, or a dot-called method on the type.
                var match = api.FieldsOf(owner).FirstOrDefault(f => f.Name == field.Value);
                return match is not null ? api.ResolveAlias(match.Type)
                     : FirstReturn(FindMethod(owner, field.Value));
            }

            default: return null;
        }
    }

    private string? TypeOfName(LuaNameExpr name, int depth)
    {
        // A local: follow what it was assigned, one hop.
        var symbol = scopes.Locals.FirstOrDefault(s => s.References.Any(r => r == name.Span));
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

    private LuaApiFunction? FindMethod(string type, string name) =>
        api.MethodsOf(api.ResolveAlias(type)).FirstOrDefault(m => m.Name == name);

    /// <summary>A function's first return type - what an assignment from a call receives.</summary>
    private string? FirstReturn(LuaApiFunction? function)
    {
        string? type = function?.Returns.FirstOrDefault()?.Type;
        if (type is null) return null;
        // `Moby|nil` is the common documented shape for a lookup; completing on the useful half is
        // more helpful than refusing because the union is not a single class.
        string first = type.Split('|', StringSplitOptions.TrimEntries)
                           .FirstOrDefault(t => t != "nil") ?? type;
        return api.ResolveAlias(first.TrimEnd('?'));
    }
}
