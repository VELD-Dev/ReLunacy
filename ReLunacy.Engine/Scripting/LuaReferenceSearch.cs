namespace ReLunacy.Engine.Scripting;

/// <summary>One place a name is written.</summary>
/// <param name="Path">The script it is in.</param>
/// <param name="Span">Where in that script.</param>
/// <param name="LineText">The whole line, for the results list to show.</param>
/// <param name="IsDeclaration">Whether this occurrence is the declaration itself.</param>
public sealed record LuaReference(string Path, LuaSpan Span, string LineText, bool IsDeclaration);

/// <summary>Where a name is declared, when that can be established.</summary>
/// <param name="Path">The script, or the documentation file for a documented API name.</param>
/// <param name="Span">Where the name is written at its declaration.</param>
/// <param name="InArchive">False for a documentation file, which is not a script the editor can
/// open - the caller has to say so rather than navigate nowhere.</param>
public sealed record LuaDeclaration(string Path, LuaSpan Span, bool InArchive);

/// <summary>
/// Finds where a name is declared and everywhere it is mentioned.
///
/// Two different searches, because Lua has two kinds of name and conflating them gives wrong
/// answers in opposite directions. A local is resolved: its mentions are exactly what the scope
/// tree says, so a <c>value</c> in one function never matches a <c>value</c> in another. A global
/// has no declaration to resolve against and is shared through the level's global table, so its
/// mentions are gathered across every script that table reaches.
///
/// The cross-file half still lexes rather than matching text - a name in a comment or inside a
/// string is not a mention - and it drops files where the name turns out to be a local, since that
/// is a different binding that merely spells the same.
/// </summary>
public static class LuaReferenceSearch
{
    /// <summary>
    /// Everywhere the name at <paramref name="position"/> is mentioned, declaration included.
    ///
    /// <paramref name="siblings"/> is only consulted for a global: a local cannot be referred to
    /// from another file, so searching them would only turn up unrelated names.
    /// </summary>
    public static List<LuaReference> Find(
        string currentPath, string source, LuaPosition position,
        IEnumerable<LuaScriptSource>? siblings = null)
    {
        var block = LuaParser.Parse(source, out _);
        var scopes = LuaScopeResolver.Resolve(block);
        var symbol = scopes.FindAt(position);
        if (symbol is null) return [];

        if (!symbol.IsGlobal)
        {
            string[] lines = Split(source);
            return [.. symbol.References
                .OrderBy(r => r.Start.Line).ThenBy(r => r.Start.Column)
                .Select(r => new LuaReference(currentPath, r, LineAt(lines, r.Start.Line),
                                              r == symbol.Definition))];
        }

        var found = new List<LuaReference>();
        foreach (var file in Files(currentPath, source, siblings))
            found.AddRange(InOneFile(file.Path, file.Text, symbol.Name));
        return found;
    }

    /// <summary>
    /// Where the name at <paramref name="position"/> comes from: a local's binding, a function the
    /// scripts declare, or the annotation that documents an API name.
    /// </summary>
    public static LuaDeclaration? Declaration(
        string currentPath, string source, LuaPosition position,
        LuaApiDatabase? api = null, LuaScriptIndex? scripts = null)
    {
        var block = LuaParser.Parse(source, out _);
        var scopes = LuaScopeResolver.Resolve(block);

        if (scopes.FindAt(position) is { IsGlobal: false, Definition: { } definition })
            return new LuaDeclaration(currentPath, definition, true);

        string? name = NameAt(source, position);
        if (name is null) return null;

        // A function one of the scripts declares beats the documentation: that is the one that runs.
        if (scripts?.Find(name) is { } declared)
            return new LuaDeclaration(declared.Function.SourceFile, declared.NameSpan, true);

        // Otherwise the annotation that describes it, which lives in a documentation file on disk
        // rather than in the archive - worth reporting, not worth pretending is navigable.
        if (api?.Functions.GetValueOrDefault(name) is { } documented)
            return new LuaDeclaration(documented.SourceFile,
                                      new LuaSpan(documented.SourcePosition, documented.SourcePosition),
                                      false);
        if (api?.Variables.GetValueOrDefault(name) is { } variable)
            return new LuaDeclaration(variable.SourceFile, default, false);
        return null;
    }

    /// <summary>The current file first, then the siblings with it excluded - it is already in the
    /// list the browser builds, and counting it twice would double every hit.</summary>
    private static IEnumerable<LuaScriptSource> Files(
        string currentPath, string source, IEnumerable<LuaScriptSource>? siblings)
    {
        yield return new LuaScriptSource(currentPath, source);
        foreach (var sibling in siblings ?? [])
            if (!string.Equals(sibling.Path, currentPath, StringComparison.OrdinalIgnoreCase))
                yield return sibling;
    }

    /// <summary>
    /// Mentions of a global in one file. Name tokens only, so comments and strings are not
    /// matches, and nothing at all when the file binds that name as a local - then it is a
    /// different thing that merely spells the same.
    /// </summary>
    private static IEnumerable<LuaReference> InOneFile(string path, string text, string name)
    {
        var block = LuaParser.Parse(text, out _);
        var scopes = LuaScopeResolver.Resolve(block);
        if (scopes.Locals.Any(local => local.Name == name)) yield break;

        string[] lines = Split(text);
        var tokens = LuaLexer.Tokenize(text)
            .Where(t => t.Kind is not (LuaTokenKind.Comment or LuaTokenKind.EndOfFile))
            .ToList();

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind != LuaTokenKind.Name || token.Text != name) continue;

            // A global has no binding to declare, but `function name(` is what a reader means by
            // its declaration, so it is marked - the results list shows that row differently.
            // `function M.name(` deliberately does not count: that declares a key of M, not M.
            bool declares = i > 0 && tokens[i - 1].Kind == LuaTokenKind.Keyword
                         && tokens[i - 1].Text == "function"
                         && i + 1 < tokens.Count && tokens[i + 1].Text == "(";

            yield return new LuaReference(path, new LuaSpan(token.Start, token.End),
                                          LineAt(lines, token.Start.Line), declares);
        }
    }

    private static string? NameAt(string source, LuaPosition position)
    {
        foreach (var token in LuaLexer.Tokenize(source))
            if (token.Kind == LuaTokenKind.Name && token.Start.Line == position.Line
                && position.Column >= token.Start.Column && position.Column <= token.End.Column)
                return token.Text;
        return null;
    }

    private static string[] Split(string text) => text.Split('\n');

    private static string LineAt(string[] lines, int line) =>
        line >= 0 && line < lines.Length ? lines[line].TrimEnd('\r') : "";
}
