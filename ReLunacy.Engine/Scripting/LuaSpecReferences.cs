namespace ReLunacy.Engine.Scripting;

/// <summary>A script naming a key of a spec table: <c>DialogueSpecs.GC3_FCOMP_GCVO_FLAV_MES01_PA</c>.</summary>
/// <param name="Context">The function it is passed to (<c>speak</c>, <c>random_dialogue_add</c>), or the variable it is
/// stored in (<c>paDialogue</c>, a table a loop picks from); null when neither.</param>
public sealed record LuaSpecReference(string Path, LuaPosition Position, string Key, string? Context);

public static class LuaSpecReferences
{
    /// <summary>Every <c>table.KEY</c> and <c>table["KEY"]</c> of the global <paramref name="table"/> in a script;
    /// comments are not code, so a commented-out line names nothing.</summary>
    public static IEnumerable<LuaSpecReference> Find(LuaScriptSource source, string table)
    {
        var block = LuaParser.Parse(source.Text, out _);
        var scopes = LuaScopeResolver.Resolve(block);
        var locals = scopes.Locals.SelectMany(s => s.References).ToHashSet();
        var found = new List<LuaSpecReference>();
        Visit(block, null);
        return found;

        void Visit(LuaNode node, string? context)
        {
            switch (node)
            {
                case LuaIndexExpr { Target: LuaNameExpr target, Key: LuaStringExpr key } index
                    when target.Name == table && !locals.Contains(target.Span):
                    found.Add(new LuaSpecReference(source.Path, index.Span.Start, key.Value, context));
                    return;
                // table.insert(list, ...) adds to list, which is what a script later picks from.
                case LuaCallExpr { Target: LuaIndexExpr { Target: LuaNameExpr { Name: "table" }, Key: LuaStringExpr { Value: "insert" } },
                    Arguments: [LuaNameExpr list, ..] } insert:
                    foreach (var argument in insert.Arguments.Skip(1)) Visit(argument, list.Name);
                    return;
                case LuaCallExpr { Target: LuaNameExpr or LuaIndexExpr { Target: LuaNameExpr, Key: LuaStringExpr } } call:
                    string callee = call.Target is LuaIndexExpr { Target: LuaNameExpr owner, Key: LuaStringExpr member }
                        ? $"{owner.Name}.{member.Value}" : ((LuaNameExpr)call.Target).Name;
                    foreach (var argument in call.Arguments) Visit(argument, callee);
                    return;
                case LuaAssignStat { Targets: [LuaNameExpr name] } assign:
                    foreach (var value in assign.Values) Visit(value, name.Name);
                    return;
                case LuaLocalStat { Names: [var name] } local:
                    foreach (var value in local.Values) Visit(value, name.Name);
                    return;
            }
            foreach (var child in LuaAstWalker.Children(node)) Visit(child, context);
        }
    }
}
