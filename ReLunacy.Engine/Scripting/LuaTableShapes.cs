namespace ReLunacy.Engine.Scripting;

/// <summary>
/// One key of a table a script declares.
/// </summary>
/// <param name="Name">The key.</param>
/// <param name="IsMethod">Declared with <c>:</c>, so it expects a receiver.</param>
/// <param name="Value">What it was assigned, when that is known - kept as the expression rather
/// than a type name so the existing inference can type it, which is what makes a member holding a
/// documented handle behave like one.</param>
/// <param name="Function">Its parameters, when it is a function.</param>
public sealed record LuaTableMember(
    string Name, bool IsMethod, LuaExpr? Value, LuaFunctionExpr? Function, LuaSpan Span)
{
    /// <summary>A signature for a function member, or null. <c>self</c> is left off a method, the
    /// same way the documented API writes them.</summary>
    public string? Signature => Function is null ? null
        : $"{Name}({string.Join(", ", Function.Parameters.Select(p => p.Name))}"
        + $"{(Function.IsVararg ? (Function.Parameters.Count > 0 ? ", ..." : "...") : "")})";
}

/// <summary>The keys of one table.</summary>
public sealed class LuaTableShape(string path)
{
    private readonly Dictionary<string, LuaTableMember> _members = [];

    /// <summary>The dotted path that reaches this table, for display.</summary>
    public string Path { get; } = path;

    public IReadOnlyCollection<LuaTableMember> Members => _members.Values;
    public LuaTableMember? Find(string name) => _members.GetValueOrDefault(name);

    /// <summary>
    /// Adds or replaces a key. Later wins, because later is what runs: a table built with
    /// <c>M = { foo = nil }</c> and then filled in by <c>function M.foo()</c> should offer the
    /// function, not the placeholder.
    /// </summary>
    internal void Add(LuaTableMember member) => _members[member.Name] = member;
}

/// <summary>
/// The shapes of tables a script declares, so completion can offer the keys of a table the person
/// wrote rather than only those of a documented type.
///
/// This exists because of how these scripts are actually written. The engine's API is documented,
/// but a level's own script builds its state in plain tables - and usually not in one place:
///
///     local level = { stage = 1 }
///     function level.advance() end
///     function level:reset() end
///     level.boss = moby_handle:new(7)
///
/// All four lines contribute to what <c>level.</c> should offer, so a shape is accumulated over the
/// whole file rather than read off a constructor. Nested constructors are registered under their
/// own path, which is what makes <c>level.timers.</c> work.
///
/// Roots are keyed by BINDING, not by name: a resolved local is held by reference and a global by
/// name, so two functions each with a <c>local state = {}</c> do not pool their keys. The lookup
/// side resolves through the same scope analysis, so both agree by construction.
///
/// What it does not do is follow a table through an assignment - <c>local alias = level</c> leaves
/// <c>alias.</c> empty - or know anything a key is given at run time. Both would need to track
/// identity through expressions, which is a different kind of analysis from reading declarations.
/// </summary>
public sealed class LuaTableShapes
{
    /// <summary>What a path hangs off: a local binding by reference, or a global by name.</summary>
    private readonly record struct Root(LuaSymbol? Local, string Name);

    private readonly Dictionary<(Root Root, string Path), LuaTableShape> _shapes = [];
    private readonly LuaScopeAnalysis _scopes;

    private LuaTableShapes(LuaScopeAnalysis scopes) => _scopes = scopes;

    public int Count => _shapes.Count;

    /// <summary>Every shape found, for diagnostics.</summary>
    public IEnumerable<LuaTableShape> All => _shapes.Values;

    public static LuaTableShapes Build(LuaBlock block, LuaScopeAnalysis scopes)
    {
        var shapes = new LuaTableShapes(scopes);
        shapes.Walk(block);
        return shapes;
    }

    /// <summary>
    /// The shape reached by a name and a dotted tail - <c>("level", ["timers"])</c> for
    /// <c>level.timers</c>. <paramref name="nameAt"/> is where the name is written, so the binding
    /// it refers to can be resolved rather than guessed.
    /// </summary>
    public LuaTableShape? For(string name, LuaPosition nameAt, IReadOnlyList<string> tail)
    {
        var root = RootFor(name, nameAt);
        return _shapes.GetValueOrDefault((root, string.Join('.', tail)));
    }

    /// <summary>
    /// Which binding a name refers to, as a key.
    ///
    /// Resolving by position comes first, but it cannot be the only way: completion runs on a line
    /// that does not parse yet - <c>level.</c> on its own is not an expression - so the reference
    /// is not in the tree to be found. Falling back to the binding of that name VISIBLE at the
    /// position covers it, and visibility is still per scope, so two tables with the same name in
    /// different functions stay apart.
    /// </summary>
    private Root RootFor(string name, LuaPosition at)
    {
        if (_scopes.FindAt(at) is { IsGlobal: false } resolved && resolved.Name == name)
            return new Root(resolved, name);
        if (_scopes.VisibleAt(at).FirstOrDefault(s => s.Name == name) is { } visible)
            return new Root(visible, name);
        return new Root(null, name);
    }

    private LuaTableShape ShapeAt(Root root, string path, string display)
    {
        if (_shapes.TryGetValue((root, path), out var existing)) return existing;
        return _shapes[(root, path)] = new LuaTableShape(display);
    }

    // --- building -------------------------------------------------------------------------------

    private void Walk(LuaNode node)
    {
        switch (node)
        {
            case LuaLocalStat local:
                // `local a, b = {...}, {...}` pairs up positionally, as the language does.
                for (int i = 0; i < local.Names.Count && i < local.Values.Count; i++)
                    if (local.Values[i] is LuaTableExpr table)
                        Register(RootFor(local.Names[i].Name, local.Names[i].Span.Start),
                                 "", local.Names[i].Name, table);
                break;

            case LuaAssignStat assign:
                for (int i = 0; i < assign.Targets.Count; i++)
                {
                    var value = i < assign.Values.Count ? assign.Values[i] : null;
                    AssignTo(assign.Targets[i], value);
                }
                break;

            case LuaFunctionStat function:
                // `function M.foo()` and `function M:foo()` both add a key to M.
                if (PathOf(function.Target) is var (root, path, display) && path.Count > 0)
                    ShapeAt(root, Join(path[..^1]), Display(display, path[..^1]))
                        .Add(new LuaTableMember(path[^1], function.IsMethod, function.Function,
                                                function.Function, function.Span));
                break;
        }

        foreach (var child in LuaAstWalker.Children(node)) Walk(child);
    }

    /// <summary>
    /// An assignment to a path: <c>M.x = 1</c> adds a key, and when the value is a constructor the
    /// table it builds is registered under that path too, so <c>M.x.</c> works as well.
    /// </summary>
    private void AssignTo(LuaExpr target, LuaExpr? value)
    {
        if (target is LuaNameExpr name)
        {
            if (value is LuaTableExpr table)
                Register(RootFor(name.Name, name.Span.Start), "", name.Name, table);
            return;
        }

        if (PathOf(target) is not var (root, path, display) || path.Count == 0) return;

        var owner = ShapeAt(root, Join(path[..^1]), Display(display, path[..^1]));
        owner.Add(new LuaTableMember(path[^1], false, value, value as LuaFunctionExpr, target.Span));
        if (value is LuaTableExpr nested) Register(root, Join(path), Display(display, path), nested);
    }

    /// <summary>Registers a constructor's keys at a path, and recurses into the tables it nests.</summary>
    private void Register(Root root, string path, string display, LuaTableExpr table)
    {
        var shape = ShapeAt(root, path, display);
        foreach (var item in table.Items)
        {
            // Only named keys describe a shape. A positional item or a computed key says how long
            // the table is, not what can be written after a dot.
            string? key = item switch
            {
                { KeyIsName: true, Key: LuaNameExpr named } => named.Name,
                { Key: LuaStringExpr literal } => literal.Value,
                _ => null,
            };
            if (key is null || !IsName(key)) continue;

            shape.Add(new LuaTableMember(key, false, item.Value, item.Value as LuaFunctionExpr, item.Span));
            if (item.Value is LuaTableExpr nested)
                Register(root, path.Length == 0 ? key : $"{path}.{key}", $"{display}.{key}", nested);
        }
    }

    /// <summary>
    /// A dotted path to a table, as its root and the keys after it. Null for anything else -
    /// an index by a variable or a call has no name completion can offer keys under.
    /// </summary>
    private (Root Root, List<string> Path, string Display)? PathOf(LuaExpr expression)
    {
        var keys = new List<string>();
        var current = expression;
        while (current is LuaIndexExpr index)
        {
            string? key = index.Key switch
            {
                LuaStringExpr literal => literal.Value,
                LuaNameExpr named when index.Dotted => named.Name,
                _ => null,
            };
            if (key is null || !IsName(key)) return null;
            keys.Insert(0, key);
            current = index.Target;
        }
        if (current is not LuaNameExpr root) return null;
        return (RootFor(root.Name, root.Span.Start), keys, root.Name);
    }

    private static string Join(IEnumerable<string> keys) => string.Join('.', keys);

    private static string Display(string root, IEnumerable<string> keys) =>
        keys.Aggregate(root, (path, key) => $"{path}.{key}");

    /// <summary>Whether a key can be written after a dot. <c>t["two words"]</c> is a real key but
    /// not one completion can insert, so it is left out rather than offered unusably.</summary>
    private static bool IsName(string key) =>
        key.Length > 0 && (char.IsLetter(key[0]) || key[0] == '_')
        && key.All(c => char.IsLetterOrDigit(c) || c == '_');
}
