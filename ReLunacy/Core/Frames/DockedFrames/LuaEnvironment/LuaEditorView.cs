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
    private string _path = "";
    private string? _status;
    private bool _modified;

    /// <summary>Scope analysis of the current text, refreshed when it changes. F2 and completion
    /// read this; it is recomputed lazily so typing does not reparse on every keystroke.</summary>
    private LuaScopeAnalysis? _scopes;
    private int _scopesVersion = -1;

    public string Path => _path;
    public TextDocument Document => _editor.Document;

    public LuaEditorView()
    {
        _highlighter = new LuaSyntaxHighlighter(_editor.Document);
        _editor.Highlighter = _highlighter.ForLine;
        _editor.RenameRequested = OnRenameRequested;
        // The popup claims Up/Down/Enter/Tab/Escape only while it is open, so the editor keeps them
        // the rest of the time.
        _editor.InterceptKey = key => _completion.HandleKey(key, _editor);
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

    public void Load(string text, string label)
    {
        _editor.SetText(text);
        _path = label;
        _modified = false;
        _status = null;
        _scopesVersion = -1;
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
                Load(File.ReadAllText(_path), _path);
                _status = string.Format(LM.Get("GUI_LuaEnv_Opened"), Document.LineCount);
            }
            else _status = string.Format(LM.Get("GUI_LuaEnv_NoFile"), _path);
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_LuaEnv_Save")))
        {
            try
            {
                File.WriteAllText(_path, Document.GetText());
                _modified = false;
                _status = string.Format(LM.Get("GUI_LuaEnv_Saved"), _path);
            }
            catch (Exception e) { _status = e.Message; }
        }

        var analysis = Scopes();
        ImGui.Text($"{Document.LineCount} lines  {(Document.LineEnding == "\r\n" ? "CRLF" : "LF")}"
                 + $"{(Document.EndsWithNewline ? "" : "  no trailing newline")}"
                 + $"  |  {_editor.Caret}{(_modified ? "  *" : "")}"
                 + $"  |  {analysis.Locals.Count} local(s), {analysis.Globals.Count} global(s)"
                 + $"  |  API: {Api().Functions.Count} fn, {Api().Types.Count} type(s)"
                 + (Api().Diagnostics.Count > 0 ? $", {Api().Diagnostics.Count} doc issue(s)" : ""));
        if (_status is { } s) { ImGui.SameLine(); ImGui.TextDisabled($"|  {s}"); }

        _editor.Draw("lua_editor", new Vector2(0f, -ImGui.GetFrameHeightWithSpacing()), deltaTime);

        // Ctrl+Space asks for the full list; otherwise it only follows an edit, and the engine
        // decides whether a bare prefix is worth showing.
        bool requested = ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Space);
        if (_editor.TextChanged) { _modified = true; _completion.Refresh(_editor, Api(), force: false); }
        else if (requested) _completion.Refresh(_editor, Api(), force: true);
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
