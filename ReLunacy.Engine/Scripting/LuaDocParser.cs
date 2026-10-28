namespace ReLunacy.Engine.Scripting;

/// <summary>
/// Reads LuaLS / EmmyLua annotation files into a <see cref="LuaApiDatabase"/>.
///
/// The files are ordinary Lua stubs, so they are read with the same lexer and parser the editor
/// uses rather than by matching lines: the lexer supplies the <c>---</c> comment blocks with their
/// positions, the parser supplies the declarations those blocks sit above, and the two are joined by
/// line. That means a stub with a syntax error is reported as such, and a declaration's real
/// parameter names are available to cross-check the annotations against.
///
/// Supported tags: <c>@class</c>, <c>@field</c>, <c>@param</c>, <c>@return</c>, <c>@alias</c>,
/// <c>@type</c>, <c>@meta</c>. Undocumented plain <c>---</c> lines become the description. Anything
/// else is reported rather than ignored, so a typo in a tag name is visible.
/// </summary>
public static class LuaDocParser
{
    private static readonly string[] KnownTags =
        ["class", "field", "param", "return", "alias", "type", "meta", "deprecated"];

    public static void ParseInto(LuaApiDatabase database, string source, string sourceFile)
    {
        var block = LuaParser.Parse(source, out var syntax);
        foreach (var diagnostic in syntax)
            database.AddDiagnostic(new LuaDocDiagnostic(diagnostic.Message, sourceFile, diagnostic.Span.Start));

        // Comment blocks, keyed by the line they end on, so a declaration can find the block
        // directly above it.
        var diagnostics = new List<LuaDocDiagnostic>();
        var blocks = ReadBlocks(source, sourceFile, diagnostics, database.AddAlias);
        foreach (var diagnostic in diagnostics) database.AddDiagnostic(diagnostic);

        // Classes first: a method's owner must already exist when the method is attached, and a file
        // may well declare the class after some of its methods.
        foreach (var (_, annotations) in blocks)
            if (annotations.Class is { } className)
                database.Add(new LuaApiType(className, annotations.BaseType,
                    [.. annotations.Fields], [], annotations.Description, sourceFile));

        // Then the carrier variables, so `function string.len` can resolve `string` to the class it
        // holds. This has to precede the function pass for the same reason the classes do.
        foreach (var statement in block.Statements)
        {
            if (!blocks.TryGetValue(statement.Span.Start.Line - 1, out var annotations)) continue;
            if (annotations.Class is not { } className) continue;
            string? carrier = statement switch
            {
                LuaAssignStat { Targets: [LuaNameExpr target] } => target.Name,
                LuaLocalStat { Names: [var only] } => only.Name,
                _ => null,
            };
            if (carrier != null && carrier != className) database.AddCarrier(carrier, className);
        }

        foreach (var statement in block.Statements)
        {
            int declarationLine = statement.Span.Start.Line;
            if (!blocks.TryGetValue(declarationLine - 1, out var annotations)) continue;
            Apply(database, statement, annotations, sourceFile);
        }

        database.ValidateTypeReferences();
    }

    private static void Apply(LuaApiDatabase database, LuaStat statement, LuaAnnotationBlock annotations, string sourceFile)
    {
        switch (statement)
        {
            case LuaFunctionStat function:
            {
                var (owner, name, isMethod) = DescribeTarget(function.Target, function.IsMethod);
                var parameters = MergeParameters(annotations, function.Function, sourceFile, name,
                                                 Collect(database), reportUndocumented: true);
                database.Add(new LuaApiFunction(name, parameters, [.. annotations.Returns],
                    annotations.Description, owner, isMethod, sourceFile, statement.Span.Start));
                break;
            }

            case LuaLocalFunctionStat function:
            {
                var parameters = MergeParameters(annotations, function.Function, sourceFile,
                                                 function.Name.Name, Collect(database),
                                                 reportUndocumented: true);
                database.Add(new LuaApiFunction(function.Name.Name, parameters, [.. annotations.Returns],
                    annotations.Description, null, false, sourceFile, statement.Span.Start));
                break;
            }

            // `Name = value` or `local Name = value` carrying a @type: a documented global that is
            // not a function. `local X = {}` under a @class is the class's carrier and needs nothing.
            case LuaAssignStat assign when assign.Targets is [LuaNameExpr target]:
                if (annotations.Class is null && annotations.TypeTag is { } assignType)
                    database.Add(new LuaApiVariable(target.Name, assignType, annotations.Description, sourceFile));
                break;

            case LuaLocalStat local when local.Names.Count == 1:
                if (annotations.Class is null && annotations.TypeTag is { } localType)
                    database.Add(new LuaApiVariable(local.Names[0].Name, localType, annotations.Description, sourceFile));
                break;
        }
    }

    /// <summary>Somewhere to send diagnostics, so the block reader and parameter merger can serve
    /// both the stub files and the script index without either knowing about a database.</summary>
    private static Action<LuaDocDiagnostic> Collect(LuaApiDatabase database) => database.AddDiagnostic;

    /// <summary>
    /// Takes the parameter list from the annotations but checks it against the declaration's own
    /// signature, which is the one cross-check available: a <c>@param</c> naming an argument the
    /// function does not have, or a missing one, is a mistake worth reporting.
    ///
    /// <paramref name="reportUndocumented"/> is off for ordinary scripts. A stub file exists to
    /// document, so an undocumented parameter there is an omission; a game script is code that
    /// happens to be readable, and flagging every parameter in 183 shipped declarations would bury
    /// the real problems.
    /// </summary>
    public static List<LuaApiParameter> MergeParameters(
        LuaAnnotationBlock? annotations, LuaFunctionExpr function,
        string sourceFile, string functionName, Action<LuaDocDiagnostic> report,
        bool reportUndocumented)
    {
        annotations ??= new LuaAnnotationBlock();
        var declared = function.Parameters.Select(p => p.Name).ToList();
        // A method's implicit self is not annotated.
        if (declared.FirstOrDefault() == "self") declared.RemoveAt(0);
        // A variadic stub declares `...` in its signature rather than as a named parameter, and
        // LuaLS documents it as `---@param ... type`, so it counts as declared.
        if (function.IsVararg) declared.Add("...");

        foreach (var annotated in annotations.Parameters)
            if (!declared.Contains(annotated.Name))
                report(new LuaDocDiagnostic(
                    $"'{functionName}' has no parameter '{annotated.Name}'", sourceFile, function.Span.Start));

        if (reportUndocumented)
            foreach (string name in declared)
                if (annotations.Parameters.All(p => p.Name != name))
                    report(new LuaDocDiagnostic(
                        $"parameter '{name}' of '{functionName}' is undocumented", sourceFile, function.Span.Start));

        // Declaration order wins, so the popup lists arguments in call order even if the annotations
        // were written out of order. Anything annotated but not declared is kept so the diagnostic
        // above is the only consequence.
        var ordered = declared
            .Select(name => annotations.Parameters.FirstOrDefault(p => p.Name == name)
                            ?? new LuaApiParameter(name, "any", null, false))
            .ToList();
        ordered.AddRange(annotations.Parameters.Where(p => !declared.Contains(p.Name)));
        return ordered;
    }

    /// <summary>Splits <c>function Moby:is_valid()</c> into owner, name and method-ness.</summary>
    private static (string? Owner, string Name, bool IsMethod) DescribeTarget(LuaExpr target, bool isMethod)
    {
        if (target is LuaIndexExpr { Key: LuaStringExpr key } index)
        {
            string? owner = index.Target switch
            {
                LuaNameExpr name => name.Name,
                LuaIndexExpr { Key: LuaStringExpr inner } => inner.Value,
                _ => null,
            };
            return (owner, key.Value, isMethod);
        }
        return (null, target is LuaNameExpr plain ? plain.Name : "?", isMethod);
    }

    /// <summary>
    /// Contiguous <c>---</c> comment runs, parsed into tags, keyed by their last line - so a
    /// declaration finds its block by looking at the line above itself.
    ///
    /// Public because the script index reads annotations out of ordinary Lua files too. Both paths
    /// have to understand exactly the same tags, and two implementations of that would drift.
    /// </summary>
    public static Dictionary<int, LuaAnnotationBlock> ReadBlocks(
        string source, string sourceFile, List<LuaDocDiagnostic> diagnostics,
        Action<string, string>? onAlias = null)
    {
        var tokens = LuaLexer.Tokenize(source);
        var result = new Dictionary<int, LuaAnnotationBlock>();
        var current = new LuaAnnotationBlock();
        int blockStart = -1, lastLine = -2;

        foreach (var token in tokens.Where(t => t.Kind == LuaTokenKind.Comment))
        {
            // Only doc comments participate; a plain `--` comment breaks the run.
            if (!token.Text.StartsWith('-'))
            {
                Flush();
                continue;
            }

            if (token.Start.Line != lastLine + 1)
            {
                Flush();
                blockStart = token.Start.Line;
                current = new LuaAnnotationBlock();
            }
            lastLine = token.Start.Line;
            ReadLine(current, token.Text.TrimStart('-').Trim(), token.Start, sourceFile,
                     diagnostics, onAlias);
        }
        Flush();
        return result;

        void Flush()
        {
            if (blockStart >= 0 && !current.IsEmpty) result[lastLine] = current;
            blockStart = -1;
            lastLine = -2;
        }
    }

    private static void ReadLine(LuaAnnotationBlock into, string text, LuaPosition at,
                                 string sourceFile, List<LuaDocDiagnostic> diagnostics,
                                 Action<string, string>? onAlias)
    {
        if (!text.StartsWith('@'))
        {
            if (text.Length > 0)
                into.DescriptionLines.Add(text);
            return;
        }

        string[] parts = text[1..].Split(' ', 2, StringSplitOptions.TrimEntries);
        string tag = parts[0];
        string rest = parts.Length > 1 ? parts[1] : "";

        if (!KnownTags.Contains(tag))
        {
            diagnostics.Add(new LuaDocDiagnostic($"unknown annotation '@{tag}'", sourceFile, at));
            return;
        }

        switch (tag)
        {
            case "meta" or "deprecated":
                break;

            case "class":
            {
                // `Name : Base`
                string[] halves = rest.Split(':', 2, StringSplitOptions.TrimEntries);
                into.Class = halves[0];
                into.BaseType = halves.Length > 1 && halves[1].Length > 0 ? halves[1] : null;
                break;
            }

            case "field":
            {
                var (name, type, description) = ReadNameTypeDescription(rest);
                if (name is null) diagnostics.Add(new LuaDocDiagnostic("@field needs a name and a type", sourceFile, at));
                else into.Fields.Add(new LuaApiField(name, type, description));
                break;
            }

            case "param":
            {
                var (name, type, description) = ReadNameTypeDescription(rest);
                if (name is null) { diagnostics.Add(new LuaDocDiagnostic("@param needs a name and a type", sourceFile, at)); break; }
                bool optional = name.EndsWith('?');
                into.Parameters.Add(new LuaApiParameter(name.TrimEnd('?'), type, description, optional));
                break;
            }

            case "return":
            {
                string[] halves = rest.Split(' ', 2, StringSplitOptions.TrimEntries);
                if (halves[0].Length == 0) { diagnostics.Add(new LuaDocDiagnostic("@return needs a type", sourceFile, at)); break; }
                into.Returns.Add(new LuaApiReturn(halves[0], halves.Length > 1 ? halves[1] : null));
                break;
            }

            case "alias":
            {
                string[] halves = rest.Split(' ', 2, StringSplitOptions.TrimEntries);
                if (halves.Length < 2) diagnostics.Add(new LuaDocDiagnostic("@alias needs a name and a target", sourceFile, at));
                else onAlias?.Invoke(halves[0], halves[1]);
                break;
            }

            case "type":
                into.TypeTag = rest;
                break;
        }
    }

    private static (string? Name, string Type, string? Description) ReadNameTypeDescription(string rest)
    {
        string[] parts = rest.Split(' ', 3, StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts[0].Length == 0) return (null, "any", null);
        return (parts[0], parts[1], parts.Length > 2 ? parts[2] : null);
    }

}

/// <summary>Tags collected from one <c>---</c> comment block, before being attached to a
/// declaration.</summary>
public sealed class LuaAnnotationBlock
{
    public string? Class;
    public string? BaseType;
    public string? TypeTag;
    public List<LuaApiField> Fields { get; } = [];
    public List<LuaApiParameter> Parameters { get; } = [];
    public List<LuaApiReturn> Returns { get; } = [];
    public List<string> DescriptionLines { get; } = [];

    public string? Description =>
        DescriptionLines.Count == 0 ? null : string.Join('\n', DescriptionLines);

    public bool IsEmpty =>
        Class is null && TypeTag is null && Fields.Count == 0 && Parameters.Count == 0
        && Returns.Count == 0 && DescriptionLines.Count == 0;
}
