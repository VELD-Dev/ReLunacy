namespace ReLunacy.Engine.Scripting;

public enum LuaCompletionKind { Local, Parameter, LoopVariable, Function, Variable, Method, Field, Keyword, Class }

/// <summary>One suggestion. <see cref="Detail"/> is the signature or type shown beside the label,
/// <see cref="Description"/> the documentation body shown under it.</summary>
public sealed record LuaCompletionItem(
    string Label, string InsertText, LuaCompletionKind Kind, string? Detail, string? Description)
{
    public override string ToString() => $"{Kind} {Label}{(Detail is null ? "" : "  " + Detail)}";
}

/// <summary>What the caret is positioned to complete.</summary>
public enum LuaCompletionTrigger
{
    /// <summary>A bare word: locals, globals, keywords.</summary>
    Identifier,
    /// <summary>After <c>.</c> - fields and dot-called functions of the receiver.</summary>
    Member,
    /// <summary>After <c>:</c> - methods of the receiver only.</summary>
    Method,
}

/// <summary>Where completion is happening and what it replaces when accepted.</summary>
public sealed record LuaCompletionContext(
    LuaCompletionTrigger Trigger, string Prefix, LuaPosition ReplaceFrom, LuaPosition ReplaceTo,
    string? ReceiverType, LuaTableShape? ReceiverShape = null);

/// <summary>
/// Turns a caret position into a list of suggestions, from the locals in scope and the documented
/// API.
///
/// The trigger is found by scanning tokens backwards from the caret rather than by parsing at it:
/// while someone is typing, the line is usually not valid Lua, so <c>moby_handle:</c> has no parse
/// at all. The rest of the file still parses - both the lexer and parser recover - which is what
/// supplies the locals and the types behind them.
/// </summary>
public static class LuaCompletionEngine
{
    /// <summary>Lua 5.0's keywords, offered on a bare identifier.</summary>
    private static readonly string[] Keywords =
    [
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
    ];

    public static List<LuaCompletionItem> Complete(
        string source, LuaPosition caret, LuaApiDatabase api, out LuaCompletionContext context,
        LuaScriptIndex? scripts = null)
    {
        var tokens = LuaLexer.Tokenize(source);
        var block = LuaParser.Parse(source, out _);
        var scopes = LuaScopeResolver.Resolve(block);
        var shapes = LuaTableShapes.Build(block, scopes);
        var inference = new LuaTypeInference(api, scopes, scripts, shapes);

        var analyzed = Analyze(tokens, caret, block, inference, shapes);
        context = analyzed;
        var items = new List<LuaCompletionItem>();

        switch (analyzed.Trigger)
        {
            case LuaCompletionTrigger.Method:
                if (analyzed.ReceiverType is { } methodType)
                    foreach (var method in api.MethodsOf(methodType))
                        items.Add(new LuaCompletionItem(method.Name, method.Name, LuaCompletionKind.Method,
                                                        method.Signature, method.Description));
                // A table the script declared: only the keys written with ':', since those are the
                // ones expecting a receiver. A plain function reached through ':' would silently
                // take the table as its first argument.
                AddShapeMembers(items, analyzed.ReceiverShape, inference, methodsOnly: true);
                break;

            case LuaCompletionTrigger.Member:
                if (analyzed.ReceiverType is { } memberType)
                {
                    foreach (var field in api.FieldsOf(memberType))
                        items.Add(new LuaCompletionItem(field.Name, field.Name, LuaCompletionKind.Field,
                                                        field.Type, field.Description));
                    // Dot access also reaches the type's functions, which is how the standard
                    // library is called: string.len(s) rather than s:len().
                    foreach (var method in api.MethodsOf(memberType))
                        items.Add(new LuaCompletionItem(method.Name, method.Name, LuaCompletionKind.Method,
                                                        method.Signature, method.Description));
                }
                AddShapeMembers(items, analyzed.ReceiverShape, inference, methodsOnly: false);
                break;

            case LuaCompletionTrigger.Identifier:
                // Innermost locals first: they are what the person is most likely reaching for, and
                // a local shadowing a global should be the one offered.
                foreach (var local in scopes.VisibleAt(caret))
                {
                    // `local c|` is naming a NEW variable, so the half-typed name is already a
                    // binding by the time the resolver sees it. Suggesting it back is noise, and it
                    // outranks everything useful because it matches the prefix exactly - which,
                    // with Tab accepting before anything else, would replace the name being typed.
                    if (local.Definition is { } definition && definition.Start == analyzed.ReplaceFrom)
                        continue;
                    items.Add(new LuaCompletionItem(local.Name, local.Name, KindOf(local.Kind),
                        inference.TypeOf(local.Initializer) is { } t ? t : local.Kind.ToString().ToLowerInvariant(),
                        null));
                }

                // Functions the scripts declare, before the documented API: when a level's own
                // script declares a name the documentation also uses, the script's is the one that
                // will actually run.
                foreach (var declared in scripts?.CallableByName ?? [])
                {
                    // `function my_f|` is NAMING a function, so the index already holds the
                    // half-typed name by the time completion runs. Offering it back matches the
                    // prefix exactly, which both ranks it first and opens the popup over the
                    // snippet stop being filled in - the same reason a local being declared is
                    // skipped just above.
                    if (declared.NameSpan.Start == analyzed.ReplaceFrom) continue;
                    items.Add(new LuaCompletionItem(
                        declared.Function.Name, declared.Function.Name, LuaCompletionKind.Function,
                        declared.Function.Signature, DescribeOrigin(declared)));
                }

                foreach (var function in api.Functions.Values)
                    items.Add(new LuaCompletionItem(function.Name, function.Name, LuaCompletionKind.Function,
                                                    function.Signature, function.Description));
                foreach (var variable in api.Variables.Values)
                    items.Add(new LuaCompletionItem(variable.Name, variable.Name, LuaCompletionKind.Variable,
                                                    variable.Type, variable.Description));
                // Carriers are how a class is reached by name - moby_handle:new(...).
                foreach (var (carrier, className) in api.Carriers)
                    items.Add(new LuaCompletionItem(carrier, carrier, LuaCompletionKind.Class, className,
                                                    api.Types.TryGetValue(className, out var t) ? t.Description : null));
                foreach (string keyword in Keywords)
                    items.Add(new LuaCompletionItem(keyword, keyword, LuaCompletionKind.Keyword, null, null));

                // Globals the file itself uses but nothing documents. Offered last and marked, so
                // the undocumented API is still reachable while staying visibly undocumented.
                foreach (var global in scopes.Globals.Values)
                    if (!api.Functions.ContainsKey(global.Name) && !api.Variables.ContainsKey(global.Name)
                        && !api.Carriers.ContainsKey(global.Name)
                        && scripts?.Find(global.Name) is null)
                        items.Add(new LuaCompletionItem(global.Name, global.Name, LuaCompletionKind.Variable,
                                                        "undocumented", null));
                break;
        }

        return Filter(items, analyzed.Prefix);
    }

    /// <summary>
    /// The keys of a table the script itself declared, after whatever the documented API had to
    /// say. Anything the API already offered wins, since a documented description beats a key read
    /// off an assignment.
    /// </summary>
    private static void AddShapeMembers(List<LuaCompletionItem> items, LuaTableShape? shape,
                                        LuaTypeInference inference, bool methodsOnly)
    {
        if (shape is null) return;
        foreach (var member in shape.Members)
        {
            if (methodsOnly && !member.IsMethod) continue;
            if (items.Any(i => i.Label == member.Name)) continue;

            string? detail = member.Signature ?? inference.TypeOf(member.Value);
            items.Add(new LuaCompletionItem(
                member.Name, member.Name,
                member.Function is not null
                    ? member.IsMethod ? LuaCompletionKind.Method : LuaCompletionKind.Function
                    : LuaCompletionKind.Field,
                detail, $"a key of {shape.Path}, declared in this script"));
        }
    }

    /// <summary>
    /// What the popup says under a declared function: its own description when it has one, and
    /// where it comes from when that is not the file being edited - a name shared through the
    /// level's global table is worth knowing the origin of.
    /// </summary>
    private static string? DescribeOrigin(LuaScriptFunction declared)
    {
        string? where = declared.Origin == LuaScriptFunctionOrigin.CurrentFile
            ? null
            : $"declared in {Path.GetFileName(declared.Function.SourceFile)}";
        return (declared.Function.Description, where) switch
        {
            ({ } description, { } place) => $"{description}\n\n{place}",
            ({ } description, null) => description,
            (null, { } place) => place,
            _ => null,
        };
    }

    private static LuaCompletionKind KindOf(LuaSymbolKind kind) => kind switch
    {
        LuaSymbolKind.Parameter => LuaCompletionKind.Parameter,
        LuaSymbolKind.LoopVariable => LuaCompletionKind.LoopVariable,
        _ => LuaCompletionKind.Local,
    };

    /// <summary>Prefix match, case-insensitive, with exact-case and start-of-word matches first.
    /// Duplicate labels are collapsed, keeping the first - which is why locals are added first.</summary>
    private static List<LuaCompletionItem> Filter(List<LuaCompletionItem> items, string prefix)
    {
        IEnumerable<LuaCompletionItem> matching = prefix.Length == 0
            ? items
            : items.Where(i => i.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return matching
            .GroupBy(i => i.Label, StringComparer.Ordinal).Select(g => g.First())
            .OrderByDescending(i => prefix.Length > 0 && i.Label.StartsWith(prefix, StringComparison.Ordinal))
            .ThenBy(i => i.Kind == LuaCompletionKind.Keyword ? 1 : 0)
            .ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Scans backwards from the caret for the word being typed and, if there is one, the <c>.</c> or
    /// <c>:</c> before it with the receiver chain behind that.
    /// </summary>
    private static LuaCompletionContext Analyze(
        List<LuaToken> tokens, LuaPosition caret, LuaBlock block, LuaTypeInference inference,
        LuaTableShapes shapes)
    {
        // Tokens strictly before the caret, comments excluded.
        var before = tokens
            .Where(t => t.Kind is not (LuaTokenKind.Comment or LuaTokenKind.EndOfFile))
            .Where(t => t.End.Line < caret.Line || (t.End.Line == caret.Line && t.End.Column <= caret.Column))
            .ToList();

        string prefix = "";
        var replaceFrom = caret;
        int index = before.Count - 1;

        // A name ending exactly at the caret is the word being completed; anything else and the
        // caret is at a fresh position with an empty prefix.
        if (index >= 0 && before[index] is { Kind: LuaTokenKind.Name } word
            && word.End == caret && word.Start.Line == caret.Line)
        {
            prefix = word.Text;
            replaceFrom = word.Start;
            index--;
        }

        if (index >= 0 && before[index].Kind == LuaTokenKind.Symbol && before[index].Text is "." or ":")
        {
            var trigger = before[index].Text == ":" ? LuaCompletionTrigger.Method : LuaCompletionTrigger.Member;
            var chain = ChainBefore(before, index - 1);
            string? receiverType = ResolveReceiver(chain, block, inference);
            var receiverShape = chain.Count > 0
                ? shapes.For(chain[0].Text, chain[0].Start, [.. chain.Skip(1).Select(t => t.Text)])
                : null;
            return new LuaCompletionContext(trigger, prefix, replaceFrom, caret, receiverType, receiverShape);
        }

        return new LuaCompletionContext(LuaCompletionTrigger.Identifier, prefix, replaceFrom, caret, null);
    }

    /// <summary>
    /// Walks back over a <c>a.b.c</c> chain and resolves its type. The head is matched against the
    /// parsed tree by position so a local's inferred type is used; the rest is followed through the
    /// documented members. A head that no longer parses - because the line is mid-edit - still
    /// resolves when it is a documented global, which covers <c>moby_handle:</c>.
    /// </summary>
    private static string? ResolveReceiver(List<LuaToken> chain, LuaBlock block, LuaTypeInference inference)
    {
        if (chain.Count == 0) return null;

        // The head, resolved through the tree where it exists so locals work.
        var head = FindNameExpr(block, chain[0].Start);
        string? type = head is not null
            ? inference.TypeOf(head)
            : inference.TypeOf(new LuaNameExpr(chain[0].Text, new LuaSpan(chain[0].Start, chain[0].End)));

        for (int i = 1; i < chain.Count && type is not null; i++)
            type = inference.TypeOf(new LuaIndexExpr(
                new LuaNameExpr(chain[i - 1].Text, new LuaSpan(chain[i - 1].Start, chain[i - 1].End)),
                new LuaStringExpr(chain[i].Text, new LuaSpan(chain[i].Start, chain[i].End)),
                true, new LuaSpan(chain[0].Start, chain[i].End)));

        return type;
    }

    /// <summary>
    /// The <c>a.b.c</c> chain of names ending at <paramref name="index"/>, in source order. Shared
    /// by type resolution and shape lookup so the two cannot disagree about what the receiver is.
    /// </summary>
    private static List<LuaToken> ChainBefore(List<LuaToken> before, int index)
    {
        var chain = new List<LuaToken>();
        while (index >= 0)
        {
            if (before[index].Kind != LuaTokenKind.Name) break;
            chain.Insert(0, before[index--]);
            if (index >= 0 && before[index].Kind == LuaTokenKind.Symbol && before[index].Text == ".") index--;
            else break;
        }
        return chain;
    }

    /// <summary>The parsed name expression at a position, so inference can use the resolved local
    /// rather than a reconstructed node that belongs to no scope.</summary>
    private static LuaNameExpr? FindNameExpr(LuaNode node, LuaPosition at)
    {
        if (node is LuaNameExpr name && name.Span.Start == at) return name;
        foreach (var child in LuaAstWalker.Children(node))
            if (FindNameExpr(child, at) is { } found) return found;
        return null;
    }
}

/// <summary>Child enumeration for the AST, in one place so walkers agree on its shape.</summary>
public static class LuaAstWalker
{
    public static IEnumerable<LuaNode> Children(LuaNode node) => node switch
    {
        LuaBlock b => b.Statements,
        LuaLocalStat s => s.Names.Cast<LuaNode>().Concat(s.Values),
        LuaLocalFunctionStat s => [s.Name, s.Function],
        LuaFunctionStat s => [s.Target, s.Function],
        LuaAssignStat s => s.Targets.Cast<LuaNode>().Concat(s.Values),
        LuaCallStat s => [s.Call],
        LuaDoStat s => [s.Body],
        LuaWhileStat s => [s.Condition, s.Body],
        LuaRepeatStat s => [s.Body, s.Condition],
        LuaIfStat s => s.Clauses.Cast<LuaNode>().Concat(s.ElseBody is null ? [] : new LuaNode[] { s.ElseBody }),
        LuaIfClause c => [c.Condition, c.Body],
        LuaNumericForStat s => s.Step is null
            ? [s.Variable, s.Start, s.Limit, s.Body]
            : [s.Variable, s.Start, s.Limit, s.Step, s.Body],
        LuaGenericForStat s => s.Names.Cast<LuaNode>().Concat(s.Expressions).Append(s.Body),
        LuaReturnStat s => s.Values,
        LuaFunctionExpr e => e.Parameters.Cast<LuaNode>().Append(e.Body),
        LuaIndexExpr e => [e.Target, e.Key],
        LuaCallExpr e => e.Arguments.Prepend<LuaNode>(e.Target),
        LuaMethodCallExpr e => e.Arguments.Prepend<LuaNode>(e.Receiver),
        LuaTableExpr e => e.Items,
        LuaTableItem i => i.Key is null ? [i.Value] : [i.Key, i.Value],
        LuaBinaryExpr e => [e.Left, e.Right],
        LuaUnaryExpr e => [e.Operand],
        LuaParenExpr e => [e.Inner],
        _ => [],
    };

    /// <summary>The node and every descendant, depth-first.</summary>
    public static IEnumerable<LuaNode> Walk(LuaNode node)
    {
        yield return node;
        foreach (var child in Children(node))
            foreach (var descendant in Walk(child))
                yield return descendant;
    }
}
