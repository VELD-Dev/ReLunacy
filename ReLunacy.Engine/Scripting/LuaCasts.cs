namespace ReLunacy.Engine.Scripting;

/// <summary>
/// One <c>---@cast</c>: a name, the type it is to be treated as from here on, and where that was
/// said.
/// </summary>
/// <param name="Binding">
/// The local it refers to, when it refers to one. Resolved here rather than matched by name later,
/// so a cast written inside one function cannot change the type of an identically named local in
/// another.
/// </param>
public sealed record LuaCast(string Name, string Type, LuaPosition At, LuaSymbol? Binding);

/// <summary>
/// Finds the <c>---@cast</c> annotations in a file.
///
/// A cast is a comment, so there is no node for it in the tree and nothing downstream would see it
/// without this. It is what lets someone say what the engine's undocumented API handed them -
/// <c>local m = get_target()</c> followed by <c>---@cast m Moby</c> - and get members afterwards,
/// which is the whole reason the annotation exists in a codebase with no type information.
///
/// Scope is approximated: a cast applies to every later line, rather than to the end of the block
/// it was written in. For a local the BINDING is resolved, so the approximation only shows when the
/// same binding is used after the block the cast was written in - narrow enough to be worth the
/// simplicity, and recorded here rather than left to be discovered.
/// </summary>
public static class LuaCasts
{
    public static List<LuaCast> Find(string source, LuaScopeAnalysis scopes)
    {
        var casts = new List<LuaCast>();
        string[] lines = source.Split('\n');

        for (int line = 0; line < lines.Length; line++)
        {
            string text = lines[line].TrimEnd('\r');
            int start = text.Length - text.TrimStart().Length;

            // Parsed by the same code that colours it, so the two cannot read a line differently.
            var parts = LuaAnnotations.Highlight(text, start);
            if (parts.Count < 4) continue;
            if (Part(text, parts, LuaAnnotationPart.Tag) != "@cast") continue;

            string? name = Part(text, parts, LuaAnnotationPart.Name);
            string? type = Part(text, parts, LuaAnnotationPart.Type);
            if (name is null || type is null) continue;

            var at = new LuaPosition(line, start);
            // The binding this names, as seen from the cast's own position - which is after the
            // declaration, since there is nothing to cast before it.
            var binding = scopes.VisibleAt(at).FirstOrDefault(s => s.Name == name);
            casts.Add(new LuaCast(name, type, at, binding));
        }
        return casts;
    }

    private static string? Part(string line, List<LuaAnnotationSpan> parts, LuaAnnotationPart part)
    {
        foreach (var span in parts)
            if (span.Part == part) return line.Substring(span.Start, span.Length);
        return null;
    }
}
