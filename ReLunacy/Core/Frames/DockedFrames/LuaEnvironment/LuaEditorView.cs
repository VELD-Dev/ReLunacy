using System.Numerics;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility.CodeEditor;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>
/// The Lua source editor, as a view rather than a window so the Lua environment can host it beside
/// the disassembly. Owns opening, saving and the editor control.
///
/// Saving writes the document's own text back, which keeps the file's line endings, tabs and
/// trailing-newline state - the shipped scripts are CRLF with tabs and a fifth have no final
/// newline, and these get recompiled and written into the archive, so a normalising save corrupts
/// them rather than merely reformatting.
/// </summary>
public sealed class LuaEditorView
{
    private readonly CodeEditorControl _editor = new();
    private readonly LuaSyntaxHighlighter _highlighter;
    private readonly LuaCompletionPopup _completion = new();
    private readonly LuaSearchBar _search = new();
    private readonly LuaReferencesView _references = new();
    private LuaApiDatabase _api = new();
    private ReLunacy.Engine.Games.GameId? _apiGame;
    private LuaEditorTheme _theme = new LuaEditorTheme().Resolve();
    private LuaSnippetSession? _snippet;
    private string _path = "";
    private string? _status;
    private bool _modified;

    /// <summary>Why an install was refused, shown under the status line so a compile error is
    /// readable rather than summarised away.</summary>
    private IReadOnlyList<string> _installProblems = [];

    /// <summary>Scope analysis of the current text, refreshed when it changes. F2 and completion
    /// read this; it is recomputed lazily so typing does not reparse on every keystroke.</summary>
    private LuaScopeAnalysis? _scopes;
    private int _scopesVersion = -1;

    /// <summary>The functions this file and the level's other scripts declare, refreshed with the
    /// text. Only this file is reparsed per edit; the siblings come prebuilt.</summary>
    private LuaScriptIndex? _scriptIndex;
    private int _scriptIndexVersion = -1;

    /// <summary>
    /// Supplies the level's already-indexed scripts. A hook rather than a reference to the browser,
    /// so the editor does not need to know an archive exists.
    /// </summary>
    public Func<LuaScriptIndex?>? SharedScripts { get; set; }

    /// <summary>
    /// The archive a script came from, for writing one back. A hook for the same reason as
    /// <see cref="SharedScripts"/>: the editor does not need to know how an archive is found.
    /// </summary>
    public Func<string?>? ArchivePath { get; set; }

    /// <summary>The level's sibling sources, for searching them. Separate from
    /// <see cref="SharedScripts"/> because that one hands over declarations, and a reference search
    /// needs the text they were read from.</summary>
    public Func<IReadOnlyList<LuaScriptSource>>? SiblingSources { get; set; }

    /// <summary>Opens another script, for a declaration or a reference that is not in this file.
    /// Null means there is nowhere to go, and the view says so rather than doing nothing.</summary>
    public Action<string>? OpenScript { get; set; }

    /// <summary>
    /// Script names to offer inside a string argument - what <c>load_level_script("</c> completes
    /// to. A hook because only whatever holds the archive knows which scripts exist.
    /// </summary>
    public Func<LuaScriptNameKind, IEnumerable<string>>? ScriptNames { get; set; }

    /// <summary>Raised after the archive has been written, so whatever holds it open can reopen it
    /// - a handle kept across the write keeps reading the file as it was.</summary>
    public Action? Installed { get; set; }

    /// <summary>Whether the archive holds this path, which is to say the script came from one
    /// rather than from the filesystem.</summary>
    private bool CameFromArchive => _path.StartsWith('/');

    public string Path => _path;
    public TextDocument Document => _editor.Document;

    public LuaEditorView()
    {
        _highlighter = new LuaSyntaxHighlighter(_editor.Document);
        _editor.Highlighter = _highlighter.ForLine;
        _editor.RenameRequested = OnRenameRequested;
        // Who gets a key is decided by LuaEditorKeyRouting, so the one priority rule that matters
        // is testable rather than implicit in the order of these calls.
        _editor.InterceptKey = key =>
            LuaEditorKeyRouting.OwnerOf(key, _snippet is { IsActive: true }, _completion.IsOpen,
                                        _editor.AutoPairExitAvailable, _search.IsOpen) switch
            {
                LuaEditorKeyOwner.AutoPair => CloseCompletionAnd(_editor.ExitAutoPair),
                // Moving to the next stop leaves the popup listing matches for the word left
                // behind, over a caret that is no longer there.
                LuaEditorKeyOwner.Snippet => CloseCompletionAnd(() => HandleSnippetKey(key)),
                LuaEditorKeyOwner.Completion => _completion.HandleKey(key, _editor),
                LuaEditorKeyOwner.Search => _search.HandleKey(key, _editor),
                _ => false,
            };
        _editor.WordCompleted = TryExpandSnippet;
        // Enter indents into a block it is opening. The decision needs the lexer, so it lives in the
        // Lua layer rather than in the control.
        _editor.OpensScope = caret => LuaIndentRules.OpensScope(_editor.Document.GetText(), caret);
        _editor.Backgrounds = Decorations;
        _editor.ContextMenuRequested = _ => _openContextMenu = true;

        // Picking a result moves the caret to it, opening the script first when it is elsewhere.
        _references.Activated = hit =>
        {
            if (!string.Equals(hit.Path, _path, StringComparison.OrdinalIgnoreCase))
            {
                if (OpenScript is null) { _status = string.Format(LM.Get("GUI_LuaEnv_DeclNone"), hit.Path); return; }
                OpenScript(hit.Path);
            }
            Reveal(hit.Span);
        };
    }

    private bool _openContextMenu;

    /// <summary>Selects a span and brings it into view - what a declaration or a reference wants.
    /// The span's end is exclusive, which is what <see cref="TextRange"/> already means.</summary>
    private void Reveal(LuaSpan span) =>
        _editor.Reveal(new TextRange(new TextPosition(span.Start.Line, span.Start.Column),
                                     new TextPosition(span.End.Line, span.End.Column)));

    /// <summary>
    /// The right-click menu. Only the things that need the caret's position are here; anything
    /// reachable from a key is listed with its key so the menu teaches it rather than replacing it.
    /// </summary>
    private void ContextMenu()
    {
        if (_openContextMenu) { ImGui.OpenPopup("lua_context"); _openContextMenu = false; }
        if (!ImGui.BeginPopup("lua_context")) return;

        string word = _editor.WordTextAt(_editor.Caret);
        if (word.Length > 0) ImGui.TextDisabled(word);
        else ImGui.TextDisabled(LM.Get("GUI_LuaEnv_MenuFind"));
        ImGui.Separator();

        ImGui.BeginDisabled(word.Length == 0);
        if (ImGui.MenuItem(LM.Get("GUI_LuaEnv_MenuDeclaration"))) GoToDeclaration();
        if (ImGui.MenuItem(LM.Get("GUI_LuaEnv_MenuReferences"))) FindReferences();
        if (ImGui.MenuItem(LM.Get("GUI_LuaEnv_MenuRename")))
            OnRenameRequested(_editor.Caret, word);
        ImGui.EndDisabled();

        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_LuaEnv_MenuFind"))) _search.Open(_editor);

        ImGui.EndPopup();
    }

    private LuaPosition CaretPosition => new(_editor.Caret.Line, _editor.Caret.Column);

    /// <summary>
    /// A script's text for colouring a result row. The file being edited comes from the document
    /// rather than the archive, so a row reflects unsaved edits instead of the shipped bytes.
    /// </summary>
    private string? SourceOf(string path)
    {
        if (string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) return _editor.Document.GetText();
        foreach (var sibling in SiblingSources?.Invoke() ?? [])
            if (string.Equals(sibling.Path, path, StringComparison.OrdinalIgnoreCase)) return sibling.Text;
        return null;
    }

    private void GoToDeclaration()
    {
        string word = _editor.WordTextAt(_editor.Caret);
        var declaration = LuaReferenceSearch.Declaration(
            _path, _editor.Document.GetText(), CaretPosition, Api(), Scripts());

        if (declaration is null)
        {
            _status = string.Format(LM.Get("GUI_LuaEnv_DeclNone"), word);
            return;
        }
        if (!declaration.InArchive)
        {
            _status = string.Format(LM.Get("GUI_LuaEnv_DeclDocumented"), word,
                                    System.IO.Path.GetFileName(declaration.Path));
            return;
        }

        if (!string.Equals(declaration.Path, _path, StringComparison.OrdinalIgnoreCase))
        {
            if (OpenScript is null)
            {
                _status = string.Format(LM.Get("GUI_LuaEnv_DeclDocumented"), word,
                                        System.IO.Path.GetFileName(declaration.Path));
                return;
            }
            OpenScript(declaration.Path);
        }
        Reveal(declaration.Span);
        _status = null;
    }

    private void FindReferences()
    {
        string word = _editor.WordTextAt(_editor.Caret);
        var hits = LuaReferenceSearch.Find(_path, _editor.Document.GetText(), CaretPosition,
                                           SiblingSources?.Invoke());
        if (hits.Count == 0) _references.ShowMessage(string.Format(LM.Get("GUI_LuaEnv_RefsNone"), word));
        else _references.Show(word, hits);
    }

    /// <summary>
    /// What to paint behind the text, recomputed only when something it depends on moved.
    ///
    /// Finding the enclosing pair lexes the whole file, and the biggest shipped script is 211KB, so
    /// this cannot run per frame. The theme's revision is part of the key because the theme editor
    /// previews by editing the live instance: keyed on the text and the caret alone, a colour drag
    /// would not show until the caret moved.
    /// </summary>
    private List<TextHighlight> _decorations = [];
    private (int Version, TextPosition Caret, TextPosition? Stop, int Theme,
             (bool, string, int, bool, bool) Search) _decoratedFor =
        (-1, default, null, -1, default);

    private IReadOnlyList<TextHighlight> Decorations()
    {
        var stop = _snippet?.CurrentStop;
        var key = (_editor.Document.Version, _editor.Caret, stop, Theme().Revision, _search.State);
        if (_decoratedFor == key) return _decorations;

        _decorations = LuaEditorDecorations.Build(
            _editor.Document, _editor.Caret, Theme(), stop, Scopes());
        // Search hits go on last so they are drawn over the rest: while a search is open, finding
        // the hits is what the person is doing.
        _decorations.AddRange(_search.Highlights(Theme()));
        _decoratedFor = key;
        return _decorations;
    }

    /// <summary>
    /// Pushes the live theme to everything that draws: the editor's chrome, the highlighter's roles
    /// and the popup.
    ///
    /// Compared by reference rather than by name: a colour edited in the theme editor mutates the
    /// instance in place and needs nothing here, while switching theme replaces the instance and is
    /// what has to be re-pushed.
    /// </summary>
    private LuaEditorTheme Theme()
    {
        var live = LuaEditorThemeStore.Current;
        if (!ReferenceEquals(_theme, live))
        {
            _theme = live;
            _editor.Theme = live;
            _completion.Theme = live;
            if (live.Problems.Count > 0) _status = string.Join("; ", live.Problems);
        }
        return _theme;
    }

    /// <summary>
    /// The functions declared in Lua, as opposed to documented in a stub: this file's own, plus
    /// those of the scripts sharing the level's global table.
    ///
    /// Rebuilt when the text changes, so a function declared a moment ago is already known - which
    /// is the point. The instance is reused between edits because the highlighter and the popup
    /// both compare it by reference to decide whether to redo their work.
    /// </summary>
    private LuaScriptIndex Scripts()
    {
        if (_scriptIndex is null || _scriptIndexVersion != _editor.Document.Version)
        {
            _scriptIndex = LuaScriptIndex.Build(
                new LuaScriptSource(_path, _editor.Document.GetText()), SharedScripts?.Invoke());
            _scriptIndexVersion = _editor.Document.Version;
        }
        return _scriptIndex;
    }

    /// <summary>Reloads the API documentation when the loaded game changes. Kept lazy: the database
    /// is per game, and a session may never open a level at all.</summary>
    private LuaApiDatabase Api()
    {
        var game = Core.LunaWindow.Instance?.Level?.GameDefinition?.Id;
        if (_api.IsEmpty || game != _apiGame)
        {
            _api = LuaApiLoader.Load(System.IO.Path.Combine(Program.EditorPath, "LuaApi"), game);
            _apiGame = game;
        }
        return _api;
    }

    /// <summary>A space just ended a word: expand it if it names a snippet. Returning true swallows
    /// the space, which is what makes "if " become the full block rather than the block plus a
    /// stray space.</summary>
    private bool TryExpandSnippet(string word, TextRange range)
    {
        if (LuaSnippets.ForTrigger(word) is not { } snippet) return false;

        // An expansion supersedes whatever was being suggested, and leaving the popup open over the
        // new block would cover it.
        _completion.Close();
        _snippet?.Finish();
        _snippet = LuaSnippetSession.Expand(_editor.Document, snippet, range, out var caret);
        _editor.MoveCaretTo(caret);
        _status = _snippet is null
            ? string.Format(LM.Get("GUI_LuaEnv_SnippetExpanded"), snippet.Trigger)
            : string.Format(LM.Get("GUI_LuaEnv_SnippetStops"), snippet.Trigger, _snippet.Progress.Count);
        return true;
    }

    private bool CloseCompletionAnd(Func<bool> action)
    {
        _completion.Close();
        return action();
    }

    /// <summary>Tab walks a running snippet's stops; Escape abandons it. Tab past the last stop ends
    /// the session and is handed back, so it indents as usual from then on.</summary>
    private bool HandleSnippetKey(ImGuiKey key)
    {
        if (_snippet is not { IsActive: true } session) return false;

        switch (key)
        {
            case ImGuiKey.Tab:
                if (session.MoveNext(out var next))
                {
                    _editor.MoveCaretTo(next);
                    var (index, count) = session.Progress;
                    _status = string.Format(LM.Get("GUI_LuaEnv_SnippetAt"), index, count);
                    return true;
                }
                _snippet = null;
                _status = null;
                return false;

            case ImGuiKey.Escape:
                session.Finish();
                _snippet = null;
                _status = null;
                return true;

            default:
                return false;
        }
    }

    public void Load(string text, string label)
    {
        _editor.SetText(text);
        _path = label;
        _modified = false;
        _status = null;
        _scopesVersion = -1;
        _snippet = null;
    }

    private LuaScopeAnalysis Scopes()
    {
        if (_scopes is null || _scopesVersion != Document.Version)
        {
            var block = LuaParser.Parse(Document.GetText(), out _);
            _scopes = LuaScopeResolver.Resolve(block);
            _scopesVersion = Document.Version;
        }
        return _scopes;
    }

    /// <summary>F2. Resolves the identifier under the caret through the scope tree and rewrites only
    /// the occurrences bound to that symbol - not every identifier that happens to share the name,
    /// which is what a textual replace would do and why this waited for the front-end.</summary>
    private void OnRenameRequested(TextPosition caret, string word)
    {
        if (string.IsNullOrEmpty(word)) { _status = LM.Get("GUI_LuaEnv_RenameNoWord"); return; }

        var symbol = Scopes().FindAt(new LuaPosition(caret.Line, caret.Column));
        if (symbol is null) { _status = string.Format(LM.Get("GUI_LuaEnv_RenameUnresolved"), word); return; }
        if (!symbol.IsRenameSafe)
        {
            // Globals are referenced by the other scripts and by the engine, so renaming one from
            // inside a single file would silently break callers this editor cannot see.
            _status = string.Format(LM.Get("GUI_LuaEnv_RenameGlobal"), word, symbol.References.Count);
            return;
        }

        _pendingRename = symbol;
        _renameText = symbol.Name;
        _openRenamePopup = true;
    }

    private LuaSymbol? _pendingRename;
    private string _renameText = "";
    private bool _openRenamePopup;

    public void Render(double deltaTime)
    {
        ImGui.SetNextItemWidth(-220f);
        ImGui.InputText(LM.Get("GUI_LuaEnv_Path"), ref _path, 1024);
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_LuaEnv_Open")))
        {
            if (File.Exists(_path))
            {
                Load(LuaText.ReadFile(_path), _path);
                _status = string.Format(LM.Get("GUI_LuaEnv_Opened"), Document.LineCount);
            }
            else _status = string.Format(LM.Get("GUI_LuaEnv_NoFile"), _path);
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_LuaEnv_Save")))
        {
            try
            {
                // Written as bytes, not as text: a script may hold bytes that are not valid UTF-8 -
                // 28 of the shipped ones do - and re-encoding them changes the file.
                File.WriteAllBytes(_path, LuaText.ToBytes(Document.GetText()));
                _modified = false;
                _status = string.Format(LM.Get("GUI_LuaEnv_Saved"), _path);
            }
            catch (Exception e) { _status = e.Message; }
        }

        // Writing into the game is deliberately a separate action from saving a file, and only
        // offered for an archive path - one starting with '/'. The script need not already be in
        // the archive: a path that is not there yet is added, index entries and all.
        ImGui.SameLine();
        string? archivePath = ArchivePath?.Invoke();
        ImGui.BeginDisabled(archivePath is null || !CameFromArchive);
        if (ImGui.Button(LM.Get("GUI_LuaEnv_Install")) && archivePath is not null)
        {
            var result = LuaScriptPackWriter.Save(archivePath, _path, Document.GetText());
            _installProblems = result.Problems;
            if (result.Succeeded)
            {
                _modified = false;
                Installed?.Invoke();
                _status = result.BackupPath is { } backup
                    ? string.Format(LM.Get("GUI_LuaEnv_InstalledWithBackup"), result.Summary,
                                    System.IO.Path.GetFileName(backup))
                    : result.Summary;
            }
            else _status = result.Summary;
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_LuaEnv_InstallHelp"));

        // Both feed the highlighter: the theme decides the colours, the API decides which names are
        // functions or classes at all, so a newly documented global recolours without an edit.
        _highlighter.Configure(Theme(), Api(), Scripts());

        var analysis = Scopes();
        ImGui.Text($"{Document.LineCount} lines  {(Document.LineEnding == "\r\n" ? "CRLF" : "LF")}"
                 + $"{(Document.EndsWithNewline ? "" : "  no trailing newline")}"
                 + $"  |  {_editor.Caret}{(_modified ? "  *" : "")}"
                 + $"  |  {analysis.Locals.Count} local(s), {analysis.Globals.Count} global(s)"
                 + $"  |  API: {Api().Functions.Count} fn, {Api().Types.Count} type(s)"
                 + (Api().Diagnostics.Count > 0 ? $", {Api().Diagnostics.Count} doc issue(s)" : "")
                 + $"  |  declared: {Scripts().CallableByName.Count()} fn"
                 + (Scripts().Files.Count > 1 ? $" over {Scripts().Files.Count} script(s)" : ""));

        // Problems in annotations written in THIS file, on hover. The person writing them needs to
        // find out from the editor rather than from completion quietly saying nothing.
        if (Scripts().Diagnostics.Count is var problems and > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1f),
                              string.Format(LM.Get("GUI_LuaEnv_AnnotationIssues"), problems));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(string.Join('\n', Scripts().Diagnostics.Take(10)
                    .Select(d => $"line {d.Position.Line + 1}: {d.Message}")));
        }

        if (_status is { } s) { ImGui.SameLine(); ImGui.TextDisabled($"|  {s}"); }

        foreach (string problem in _installProblems.Take(6))
            ImGui.TextColored(new Vector4(0.9f, 0.5f, 0.4f, 1f), problem);

        // Ctrl+F before the editor draws, so the bar is up on the same frame it is asked for.
        if (ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyAlt && ImGui.IsKeyPressed(ImGuiKey.F))
            _search.Open(_editor);
        _search.Render(_editor);

        // The editor gives up the bottom of the panel while results are showing.
        float reserved = ImGui.GetFrameHeightWithSpacing()
                       + (_references.HasResults ? ReferencesHeight + ImGui.GetFrameHeightWithSpacing() : 0f);
        _editor.Draw("lua_editor", new Vector2(0f, -reserved), deltaTime);
        ContextMenu();

        // Escape while the query field has focus: the editor is not focused then, so its own key
        // routing never runs and the bar would be stuck open.
        if (_search.IsOpen && !_editor.HasFocus && ImGui.IsKeyPressed(ImGuiKey.Escape))
            _search.Close();

        // Ctrl+Space asks for the full list; otherwise it only follows an edit, and the engine
        // decides whether a bare prefix is worth showing.
        bool requested = ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Space);
        if (_editor.TextChanged)
        {
            _modified = true;
            _completion.Refresh(_editor, Api(), false, Scripts(), ScriptNames);
        }
        else if (requested) _completion.Refresh(_editor, Api(), true, Scripts(), ScriptNames);
        _completion.Draw(_editor, Theme(), Api());
        HoverTooltip();
        if (_references.HasResults)
            _references.Render(ReferencesHeight, Theme(), Api(), Scripts(), SourceOf);

        ImGui.TextDisabled(LM.Get("GUI_LuaEnv_Hint"));
        RenamePopup();
    }

    /// <summary>How long the pointer has to rest on a word before it is documented.</summary>
    private const double HoverDelay = 0.45;

    /// <summary>How much of the panel the results list takes when it is showing.</summary>
    private const float ReferencesHeight = 140f;

    private readonly DocMarkdownView _markdown = new();
    private LuaHover? _hover;
    private (int Version, TextPosition Position) _hoveredFor = (-1, default);

    /// <summary>
    /// Documents the word the pointer is resting on.
    ///
    /// Suppressed while the completion popup is open: both would be anchored a few pixels apart and
    /// describe different words, which reads as a glitch rather than as two features. The lookup is
    /// cached on the text and the position because it parses the file, and the pointer sits still
    /// for many frames by definition - that being what a hover is.
    /// </summary>
    private void HoverTooltip()
    {
        if (_completion.IsOpen) return;
        if (_editor.Hover is not { } hover || hover.Seconds < HoverDelay) return;

        var key = (_editor.Document.Version, hover.Position);
        if (_hoveredFor != key)
        {
            _hover = LuaHoverEngine.At(_editor.Document.GetText(),
                                       new LuaPosition(hover.Position.Line, hover.Position.Column),
                                       Api(), Scripts());
            _hoveredFor = key;
        }
        if (_hover is not { } documented) return;

        ImGui.BeginTooltip();
        float wrap = ImGui.GetFontSize() * 28f;
        ImGui.PushTextWrapPos(wrap);
        ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(Theme().FunctionColour), documented.Label);
        ImGui.SameLine();
        ImGui.TextDisabled(documented.Kind);
        if (documented.Detail is { } detail)
        {
            ImGui.Separator();
            ImGui.TextWrapped(detail);
        }
        if (documented.Description is { } description)
        {
            ImGui.Separator();
            _markdown.Draw(description, Theme(), Api(), wrap);
        }
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private void RenamePopup()
    {
        if (_openRenamePopup) { ImGui.OpenPopup("lua_rename"); _openRenamePopup = false; }
        if (!ImGui.BeginPopup("lua_rename")) return;

        var symbol = _pendingRename;
        if (symbol is null) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }

        ImGui.Text(string.Format(LM.Get("GUI_LuaEnv_RenameTitle"), symbol.Kind, symbol.Name, symbol.References.Count));
        ImGui.SetNextItemWidth(220f);
        bool submitted = ImGui.InputText("##rename", ref _renameText, 128, ImGuiInputTextFlags.EnterReturnsTrue);
        if (submitted || ImGui.Button(LM.Get("GUI_LuaEnv_RenameApply")))
        {
            if (!string.IsNullOrWhiteSpace(_renameText) && _renameText != symbol.Name)
            {
                // Every reference in one undo-able pass, applied last-first inside the control so
                // earlier spans keep their positions.
                _editor.ReplaceRanges(symbol.References.Select(ToRange), _renameText.Trim());
                _status = string.Format(LM.Get("GUI_LuaEnv_Renamed"), symbol.Name, _renameText.Trim(),
                                        symbol.References.Count);
                _modified = true;
            }
            _pendingRename = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private static TextRange ToRange(LuaSpan span) =>
        new(new TextPosition(span.Start.Line, span.Start.Column),
            new TextPosition(span.End.Line, span.End.Column));
}
