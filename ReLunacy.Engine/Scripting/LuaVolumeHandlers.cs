namespace ReLunacy.Engine.Scripting;

public sealed record LuaVolumeHandler(string Path, LuaPosition Position, string Function,
                                      string Volume, string? LocationTag, int? LocationId,
                                      LuaSpan? LocationArgument, bool ShowsLocation);

public static class LuaVolumeHandlers
{
    public static IEnumerable<LuaVolumeHandler> Find(LuaScriptSource source)
    {
        var block = LuaParser.Parse(source.Text, out _);
        var scopes = LuaScopeResolver.Resolve(block);
        var locals = scopes.Locals.SelectMany(s => s.References.Select(r => (r, s)))
            .ToLookup(entry => entry.r, entry => entry.s);

        string? VolumeName(LuaExpr expression, int depth = 0) => expression switch
        {
            LuaParenExpr p when depth < 8 => VolumeName(p.Inner, depth + 1),
            LuaNameExpr n when locals[n.Span].FirstOrDefault() is not { } => n.Name,
            LuaNameExpr n when depth < 8 && locals[n.Span].First().Initializer is { } value =>
                VolumeName(value, depth + 1),
            _ => null,
        };

        foreach (var call in LuaAstWalker.Walk(block).OfType<LuaCallExpr>())
        {
            if (call.Target is not LuaNameExpr { Name: "on_enter" or "on_enter_volume" } target
                || locals[target.Span].Any() || call.Arguments.Count < 2
                || VolumeName(call.Arguments[1]) is not { } volume) continue;

            var calls = call.Arguments.ElementAtOrDefault(2) is LuaFunctionExpr callback
                ? LuaAstWalker.Walk(callback.Body).OfType<LuaCallExpr>()
                    .Where(c => c.Target is LuaNameExpr { Name: "show_location" or "UpdateLocation" }
                        && c.Arguments.Count > 0).ToArray()
                : [];
            LuaExpr? tag = calls.FirstOrDefault()?.Arguments[0];
            int? id = tag is LuaNumberExpr { Value: >= 0 and <= int.MaxValue } number
                && number.Value == Math.Truncate(number.Value) ? (int)number.Value : null;
            yield return new LuaVolumeHandler(source.Path, call.Span.Start, target.Name, volume,
                tag is LuaNameExpr name ? name.Name : tag is LuaStringExpr literal ? literal.Value : null,
                id, calls.Length == 1 && (id.HasValue || tag is LuaNameExpr) ? tag!.Span : null, calls.Length > 0);
        }
    }
}
