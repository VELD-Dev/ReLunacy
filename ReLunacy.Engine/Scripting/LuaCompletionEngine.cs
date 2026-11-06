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
    /// <summary>Inside a string literal that a known function takes a script's name in.</summary>
    StringArgument,
    /// <summary>Inside a LuaLS annotation: a tag, a type, or a parameter name.</summary>
    Annotation,
}

/// <summary>Where completion is happening and what it replaces when accepted.</summary>
public sealed record LuaCompletionContext(
    LuaCompletionTrigger Trigger, string Prefix, LuaPosition ReplaceFrom, LuaPosition ReplaceTo,
    string? ReceiverType, LuaTableShape? ReceiverShape = null,
    LuaScriptNameKind ScriptNames = LuaScriptNameKind.None, string? Function = null,
    LuaAnnotationSuggestion Annotation = LuaAnnotationSuggestion.None, string? Tag = null);

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

    /// <param name="scriptNames">
    /// Names to offer inside a string a function takes a script name in. A hook because only the
    /// host knows what the archive holds - the engine would have to open it to find out, and it has
    /// no business doing that from a completion call.
    /// </param>
    public static List<LuaCompletionItem> Complete(
        string source, LuaPosition caret, LuaApiDatabase api, out LuaCompletionContext context,
        LuaScriptIndex? scripts = null,
        Func<LuaScriptNameKind, IEnumerable<string>>? scriptNames = null)
    {
        var tokens = LuaLexer.Tokenize(source);
        var block = LuaParser.Parse(source, out _);
        var scopes = LuaScopeResolver.Resolve(block);
        var shapes = LuaTableShapes.Build(block, scopes);
        var casts = LuaCasts.Find(source, scopes);
        var inference = new LuaTypeInference(api, scopes, scripts, shapes, casts);

        var analyzed = Analyze(source, tokens, caret, block, inference, shapes);
        context = analyzed;
        var items = new List<LuaCompletionItem>();

        switch (analyzed.Trigger)
        {
            case LuaCompletionTrigger.Annotation:
                AddAnnotationItems(items, analyzed, source, api, scopes, block);
                break;

            case LuaCompletionTrigger.StringArgument:
                foreach (string name in scriptNames?.Invoke(analyzed.ScriptNames) ?? [])
                    items.Add(new LuaCompletionItem(name, name, LuaCompletionKind.Variable, null,
                        string.Format(ScriptNameDescription(analyzed.ScriptNames), analyzed.Function)));
                break;

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
                    // Asked AT the caret, not in general: a ---@cast above it is the whole reason
                    // a binding's type can differ from what it was initialised with, and asking
                    // the initialiser directly answered as though the cast had not been written.
                    items.Add(new LuaCompletionItem(local.Name, local.Name, KindOf(local.Kind),
                        inference.TypeOfSymbolAt(local, caret) is { } t
                            ? t : local.Kind.ToString().ToLowerInvariant(),
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
    /// What to offer inside an annotation: the tags, the types that exist, the parameter names of
    /// the function being documented, or the diagnostic actions.
    ///
    /// The parameter names are the ones that save real typing, and they come from the function
    /// BELOW the annotation - which is what an annotation documents, and the only place those names
    /// are written.
    /// </summary>
    private static void AddAnnotationItems(List<LuaCompletionItem> items,
                                           LuaCompletionContext context, string source,
                                           LuaApiDatabase api, LuaScopeAnalysis scopes,
                                           LuaBlock block)
    {
        switch (context.Annotation)
        {
            case LuaAnnotationSuggestion.Tag:
                foreach (var tag in LuaAnnotations.Tags)
                    items.Add(new LuaCompletionItem(tag.Name, tag.Name, LuaCompletionKind.Keyword,
                        Shape(tag), tag.Interpreted
                            ? tag.Summary
                            : $"{tag.Summary}\n\nRecognised, but this editor does not act on it yet."));
                break;

            case LuaAnnotationSuggestion.Type:
                foreach (string type in LuaAnnotations.BuiltInTypes)
                    items.Add(new LuaCompletionItem(type, type, LuaCompletionKind.Class, "built in", null));
                foreach (var type in api.Types.Values)
                    items.Add(new LuaCompletionItem(type.Name, type.Name, LuaCompletionKind.Class,
                                                    "class", type.Description));
                foreach (string alias in api.Aliases.Keys)
                    items.Add(new LuaCompletionItem(alias, alias, LuaCompletionKind.Class, "alias", null));
                break;

            case LuaAnnotationSuggestion.ParameterName:
                foreach (string name in ParametersBelow(source, context.ReplaceFrom.Line))
                    items.Add(new LuaCompletionItem(name, name, LuaCompletionKind.Parameter,
                                                    "parameter", "of the function below"));
                break;

            case LuaAnnotationSuggestion.VariableName:
                // What a cast can narrow: the locals in scope where the cast is written. Innermost
                // first, as elsewhere, and only those already declared - there is nothing to cast
                // about a binding that does not exist yet.
                foreach (var local in scopes.VisibleAt(WithinBlock(context.ReplaceFrom, block)))
                    items.Add(new LuaCompletionItem(local.Name, local.Name, KindOf(local.Kind),
                                                    local.Kind.ToString().ToLowerInvariant(),
                                                    "in scope here"));
                foreach (var global in scopes.Globals.Values)
                    items.Add(new LuaCompletionItem(global.Name, global.Name,
                                                    LuaCompletionKind.Variable, "global", null));
                break;

            case LuaAnnotationSuggestion.DiagnosticAction:
                foreach (string action in LuaAnnotations.DiagnosticActions)
                    items.Add(new LuaCompletionItem(action, action, LuaCompletionKind.Keyword, null, null));
                break;
        }
    }

    /// <summary>
    /// A position pulled back inside the file's outermost block.
    ///
    /// An annotation written after the last statement is outside it - a comment is not a statement,
    /// so the block ends above it - and asking what is in scope out there answers nothing. Which
    /// is wrong in the one place a cast is most likely to be typed: at the end of what has been
    /// written so far.
    /// </summary>
    private static LuaPosition WithinBlock(LuaPosition at, LuaBlock block)
    {
        var end = block.Span.End;
        bool past = at.Line > end.Line || (at.Line == end.Line && at.Column > end.Column);
        return past ? end : at;
    }

    /// <summary>How a tag's completion entry describes what comes after it.</summary>
    private static string Shape(LuaAnnotationTag tag) => tag.Shape switch
    {
        LuaAnnotationShape.Type => $"@{tag.Name} <type>",
        LuaAnnotationShape.NameThenType => $"@{tag.Name} <name> <type>",
        LuaAnnotationShape.Name => $"@{tag.Name} <name>",
        LuaAnnotationShape.Diagnostic => $"@{tag.Name} <action>: <name>",
        _ => $"@{tag.Name}",
    };

    /// <summary>
    /// The parameters of the first function declared after a line - what the annotations above it
    /// are describing.
    ///
    /// Only the first, and only when nothing but more annotation lines separates them: an
    /// annotation block applies to what immediately follows, and reaching past a blank line or a
    /// statement would offer names from an unrelated function.
    /// </summary>
    private static IEnumerable<string> ParametersBelow(string source, int line)
    {
        string[] lines = source.Split('\n');
        int at = line + 1;
        while (at < lines.Length && lines[at].TrimEnd('\r').TrimStart().StartsWith("---")) at++;
        if (at >= lines.Length) return [];

        var block = LuaParser.Parse(source, out _);
        foreach (var node in LuaAstWalker.Walk(block))
        {
            var function = node switch
            {
                LuaFunctionStat statement => statement.Function,
                LuaLocalFunctionStat statement => statement.Function,
                _ => null,
            };
            if (function is null || node.Span.Start.Line != at) continue;

            var names = function.Parameters.Select(p => p.Name).ToList();
            // A method's implicit receiver is not written in the parameter list and is not
            // documented with @param, so offering it would be offering a name that cannot be used.
            if (node is LuaFunctionStat { IsMethod: true }) names.Remove("self");
            return names;
        }
        return [];
    }

    private static string ScriptNameDescription(LuaScriptNameKind kind) => kind switch
    {
        LuaScriptNameKind.LevelScript => "a script in this level, loadable with {0}",
        LuaScriptNameKind.GameScript => "a game-wide script, loadable with {0}",
        _ => "{0}",
    };

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
        string source, List<LuaToken> tokens, LuaPosition caret, LuaBlock block,
        LuaTypeInference inference, LuaTableShapes shapes)
    {
        // Tokens strictly before the caret, comments excluded.
        var before = tokens
            .Where(t => t.Kind is not (LuaTokenKind.Comment or LuaTokenKind.EndOfFile))
            .Where(t => t.End.Line < caret.Line || (t.End.Line == caret.Line && t.End.Column <= caret.Column))
            .ToList();

        // An annotation and a string both rule out every other trigger, so they come first.
        if (AnnotationAt(source, caret) is { } inAnnotation) return inAnnotation;
        if (StringArgumentAt(source, tokens, caret) is { } inString) return inString;

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
    /// The context for a caret inside a LuaLS annotation, or null.
    ///
    /// Decided from the line's text rather than from the parse, because an annotation is a comment
    /// as far as the language is concerned - there is no node to find - and because a half-written
    /// one has to work.
    /// </summary>
    private static LuaCompletionContext? AnnotationAt(string source, LuaPosition caret)
    {
        string[] lines = source.Split('\n');
        if (caret.Line < 0 || caret.Line >= lines.Length) return null;

        string line = lines[caret.Line].TrimEnd('\r');
        var found = LuaAnnotations.SuggestionAt(line, caret.Column);
        if (found.Suggestion == LuaAnnotationSuggestion.None) return null;

        var from = new LuaPosition(caret.Line, found.Start);
        return new LuaCompletionContext(LuaCompletionTrigger.Annotation, found.Prefix, from, caret,
                                        null, null, LuaScriptNameKind.None, null,
                                        found.Suggestion, found.Tag);
    }

    /// <summary>
    /// The context for a caret inside a string literal that a known function takes a script name
    /// in, or null when it is not in one - or is in one nothing is known about.
    ///
    /// The literal is usually unterminated while it is being typed, which the lexer reports as an
    /// error rather than a string. Both shapes count: refusing the unterminated one would mean
    /// completion worked only after the closing quote was already there, which with auto-pairing is
    /// the common case but not the only one.
    /// </summary>
    private static LuaCompletionContext? StringArgumentAt(
        string source, List<LuaToken> tokens, LuaPosition caret)
    {
        string[] lines = source.Split('\n');
        int index = tokens.FindIndex(t => IsQuoted(lines, t) && Within(t, caret));
        if (index < 0) return null;

        var token = tokens[index];
        // Just inside the opening quote: where the replacement starts, and where the prefix does.
        var from = new LuaPosition(token.Start.Line, token.Start.Column + 1);
        if (caret.Line != from.Line || caret.Column < from.Column) return null;

        if (EnclosingCall(tokens, index) is not var (function, argument)) return null;
        var kind = LuaStringArguments.KindFor(function, argument);
        if (kind == LuaScriptNameKind.None) return null;

        // Read from the SOURCE, not from the token. An unterminated literal - which is what a
        // string being typed is - comes back as an error token whose text is the lexer's message,
        // so slicing that would complete against the words "unterminated string".
        string line = caret.Line < lines.Length ? lines[caret.Line].TrimEnd('\r') : "";
        string prefix = line[from.Column..Math.Min(caret.Column, line.Length)];

        return new LuaCompletionContext(LuaCompletionTrigger.StringArgument, prefix, from, caret,
                                        null, null, kind, function);
    }

    /// <summary>
    /// Whether a token is a quoted string, finished or not.
    ///
    /// Decided by the character the token starts with rather than by its kind, because the two
    /// states have different kinds - String once closed, Error while it is still being typed - and
    /// because not every error is an unterminated string. A long-bracket string fails this for the
    /// right reason too: nothing takes a script name in one.
    /// </summary>
    private static bool IsQuoted(string[] lines, LuaToken token)
    {
        if (token.Kind is not (LuaTokenKind.String or LuaTokenKind.Error)) return false;
        if (token.Start.Line >= lines.Length) return false;
        string line = lines[token.Start.Line];
        return token.Start.Column < line.Length && line[token.Start.Column] is '"' or '\'';
    }

    /// <summary>
    /// Whether the caret is INSIDE a literal: past its opening quote, and not out the other side.
    ///
    /// Where the other side is depends on whether the literal is finished. A closed one ends just
    /// past its closing quote, so the caret has to stop short of that - immediately after the quote
    /// it has left the string, and completing script names there would offer them in the middle of
    /// ordinary code. An unterminated one has no closing quote and ends at the line's end, which is
    /// exactly where it is being typed, so that position counts as inside.
    /// </summary>
    private static bool Within(LuaToken token, LuaPosition caret)
    {
        if (caret.Line != token.Start.Line || caret.Column <= token.Start.Column) return false;
        return token.Kind == LuaTokenKind.String
            ? caret.Line < token.End.Line || caret.Column < token.End.Column
            : caret.Line < token.End.Line || caret.Column <= token.End.Column;
    }

    /// <summary>
    /// The call a token is an argument of: the function's name and which argument this is.
    ///
    /// Walks back counting nesting, so a string inside a nested call belongs to the inner one and
    /// commas in a nested argument list do not shift the index.
    /// </summary>
    private static (string Function, int Argument)? EnclosingCall(List<LuaToken> tokens, int from)
    {
        int depth = 0, argument = 0;
        for (int i = from - 1; i >= 0; i--)
        {
            var token = tokens[i];
            if (token.Kind is LuaTokenKind.Comment or LuaTokenKind.EndOfFile) continue;
            if (token.Kind != LuaTokenKind.Symbol) continue;

            switch (token.Text)
            {
                case ")" or "]" or "}":
                    depth++;
                    break;
                case "[" or "{":
                    if (depth == 0) return null;   // inside a table or an index, not an argument list
                    depth--;
                    break;
                case "(":
                    if (depth > 0) { depth--; break; }
                    // The name being called has to be the token immediately before the bracket.
                    int name = i - 1;
                    while (name >= 0 && tokens[name].Kind is LuaTokenKind.Comment) name--;
                    return name >= 0 && tokens[name].Kind == LuaTokenKind.Name
                        ? (tokens[name].Text, argument)
                        : null;
                case "," when depth == 0:
                    argument++;
                    break;
            }
        }
        return null;
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
