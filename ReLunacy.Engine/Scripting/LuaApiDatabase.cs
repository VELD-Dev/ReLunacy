namespace ReLunacy.Engine.Scripting;

/// <summary>A documented parameter. <see cref="Optional"/> comes from LuaLS's <c>name?</c>.</summary>
public sealed record LuaApiParameter(string Name, string Type, string? Description, bool Optional)
{
    public override string ToString() => $"{Name}{(Optional ? "?" : "")}: {Type}";
}

public sealed record LuaApiReturn(string Type, string? Description)
{
    public override string ToString() => Type;
}

/// <summary>A documented function: a global, or a method on a class.</summary>
public sealed record LuaApiFunction(
    string Name,
    List<LuaApiParameter> Parameters,
    List<LuaApiReturn> Returns,
    string? Description,
    string? OwnerType,
    bool IsMethod,
    string SourceFile,
    LuaPosition SourcePosition)
{
    /// <summary>How the popup labels it: <c>skydive_spawn_missile(target: Moby, angle: number): Missile</c>.</summary>
    public string Signature =>
        $"{(OwnerType is null ? "" : OwnerType + (IsMethod ? ":" : "."))}{Name}"
        + $"({string.Join(", ", Parameters)})"
        + (Returns.Count == 0 ? "" : ": " + string.Join(", ", Returns));

    public override string ToString() => Signature;
}

public sealed record LuaApiField(string Name, string Type, string? Description);

/// <summary>A documented table shape - what <c>---@class</c> declares. These are the object types
/// the engine hands to Lua: the corpus never defines a method in Lua, so without these the editor
/// has nothing to say about <c>obj:</c>.</summary>
public sealed record LuaApiType(
    string Name,
    string? BaseType,
    List<LuaApiField> Fields,
    List<LuaApiFunction> Methods,
    string? Description,
    string SourceFile);

/// <summary>A documented global that is not a function - <c>Ratchet1</c>, <c>DialogueSpecs</c>.</summary>
public sealed record LuaApiVariable(string Name, string Type, string? Description, string SourceFile);

/// <summary>A problem in the documentation files themselves, surfaced so the person writing them
/// finds out from the editor rather than from completion quietly saying nothing.</summary>
public readonly record struct LuaDocDiagnostic(string Message, string SourceFile, LuaPosition Position)
{
    public override string ToString() => $"{SourceFile}:{Position}: {Message}";
}

/// <summary>
/// The documented API surface: global functions, global variables, class types and aliases, loaded
/// from user-editable LuaLS annotation files.
///
/// None of this can be derived from the scripts. The engine exposes functions and object types that
/// Lua only ever calls, never defines - 35,490 method calls in the shipped bytecode against zero
/// method definitions in the sources - so the types, parameter names, return shapes and descriptions
/// exist only in whoever reverse-engineered the EBOOT. This is where that knowledge lives.
/// </summary>
public sealed class LuaApiDatabase
{
    private readonly Dictionary<string, LuaApiFunction> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LuaApiType> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LuaApiVariable> _variables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _carriers = new(StringComparer.Ordinal);
    private readonly List<LuaDocDiagnostic> _diagnostics = [];

    public IReadOnlyDictionary<string, LuaApiFunction> Functions => _functions;
    public IReadOnlyDictionary<string, LuaApiType> Types => _types;
    public IReadOnlyDictionary<string, LuaApiVariable> Variables => _variables;
    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    /// <summary>Variables that hold a documented class, mapped to its name. LuaLS's convention
    /// separates the two - <c>---@class stringlib</c> above <c>string = {}</c> - so
    /// <c>function string.len</c> has to resolve `string` to `stringlib` before it can be attached.</summary>
    public IReadOnlyDictionary<string, string> Carriers => _carriers;
    public IReadOnlyList<LuaDocDiagnostic> Diagnostics => _diagnostics;

    public bool IsEmpty => _functions.Count == 0 && _types.Count == 0 && _variables.Count == 0;

    internal void Add(LuaApiFunction function)
    {
        if (function.OwnerType is { } declaredOwner)
        {
            // The owner as written may be a carrier variable rather than the class name.
            string owner = _carriers.TryGetValue(declaredOwner, out string? carried) ? carried : declaredOwner;
            function = function with { OwnerType = owner };
            // A method on a class the file has not declared is almost always a typo in the owner
            // name, and silently creating the class would hide it.
            if (!_types.TryGetValue(owner, out var type))
            {
                _diagnostics.Add(new LuaDocDiagnostic(
                    $"method '{function.Name}' is declared on unknown class '{owner}'",
                    function.SourceFile, function.SourcePosition));
                return;
            }
            if (type.Methods.Any(m => m.Name == function.Name))
                _diagnostics.Add(new LuaDocDiagnostic(
                    $"'{owner}:{function.Name}' is documented more than once",
                    function.SourceFile, function.SourcePosition));
            else type.Methods.Add(function);
            return;
        }

        if (_functions.TryGetValue(function.Name, out var existing))
        {
            _diagnostics.Add(new LuaDocDiagnostic(
                $"'{function.Name}' is already documented in {existing.SourceFile}",
                function.SourceFile, function.SourcePosition));
            return;
        }
        _functions[function.Name] = function;
    }

    internal void Add(LuaApiType type)
    {
        if (_types.ContainsKey(type.Name))
        {
            _diagnostics.Add(new LuaDocDiagnostic($"class '{type.Name}' is declared more than once",
                                                  type.SourceFile, default));
            return;
        }
        _types[type.Name] = type;
    }

    internal void Add(LuaApiVariable variable) => _variables[variable.Name] = variable;
    internal void AddCarrier(string variable, string className) => _carriers[variable] = className;
    internal void AddAlias(string name, string target) => _aliases[name] = target;
    internal void AddDiagnostic(LuaDocDiagnostic diagnostic) => _diagnostics.Add(diagnostic);

    /// <summary>Follows aliases to the underlying type name.</summary>
    public string ResolveAlias(string type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (_aliases.TryGetValue(type, out string? target) && seen.Add(type)) type = target;
        return type;
    }

    /// <summary>A class plus everything it inherits, base-most last.</summary>
    public IEnumerable<LuaApiType> TypeHierarchy(string name)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? current = ResolveAlias(name);
        while (current != null && seen.Add(current) && _types.TryGetValue(current, out var type))
        {
            yield return type;
            current = type.BaseType is null ? null : ResolveAlias(type.BaseType);
        }
    }

    /// <summary>Members visible on a class, inherited ones included. What <c>obj:</c> offers.</summary>
    public IEnumerable<LuaApiFunction> MethodsOf(string typeName) =>
        TypeHierarchy(typeName).SelectMany(t => t.Methods);

    public IEnumerable<LuaApiField> FieldsOf(string typeName) =>
        TypeHierarchy(typeName).SelectMany(t => t.Fields);

    /// <summary>Checks that every type mentioned actually exists, so a misspelled class in a
    /// freshly written annotation is reported rather than silently producing no completions.</summary>
    internal void ValidateTypeReferences()
    {
        void Check(string type, string where, string file, LuaPosition at)
        {
            foreach (string part in SplitTypeExpression(type))
            {
                if (IsBuiltinType(part) || _types.ContainsKey(part) || _aliases.ContainsKey(part)) continue;
                _diagnostics.Add(new LuaDocDiagnostic($"{where} refers to unknown type '{part}'", file, at));
            }
        }

        foreach (var f in _functions.Values)
        {
            foreach (var p in f.Parameters) Check(p.Type, $"parameter '{p.Name}' of '{f.Name}'", f.SourceFile, f.SourcePosition);
            foreach (var r in f.Returns) Check(r.Type, $"return of '{f.Name}'", f.SourceFile, f.SourcePosition);
        }
        foreach (var t in _types.Values)
        {
            if (t.BaseType is { } b) Check(b, $"base of class '{t.Name}'", t.SourceFile, default);
            foreach (var field in t.Fields) Check(field.Type, $"field '{t.Name}.{field.Name}'", t.SourceFile, default);
            foreach (var m in t.Methods)
            {
                foreach (var p in m.Parameters) Check(p.Type, $"parameter '{p.Name}' of '{t.Name}:{m.Name}'", m.SourceFile, m.SourcePosition);
                foreach (var r in m.Returns) Check(r.Type, $"return of '{t.Name}:{m.Name}'", m.SourceFile, m.SourcePosition);
            }
        }
        foreach (var v in _variables.Values) Check(v.Type, $"variable '{v.Name}'", v.SourceFile, default);
    }

    /// <summary>Lua's own types plus LuaLS's wildcards. Anything else has to be declared.</summary>
    public static bool IsBuiltinType(string type) => type is
        "nil" or "boolean" or "number" or "string" or "table" or "function" or "thread" or "userdata"
        or "any" or "unknown" or "self" or "integer" or "void" or "";

    /// <summary>Breaks a type expression into the names it mentions, so <c>Moby[]|nil</c> checks both
    /// parts and <c>fun(a: Moby): boolean</c> checks Moby and boolean.</summary>
    public static IEnumerable<string> SplitTypeExpression(string type)
    {
        if (string.IsNullOrWhiteSpace(type)) yield break;
        // A function type's own structure is not validated here - only the names inside it.
        var buffer = new System.Text.StringBuilder();
        foreach (char c in type)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.') buffer.Append(c);
            else
            {
                if (buffer.Length > 0) { yield return Strip(buffer.ToString()); buffer.Clear(); }
            }
        }
        if (buffer.Length > 0) yield return Strip(buffer.ToString());

        // "fun" and parameter names inside a function type are syntax, not type references.
        static string Strip(string s) => s is "fun" ? "" : s;
    }
}
