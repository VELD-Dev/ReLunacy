namespace ReLunacy.Engine.Scripting;

/// <summary>What to show about the name under the pointer.</summary>
/// <param name="Label">The name itself.</param>
/// <param name="Kind">What sort of thing it is, for the heading.</param>
/// <param name="Detail">A signature or a type.</param>
/// <param name="Description">The documentation body, when there is one.</param>
public sealed record LuaHover(string Label, string Kind, string? Detail, string? Description);

/// <summary>
/// Documentation for the name at a position - what a tooltip shows when the pointer rests on a word.
///
/// Most of it is answered by asking completion what it would offer for that exact name, with the
/// caret placed at the name's end so the whole word is the prefix. That is not a shortcut: hover and
/// completion have to agree, or a name is documented one way in the list and another way on the
/// pointer. It also means receiver types, declared functions, fields and methods all work here
/// because they already work there - including the inference behind <c>m:</c> after
/// <c>local m = moby_handle:new(1)</c>.
///
/// Locals are answered directly from the scope tree instead, for one reason: completion
/// deliberately refuses to offer a binding at its own declaration (suggesting `value` back while
/// `local value` is being typed is noise), so going through it would leave exactly the declaration
/// a reader is most likely to point at with nothing to say.
/// </summary>
public static class LuaHoverEngine
{
    public static LuaHover? At(string source, LuaPosition position, LuaApiDatabase api,
                               LuaScriptIndex? scripts = null)
    {
        if (NameAt(source, position) is not { } name) return null;

        var block = LuaParser.Parse(source, out _);
        var scopes = LuaScopeResolver.Resolve(block);

        if (scopes.FindAt(name.Start) is { IsGlobal: false } local)
        {
            var inference = new LuaTypeInference(api, scopes, scripts, null,
                                                 LuaCasts.Find(source, scopes));
            return DescribeLocal(local, inference, name.Start);
        }

        // The caret goes at the name's END so the prefix is the whole word, which is what makes an
        // exact label match meaningful rather than a word that merely starts the same.
        var items = LuaCompletionEngine.Complete(source, name.End, api, out _, scripts);
        var match = items.FirstOrDefault(i => i.Label == name.Text);
        return match is null ? null : new LuaHover(match.Label, KindOf(match.Kind),
                                                   match.Detail, match.Description);
    }

    /// <summary>
    /// What to call a kind on the hover line. Everything a hover can land on that completion calls
    /// a "variable" is reached by a bare name with no binding, which is what a global IS - and
    /// naming it that says more than the word completion uses for its list.
    /// </summary>
    private static string KindOf(LuaCompletionKind kind) => kind switch
    {
        LuaCompletionKind.Variable => "global",
        LuaCompletionKind.LoopVariable => "loop variable",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// A binding's own description: what it is, the type it was initialised from when that can be
    /// inferred, and how widely it is used - a count being the thing a reader cannot see at a
    /// glance and the reason to resolve scopes at all.
    /// </summary>
    private static LuaHover DescribeLocal(LuaSymbol symbol, LuaTypeInference inference, LuaPosition at)
    {
        string kind = symbol.Kind switch
        {
            LuaSymbolKind.Parameter => "parameter",
            LuaSymbolKind.LoopVariable => "loop variable",
            _ => "local",
        };

        int mentions = symbol.References.Count;
        string where = symbol.Definition is { } definition ? $"declared at {definition.Start}" : "";
        string used = mentions switch
        {
            <= 1 => "never used after it is bound",
            2 => "mentioned once more",
            _ => $"mentioned {mentions - 1} more times",
        };

        // Asked at the hovered position, not in general: a cast above it changes the answer, and
        // the same name can be one type above the cast and another below.
        return new LuaHover(symbol.Name, kind, inference.TypeOfSymbolAt(symbol, at),
                            where.Length > 0 ? $"{where}, {used}" : used);
    }

    /// <summary>
    /// The name token covering a position, or null when the position is not on one.
    ///
    /// Only Name tokens qualify: a keyword, a number or a string has nothing to document, and
    /// offering a tooltip for them would make the pointer twitchy over ordinary code. The end is
    /// inclusive so that resting on the character just past a name still describes it, matching
    /// what the caret-occurrence highlight does.
    /// </summary>
    private static LuaToken? NameAt(string source, LuaPosition position)
    {
        foreach (var token in LuaLexer.Tokenize(source))
        {
            if (token.Kind != LuaTokenKind.Name) continue;
            if (token.Start.Line != position.Line) continue;
            if (position.Column >= token.Start.Column && position.Column <= token.End.Column)
                return token;
        }
        return null;
    }
}
