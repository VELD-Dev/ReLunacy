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
                                        _editor.AutoPairExitAvailable) switch
            {
                LuaEditorKeyOwner.AutoPair => CloseCompletionAnd(_editor.ExitAutoPair),
                // Moving to the next stop leaves the popup listing matches for the word left
                // behind, over a caret that is no longer there.
                LuaEditorKeyOwner.Snippet => CloseCompletionAnd(() => HandleSnippetKey(key)),
                LuaEditorKeyOwner.Completion => _completion.HandleKey(key, _editor),
                _ => false,
            };
        _editor.WordCompleted = TryExpandSnippet;
        // Enter indents into a block it is opening. The decision needs the lexer, so it lives in the
        // Lua layer rather than in the control.
        _editor.OpensScope = caret => LuaIndentRules.OpensScope(_editor.Document.GetText(), caret);
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
        // offered for a script that came out of the archive in the first place.
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

        _editor.Draw("lua_editor", new Vector2(0f, -ImGui.GetFrameHeightWithSpacing()), deltaTime);

        // Ctrl+Space asks for the full list; otherwise it only follows an edit, and the engine
        // decides whether a bare prefix is worth showing.
        bool requested = ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Space);
        if (_editor.TextChanged) { _modified = true; _completion.Refresh(_editor, Api(), false, Scripts()); }
        else if (requested) _completion.Refresh(_editor, Api(), true, Scripts());
        _completion.Draw(_editor);

        ImGui.TextDisabled(LM.Get("GUI_LuaEnv_Hint"));
        RenamePopup();
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
