using System.Numerics;
using ReLunacy.Engine.Scripting;
using ReLunacy.Engine.Projects;
using ReLunacy.Core.Project;
using ReLunacy.Utility;
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
    private string _savedText = "";
    private IReadOnlyList<string> _saveProblems = [];
    private LuaScriptIndex? _lastShared;

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

    /// <summary>The level's sibling sources, for searching them. Separate from
    /// <see cref="SharedScripts"/> because that one hands over declarations, and a reference search
    /// needs the text they were read from.</summary>
    public Func<IReadOnlyList<LuaScriptSource>>? SiblingSources { get; set; }

    /// <summary>Opens another script, for a declaration or a reference that is not in this file.
    /// Null means there is nowhere to go, and the view says so rather than doing nothing.</summary>
    public Func<string, bool>? OpenScript { get; set; }

    /// <summary>
    /// Script names to offer inside a string argument - what <c>load_level_script("</c> completes
    /// to. A hook because only whatever holds the archive knows which scripts exist.
    /// </summary>
    public Func<LuaScriptNameKind, IEnumerable<string>>? ScriptNames { get; set; }

    public Func<IEnumerable<LuaCompletionItem>>? TextTags { get; set; }
    public string? Archive { get; private set; }
    public LunaProject? Project { get; private set; }
    public bool IsDraft { get; private set; }

    public string Path => _path;
    public TextDocument Document => _editor.Document;
    private int _dirtyVersion = -1;
    private bool _dirty;
    public bool Dirty
    {
        get
        {
            if (_dirtyVersion != Document.Version)
            {
                _dirty = _path.Length > 0 && _savedText != Document.GetText();
                _dirtyVersion = Document.Version;
            }
            return _dirty;
        }
    }

    public void ShowStatus(string status) => _status = status;

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
                if (!OpenScript(hit.Path)) return;
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
            if (!OpenScript(declaration.Path)) return;
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
        var shared = SharedScripts?.Invoke();
        if (_scriptIndex is null || _scriptIndexVersion != Document.Version || !ReferenceEquals(shared, _lastShared))
        {
            _scriptIndex = LuaScriptIndex.Build(new LuaScriptSource(_path, Document.GetText()), shared);
            _scriptIndexVersion = Document.Version;
            _lastShared = shared;
        }
        return _scriptIndex;
    }

    /// <summary>Reloads the API documentation when the loaded game changes. Kept lazy: the database
    /// is per game, and a session may never open a level at all.</summary>
    private LuaApiDatabase Api()
    {
        var game = ProjectSession.Game;
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

    public void Load(string text, string label, string? archive = null, LunaProject? project = null, bool isDraft = false)
    {
        _editor.SetText(text);
        _path = label;
        Archive = archive;
        Project = project;
        IsDraft = isDraft;
        _savedText = text;
        _dirtyVersion = -1;
        _status = null;
        _saveProblems = [];
        if (isDraft)
        {
            LuaParser.Parse(text, out var diagnostics);
            _saveProblems = diagnostics.Select(d => $"{d.Span.Start}: {d.Message}").ToArray();
        }
        _scopesVersion = -1;
        _scriptIndexVersion = -1;
        _snippet = null;
        _completion.Close();
        _search.Close();
        _references.Clear();
    }

    public void Revert() => Load(_savedText, _path, Archive, Project, IsDraft);

    public bool Save()
    {
        if (!Dirty) return true;
        try
        {
            string source = Document.GetText();
            if (Archive is { } archive)
            {
                var result = LuaScriptCatalog.Save(archive, _path, source, Project);
                _status = result.Summary;
                _saveProblems = result.Problems;
                if (!result.Succeeded && (!result.CompileFailed || Project == null)) return false;
                Project?.SetLuaDraft(archive, _path, result.CompileFailed ? source : null);
                IsDraft = result.CompileFailed;
                if (IsDraft) _status = null;
            }
            else
            {
                File.WriteAllBytes(_path, LuaText.ToBytes(source));
                _status = string.Format(LM.Get("GUI_LuaEnv_Saved"), _path);
            }
            _savedText = source;
            _dirty = false;
            if (!IsDraft) _saveProblems = [];
            return true;
        }
        catch (Exception e) { _status = e.Message; return false; }
    }

    public void Reveal(LuaPosition position)
    {
        var caret = new TextPosition(position.Line, position.Column);
        _editor.Reveal(new TextRange(caret, caret));
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

    public void Render(double deltaTime, Func<bool> save)
    {
        ImGui.BeginDisabled(!Dirty);
        if (ImGuiPlus.IconButton(Icons.Save, "save_lua")) save();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(LM.Get("GUI_LuaEnv_Save") + " (Ctrl+S)");
        ImGui.SameLine();
        if (_path.Length > 0)
        {
            ImGui.TextUnformatted($"{(Dirty ? "· " : "")}{System.IO.Path.GetFileName(_path)}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(_path);
        }
        else ImGui.TextDisabled(LM.Get("GUI_LuaEnv_SelectScript"));

        _editor.ReadOnly = _path.Length == 0 || (Archive != null && Project == null);
        if (Archive != null && Project == null) ImGui.TextDisabled(LM.Get("GUI_Install_NoProject"));
        _highlighter.Configure(Theme(), Api(), Scripts());

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

        if (IsDraft) ImGui.TextWrapped(LM.Get("GUI_LuaEnv_DraftSaved"));
        if (_status is { } s) ImGui.TextWrapped(s);

        foreach (string problem in _saveProblems.Take(6))
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
        bool requested = _editor.HasFocus && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Space);
        if (_editor.TextChanged)
        {
            _completion.Refresh(_editor, Api(), false, Scripts(), ScriptNames, TextTags);
        }
        else if (requested) _completion.Refresh(_editor, Api(), true, Scripts(), ScriptNames, TextTags);
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

        // One line says everything the heading and the signature used to say between them:
        // (function) wait(duration?: number): nil
        SignatureView.Draw(LuaSignature.Header(documented.Kind, documented.Label, documented.Detail),
                           documented.Kind, Theme());

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
