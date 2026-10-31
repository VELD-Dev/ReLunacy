namespace ReLunacy.Engine.Scripting;

/// <summary>Where a declaration was found, which decides precedence and how it is labelled.</summary>
public enum LuaScriptFunctionOrigin
{
    /// <summary>Declared in the file being edited.</summary>
    CurrentFile,
    /// <summary>
    /// Declared in another script of the same level. These games load every script of a level into
    /// one shared global table, so a function declared in <c>utility.lua</c> is callable from the
    /// rest of the level with nothing importing it - 32 of the 103 global functions the shipped
    /// corpus declares are called from a file other than the one declaring them.
    /// </summary>
    Sibling,
}

/// <summary>A function declared in Lua source, as opposed to one documented in a stub file.</summary>
/// <param name="NameSpan">
/// Where the name itself is written, as opposed to where the statement starts. Completion needs it
/// to recognise the declaration being typed: at <c>function my_f|</c> the index already holds
/// <c>my_f</c>, and offering it back is both useless and harmful - it matches the prefix exactly,
/// so it opens the popup over the stop being filled in.
/// </param>
public sealed record LuaScriptFunction(
    LuaApiFunction Function, LuaScriptFunctionOrigin Origin, bool IsLocal, LuaSpan NameSpan)
{
    /// <summary>The name a call site writes: <c>do_thing</c>, or <c>helpers.do_thing</c>.</summary>
    public string QualifiedName => Function.OwnerType is null
        ? Function.Name
        : Function.OwnerType + (Function.IsMethod ? ":" : ".") + Function.Name;
}

/// <summary>A Lua file's path and contents.</summary>
public readonly record struct LuaScriptSource(string Path, string Text);

/// <summary>
/// The functions a set of Lua sources declares, with whatever <c>---</c> annotations sit above
/// them.
///
/// This is the other half of <see cref="LuaApiDatabase"/>. That one holds the engine's API, which
/// Lua only ever calls and never defines, so it has to be hand-documented. This one holds what the
/// scripts themselves define - 183 declarations across the shipped corpus - which needs no
/// documenting to be discovered, only parsing. Without it a function the file declares a few lines
/// up is indistinguishable from an undocumented engine global: no signature, no parameter names,
/// and any <c>---@param</c> written above it ignored.
///
/// Annotations are read with <see cref="LuaDocParser.ReadBlocks"/>, the same reader the stub files
/// use, so a tag means the same thing in a script as it does in the documentation.
/// </summary>
public sealed class LuaScriptIndex
{
    private readonly Dictionary<string, LuaScriptFunction> _functions = new(StringComparer.Ordinal);
    private readonly List<LuaDocDiagnostic> _diagnostics = [];
    private readonly List<string> _files = [];

    /// <summary>Declared functions by the name a call site writes.</summary>
    public IReadOnlyDictionary<string, LuaScriptFunction> Functions => _functions;

    /// <summary>Problems in annotations written above declarations - a <c>@param</c> naming an
    /// argument that is not there, an unknown tag.</summary>
    public IReadOnlyList<LuaDocDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Every file that contributed, current file first.</summary>
    public IReadOnlyList<string> Files => _files;

    public bool IsEmpty => _functions.Count == 0;

    /// <summary>
    /// Functions reachable by a plain name from the file being edited: the globals, plus the
    /// current file's own locals. Another file's locals are deliberately absent - they are not in
    /// scope here, and offering them would suggest code that does not compile.
    /// </summary>
    public IEnumerable<LuaScriptFunction> CallableByName =>
        _functions.Values.Where(f => f.Function.OwnerType is null
                                  && (!f.IsLocal || f.Origin == LuaScriptFunctionOrigin.CurrentFile));

    public LuaScriptFunction? Find(string name) => _functions.GetValueOrDefault(name);

    /// <summary>
    /// Indexes the file being edited, then its siblings.
    ///
    /// The order is the precedence: a name declared in the current file wins over one of the same
    /// name elsewhere, because that is the declaration the person is looking at.
    ///
    /// There is deliberately no <c>require</c> handling. The shipped corpus contains not one
    /// <c>require</c>, <c>dofile</c> or <c>loadfile</c> call, so whether the game's Lua environment
    /// even provides them is unknown - and following imports would mean inventing a search path
    /// that nothing could be checked against. Sharing through the level's global table is the
    /// mechanism the scripts demonstrably use.
    /// </summary>
    /// <param name="current">The file being edited.</param>
    /// <param name="shared">
    /// A prebuilt index of the level's other scripts, from <see cref="BuildShared"/>. Kept separate
    /// because this runs on every edit while that one does not: re-parsing the siblings per
    /// keystroke would cost as much as parsing the whole corpus, which measures around half a
    /// second.
    /// </param>
    public static LuaScriptIndex Build(LuaScriptSource current, LuaScriptIndex? shared = null)
    {
        var index = new LuaScriptIndex();
        index.AddSource(current, LuaScriptFunctionOrigin.CurrentFile);

        // Already-parsed entries, copied in behind the current file's own so a local declaration
        // keeps precedence.
        foreach (var (name, entry) in shared?._functions ?? [])
            if (!string.Equals(entry.Function.SourceFile, current.Path, StringComparison.OrdinalIgnoreCase))
                index._functions.TryAdd(name, entry);
        foreach (string file in shared?._files ?? [])
            if (!string.Equals(file, current.Path, StringComparison.OrdinalIgnoreCase))
                index._files.Add(file);

        return index;
    }

    /// <summary>
    /// Indexes the scripts that share a level's global table, for reuse across edits.
    ///
    /// Annotation diagnostics from these are kept out: a problem in another file is not something
    /// to report while someone is editing this one.
    /// </summary>
    public static LuaScriptIndex BuildShared(IEnumerable<LuaScriptSource> sources)
    {
        var index = new LuaScriptIndex();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            if (seen.Add(source.Path))
                index.AddSource(source, LuaScriptFunctionOrigin.Sibling);
        index._diagnostics.Clear();
        return index;
    }

    /// <summary>Indexes one source.</summary>
    private void AddSource(LuaScriptSource source, LuaScriptFunctionOrigin origin)
    {
        _files.Add(source.Path);

        LuaBlock block;
        Dictionary<int, LuaAnnotationBlock> blocks;
        try
        {
            // Parse diagnostics are not collected: the file being edited is mid-keystroke most of
            // the time, and the editor reports syntax separately. What matters is that the parser
            // recovers, so the declarations it did manage to read are still indexed.
            block = LuaParser.Parse(source.Text, out _);
            blocks = LuaDocParser.ReadBlocks(source.Text, source.Path, _diagnostics);
        }
        catch
        {
            return;
        }

        foreach (var node in LuaAstWalker.Walk(block))
            switch (node)
            {
                case LuaFunctionStat declaration:
                    AddDeclaration(declaration.Target, declaration.IsMethod, declaration.Function,
                                   declaration.Span.Start, source, origin, isLocal: false, blocks);
                    break;

                case LuaLocalFunctionStat declaration:
                    Add(declaration.Name.Name, null, false, declaration.Function,
                        declaration.Span.Start, source, origin, isLocal: true, blocks,
                        declaration.Name.Span);
                    break;

                // `Name = function() ... end`, the other spelling of a declaration.
                case LuaAssignStat { Targets: [var target], Values: [LuaFunctionExpr value] } assign:
                    AddDeclaration(target, false, value, assign.Span.Start, source, origin,
                                   isLocal: false, blocks);
                    break;

                case LuaLocalStat { Names: [var name], Values: [LuaFunctionExpr value] } local:
                    Add(name.Name, null, false, value, local.Span.Start, source, origin,
                        isLocal: true, blocks, name.Span);
                    break;
            }
    }

    /// <summary>Splits a declaration target into owner and name: <c>function helpers.reset()</c>.</summary>
    private void AddDeclaration(
        LuaExpr target, bool isMethod, LuaFunctionExpr function, LuaPosition at,
        LuaScriptSource source, LuaScriptFunctionOrigin origin, bool isLocal,
        Dictionary<int, LuaAnnotationBlock> blocks)
    {
        switch (target)
        {
            case LuaNameExpr plain:
                Add(plain.Name, null, false, function, at, source, origin, isLocal, blocks,
                    plain.Span);
                break;

            case LuaIndexExpr { Key: LuaStringExpr key } index:
            {
                string? owner = index.Target switch
                {
                    LuaNameExpr name => name.Name,
                    LuaIndexExpr { Key: LuaStringExpr inner } => inner.Value,
                    _ => null,
                };
                if (owner is not null)
                    Add(key.Value, owner, isMethod, function, at, source, origin, isLocal, blocks,
                        index.Key.Span);
                break;
            }
        }
    }

    private void Add(
        string name, string? owner, bool isMethod, LuaFunctionExpr function, LuaPosition at,
        LuaScriptSource source, LuaScriptFunctionOrigin origin, bool isLocal,
        Dictionary<int, LuaAnnotationBlock> blocks, LuaSpan nameSpan)
    {
        // A half-typed `function (((` parses to a declaration with no name. Indexing it would put a
        // blank entry in the completion list, so it is dropped rather than shown.
        if (string.IsNullOrEmpty(name)) return;

        // The block immediately above the declaration, if there is one. Undocumented declarations
        // are indexed too - the signature alone is already more than the editor had.
        var annotations = blocks.GetValueOrDefault(at.Line - 1);

        var parameters = LuaDocParser.MergeParameters(
            annotations, function, source.Path, name, _diagnostics.Add, reportUndocumented: false);

        var entry = new LuaScriptFunction(
            new LuaApiFunction(name, parameters, [.. annotations?.Returns ?? []],
                               annotations?.Description, owner, isMethod, source.Path, at),
            origin, isLocal, nameSpan);

        // First declaration wins, which given the build order means the current file beats a
        // sibling. A name declared twice in one file is the later one
        // overwriting the earlier at runtime, but the first is the one the reader is most likely
        // looking at, and guessing differently would need flow analysis.
        _functions.TryAdd(entry.QualifiedName, entry);
    }
}
