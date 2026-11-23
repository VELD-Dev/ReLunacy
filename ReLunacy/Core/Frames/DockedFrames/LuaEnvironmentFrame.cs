using System.Numerics;
using ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;
using ReLunacy.Utility;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The whole Lua development environment in one window: the source editor and the bytecode
/// disassembly, as tabs or side by side.
///
/// They are views inside one frame rather than two dockable windows on purpose. The layout system
/// docks every open frame by name into the MAIN dockspace and persists that in saved layouts
/// (see DockspaceLayoutManager.TryLoadLayout), so windows docked into a nested dockspace would be
/// pulled back out the next time a layout was applied. Hosting them here gives the single-window
/// result without fighting that, and the Split toggle covers the case a sub-dockspace would have
/// been for - reading a source and its compiled output together.
/// </summary>
public class LuaEnvironmentFrame : DockedFrame, IDisposable
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private readonly LuaEditorView _editor = new();
    private readonly LuaDisassemblyView _disassembly = new();
    private readonly LuaThemeEditorView _themeEditor = new();
    private readonly LuaScriptBrowser _scripts = new();
    private bool _split;
    private bool _showScripts = true;

    /// <summary>The script list's width, and the editor's share of a split. Both are dragged by
    /// their own border rather than fixed, since how much of each is wanted depends entirely on
    /// what is being read.</summary>
    private readonly ResizablePane _scriptsPane = new(260f);
    private readonly ResizablePane _editorPane = new(0f, minimum: 200f);

    public LuaEnvironmentFrame()
    {
        FrameName = LM.Get("GUI_Frame_LuaEnvironment");
        _editor.SharedScripts = () => _scripts.SharedScripts(_editor.Path);
        _editor.SiblingSources = () => _scripts.SharedSources(_editor.Path);
        _editor.OpenScript = OpenScript;
        _editor.ScriptNames = kind => _scripts.ScriptNames(_editor.Path, kind);
        _editor.TextTags = () => ProjectTextTags.List().Select(tag => new LuaCompletionItem(
            tag.Name, tag.LuaValue, LuaCompletionKind.Variable, $"text ID {tag.Id}", null));
    }

    public bool Focused { get; private set; }
    public bool Dirty => _editor.Dirty;
    private bool _showEditor;

    public bool Save()
    {
        bool changed = _editor.Dirty;
        if (!_editor.Save()) return false;
        if (changed && _editor.Archive != null)
        {
            _scripts.Refresh();
            try
            {
                if (_scripts.Contains(_editor.Path)) LoadDisassembly(_editor.Path, _scripts.Read(_editor.Path).Chunk);
            }
            catch (Exception e) { _editor.ShowStatus(e.Message); }
        }
        return true;
    }

    public void Revert() => _editor.Revert();

    public void Dispose()
    {
        if (!Save()) { isOpen = true; return; }
        _scripts.Dispose();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Focused = false;
        base.RenderAsWindow(deltaTime);
    }

    public void LoadSource(string text, string label)
    {
        if (Save()) _editor.Load(text, label);
    }

    public void LoadChunk(byte[] bytes, string label) => _disassembly.Load(bytes, label);

    private void LoadDisassembly(string path, byte[]? chunk) =>
        _disassembly.Load(chunk, LuaScriptPackWriter.CompiledPathFor(path));

    private bool OpenScript(string path)
    {
        if (_editor.Archive != null && ReferenceEquals(_editor.Project, ProjectSession.Current)
            && path.Equals(_editor.Path, StringComparison.OrdinalIgnoreCase))
        {
            _showEditor = true;
            return true;
        }
        if (!Save()) return false;
        _scripts.Refresh();
        if (!_scripts.Contains(path)) return false;
        try
        {
            var script = _scripts.Read(path);
            _editor.Load(script.Source, path, script.Archive, ProjectSession.Current, script.Draft);
            LoadDisassembly(path, script.Chunk);
            _showEditor = true;
            return true;
        }
        catch (Exception e) { _editor.ShowStatus(e.Message); return false; }
    }

    internal (string Path, int Version)? LiveScriptVersion =>
        _editor.Archive != null && ReferenceEquals(_editor.Project, ProjectSession.Current)
            ? (_editor.Path, _editor.Document.Version) : null;

    internal LuaScriptSource WithLiveSource(LuaScriptSource source) =>
        _editor.Archive != null && ReferenceEquals(_editor.Project, ProjectSession.Current)
        && source.Path.Equals(_editor.Path, StringComparison.OrdinalIgnoreCase)
            ? source with { Text = _editor.Document.GetText() } : source;

    public void RevealScript(string path, LuaPosition at)
    {
        bool alreadyOpen = _editor.Archive != null && ReferenceEquals(_editor.Project, ProjectSession.Current)
            && path.Equals(_editor.Path, StringComparison.OrdinalIgnoreCase);
        if (alreadyOpen || OpenScript(path))
        {
            _showEditor = true;
            _editor.Reveal(at);
        }
        Focus();
    }

    protected override void Render(double deltaTime)
    {
        Focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        bool refreshed = _scripts.Refresh();
        if (_editor.Archive != null && !_editor.Dirty)
        {
            if (!ReferenceEquals(_editor.Project, ProjectSession.Current) || !_scripts.Contains(_editor.Path))
            {
                _editor.Load("", "");
                _disassembly.Load(null, "");
            }
            else if (refreshed)
            {
                try
                {
                    var script = _scripts.Read(_editor.Path);
                    if (script.Source != _editor.Document.GetText() || script.Draft != _editor.IsDraft)
                    {
                        _editor.Load(script.Source, _editor.Path, script.Archive, ProjectSession.Current, script.Draft);
                        LoadDisassembly(_editor.Path, script.Chunk);
                    }
                }
                catch (Exception e) { _editor.ShowStatus(e.Message); }
            }
        }

        if (ImGuiPlus.IconButton(Icons.FolderOpen, "toggle_lua_scripts")) _showScripts = !_showScripts;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_LuaEnv_Scripts"));
        ImGui.SameLine();
        ImGui.Checkbox(LM.Get("GUI_LuaEnv_Split"), ref _split);
        ImGui.Separator();

        if (_showScripts)
        {
            if (_scriptsPane.Begin("lua_env_scripts")) _scripts.Render(_editor.Path, Dirty, OpenScript, Save);
            ResizablePane.End();
            ImGui.SameLine();
        }

        // The remaining width holds the editor and disassembly, as tabs or side by side.
        if (!ImGui.BeginChild("lua_env_main", new Vector2(0f, 0f))) { ImGui.EndChild(); return; }
        RenderMain(deltaTime);
        ImGui.EndChild();
    }

    private void RenderMain(double deltaTime)
    {
        if (_split)
        {
            // Half and half to begin with, then wherever it is dragged to: reading a source
            // against its compiled output rarely wants the two the same width.
            if (_editorPane.Begin("lua_env_left")) _editor.Render(deltaTime, Save);
            ResizablePane.End();
            ImGui.SameLine();
            if (ImGui.BeginChild("lua_env_right", new Vector2(0f, 0f)))
                _disassembly.Render(deltaTime);
            ImGui.EndChild();
            return;
        }

        if (!ImGui.BeginTabBar("lua_env_tabs")) return;
        var editorFlags = _showEditor ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        _showEditor = false;
        if (ImGui.BeginTabItem(LM.Get("GUI_LuaEnv_TabEditor"), editorFlags))
        {
            _editor.Render(deltaTime, Save);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem(LM.Get("GUI_LuaEnv_TabDisassembly")))
        {
            _disassembly.Render(deltaTime);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem(LM.Get("GUI_LuaEnv_TabTheme")))
        {
            _themeEditor.Draw(deltaTime);
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }
}
