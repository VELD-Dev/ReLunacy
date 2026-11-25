using System.Numerics;
using ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;
using ReLunacy.Utility;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The whole Lua development environment in one window: a tab per open script, each with its source editor and bytecode
/// disassembly, as tabs or side by side. Scripts are listed and opened from the Assets Browser (Scripts folder).
///
/// They are views inside one frame rather than dockable windows on purpose. The layout system
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

    /// <summary>One open script: its editor and the disassembly of its compiled chunk.</summary>
    private sealed class OpenFile
    {
        public readonly LuaEditorView Editor = new();
        public readonly LuaDisassemblyView Disassembly = new();
        public bool FromArchive => Editor.Archive != null;
    }

    private readonly List<OpenFile> _files = [];
    private readonly LuaThemeEditorView _themeEditor = new();
    private static LuaScriptCatalog Scripts => LuaScriptCatalog.Shared;
    private OpenFile? _active;
    private OpenFile? _select;
    private bool _split;
    private bool _showEditor;
    private string _fileToOpen = "";
    private string? _status;

    /// <summary>The editor's share of a split, dragged by its border.</summary>
    private readonly ResizablePane _editorPane = new(0f, minimum: 200f);

    public LuaEnvironmentFrame()
    {
        FrameName = LM.Get("GUI_Frame_LuaEnvironment");
    }

    public bool Focused { get; private set; }
    public bool Dirty => _files.Any(f => f.Editor.Dirty);

    /// <summary>Saves every changed script; false when one could not be saved.</summary>
    public bool Save()
    {
        bool saved = true;
        foreach (var file in _files) saved &= Save(file);
        return saved;
    }

    private bool Save(OpenFile file)
    {
        bool changed = file.Editor.Dirty;
        if (!file.Editor.Save()) return false;
        if (changed && file.FromArchive)
        {
            Scripts.Refresh();
            try
            {
                if (Scripts.Contains(file.Editor.Path)) LoadDisassembly(file, Scripts.Read(file.Editor.Path).Chunk);
            }
            catch (Exception e) { file.Editor.ShowStatus(e.Message); }
        }
        return true;
    }

    public void Revert()
    {
        foreach (var file in _files) file.Editor.Revert();
    }

    public void Dispose()
    {
        if (!Save()) isOpen = true;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Focused = false;
        base.RenderAsWindow(deltaTime);
    }

    private static void LoadDisassembly(OpenFile file, byte[]? chunk) =>
        file.Disassembly.Load(chunk, LuaScriptPackWriter.CompiledPathFor(file.Editor.Path));

    private OpenFile? Find(string path) => _files.FirstOrDefault(f => f.FromArchive
        && ReferenceEquals(f.Editor.Project, ProjectSession.Current)
        && path.Equals(f.Editor.Path, StringComparison.OrdinalIgnoreCase));

    private OpenFile NewFile()
    {
        var file = new OpenFile();
        var editor = file.Editor;
        editor.SharedScripts = () => Scripts.SharedScripts(editor.Path);
        editor.SiblingSources = () => Scripts.SharedSources(editor.Path);
        editor.OpenScript = Show;
        editor.ScriptNames = kind => Scripts.ScriptNames(editor.Path, kind);
        editor.TextTags = () => ProjectTextTags.List().Select(tag => new LuaCompletionItem(
            tag.Name, tag.LuaValue, LuaCompletionKind.Variable, $"text ID {tag.Id}", null));
        _files.Add(file);
        return file;
    }

    private void Activate(OpenFile file)
    {
        _select = file;
        _showEditor = true;
        Focus();
    }

    /// <summary>Opens a script of the archives in its own tab, or brings its tab forward when it is open.</summary>
    public bool Show(string path)
    {
        if (Find(path) is { } open) { Activate(open); return true; }
        Scripts.Refresh();
        if (!Scripts.Contains(path)) return false;
        try
        {
            var script = Scripts.Read(path);
            var file = NewFile();
            file.Editor.Load(script.Source, path, script.Archive, ProjectSession.Current, script.Draft);
            LoadDisassembly(file, script.Chunk);
            Activate(file);
            return true;
        }
        catch (Exception e) { _status = e.Message; return false; }
    }

    private void OpenLocal(string path)
    {
        string full = Path.GetFullPath(path);
        if (_files.FirstOrDefault(f => !f.FromArchive && f.Editor.Path == full) is { } open) { Activate(open); return; }
        try
        {
            string text = LuaText.ReadFile(full);
            var file = NewFile();
            file.Editor.Load(text, full);
            Activate(file);
        }
        catch (Exception e) { _status = e.Message; }
    }

    private void Close(OpenFile file)
    {
        if (!Save(file)) { _select = file; return; }
        int at = _files.IndexOf(file);
        _files.RemoveAt(at);
        if (_active == file && _files.Count > 0) _select = _files[Math.Min(at, _files.Count - 1)];
    }

    /// <summary>A stamp of the open scripts' text, for views that read it through <see cref="WithLiveSource"/>.</summary>
    internal int LiveScriptVersion
    {
        get
        {
            var hash = new HashCode();
            foreach (var file in _files)
                if (file.FromArchive && ReferenceEquals(file.Editor.Project, ProjectSession.Current))
                {
                    hash.Add(file.Editor.Path, StringComparer.OrdinalIgnoreCase);
                    hash.Add(file.Editor.Document.Version);
                }
            return hash.ToHashCode();
        }
    }

    internal LuaScriptSource WithLiveSource(LuaScriptSource source) =>
        Find(source.Path) is { } file ? source with { Text = file.Editor.Document.GetText() } : source;

    public void RevealScript(string path, LuaPosition at)
    {
        if (!Show(path)) { Focus(); return; }
        Find(path)?.Editor.Reveal(at);
    }

    protected override void Render(double deltaTime)
    {
        Focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        SyncWithArchives();
        if (Focused) CycleTabs();

        ImGui.Checkbox(LM.Get("GUI_LuaEnv_Split"), ref _split);
        ImGui.SameLine();
        if (ImGuiPlus.IconButton(Icons.FolderOpen, "lua_scripts_browser")) AssetsBrowserFrame.RevealFolder("Scripts");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_LuaEnv_Scripts"));
        ImGui.SameLine();
        if (ImGuiPlus.IconButton(Icons.File, "lua_open_local")) ImGui.OpenPopup("lua_open_local_popup");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_LuaEnv_OpenLocal"));
        if (ImGui.BeginPopup("lua_open_local_popup"))
        {
            ImGui.SetNextItemWidth(360f);
            bool enter = ImGui.InputText(LM.Get("GUI_LuaEnv_Path"), ref _fileToOpen, 1024, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            if ((ImGui.Button(LM.Get("GUI_LuaEnv_Open")) || enter) && _fileToOpen.Length > 0)
            {
                OpenLocal(_fileToOpen);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (_status is { } status) ImGui.TextWrapped(status);

        if (_files.Count == 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled(LM.Get("GUI_LuaEnv_NoScripts"));
            RenderTheme(deltaTime);
            return;
        }

        if (!ImGui.BeginTabBar("lua_env_files", ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.FittingPolicyScroll
                | ImGuiTabBarFlags.ListPopupButton)) return;
        OpenFile? closed = null;
        foreach (var file in _files.ToList())
        {
            var flags = file.Editor.Dirty ? ImGuiTabItemFlags.UnsavedDocument : ImGuiTabItemFlags.None;
            if (_select == file) flags |= ImGuiTabItemFlags.SetSelected;
            bool open = true;
            string name = file.Editor.Path.Length > 0 ? Path.GetFileName(file.Editor.Path) : LM.Get("GUI_LuaEnv_Untitled");
            if (file.Editor.IsDraft) name += " " + LM.Get("GUI_LuaEnv_DraftName");
            bool visible = ImGui.BeginTabItem($"{name}###{file.GetHashCode()}", ref open, flags);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(file.Editor.Path);
            if (visible)
            {
                _active = file;
                RenderFile(file, deltaTime);
                ImGui.EndTabItem();
            }
            if (!open) closed = file;
        }
        _select = null;
        ImGui.EndTabBar();
        if (closed != null) Close(closed);
    }

    /// <summary>Follows the archives under each unchanged script: closed when the project or the script went away,
    /// reloaded when a save elsewhere changed it.</summary>
    private void SyncWithArchives()
    {
        bool refreshed = Scripts.Refresh();
        foreach (var file in _files.ToList())
        {
            if (!file.FromArchive || file.Editor.Dirty) continue;
            if (!ReferenceEquals(file.Editor.Project, ProjectSession.Current) || !Scripts.Contains(file.Editor.Path))
            {
                _files.Remove(file);
                continue;
            }
            if (!refreshed) continue;
            try
            {
                var script = Scripts.Read(file.Editor.Path);
                if (script.Source != file.Editor.Document.GetText() || script.Draft != file.Editor.IsDraft)
                {
                    file.Editor.Load(script.Source, file.Editor.Path, script.Archive, ProjectSession.Current, script.Draft);
                    LoadDisassembly(file, script.Chunk);
                }
            }
            catch (Exception e) { file.Editor.ShowStatus(e.Message); }
        }
    }

    /// <summary>Ctrl+PageDown / Ctrl+PageUp step through the open scripts.</summary>
    private void CycleTabs()
    {
        if (_active == null || _files.Count < 2 || !ImGui.GetIO().KeyCtrl) return;
        int step = ImGui.IsKeyPressed(ImGuiKey.PageDown) ? 1 : ImGui.IsKeyPressed(ImGuiKey.PageUp) ? -1 : 0;
        if (step == 0) return;
        int at = _files.IndexOf(_active);
        _select = _files[(at + step + _files.Count) % _files.Count];
    }

    private void RenderFile(OpenFile file, double deltaTime)
    {
        if (_split)
        {
            // Half and half to begin with, then wherever it is dragged to: reading a source
            // against its compiled output rarely wants the two the same width.
            if (_editorPane.Begin("lua_env_left")) file.Editor.Render(deltaTime, () => Save(file));
            ResizablePane.End();
            ImGui.SameLine();
            if (ImGui.BeginChild("lua_env_right", new Vector2(0f, 0f)))
                file.Disassembly.Render(deltaTime);
            ImGui.EndChild();
            return;
        }

        if (!ImGui.BeginTabBar("lua_env_tabs")) return;
        var editorFlags = _showEditor ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        _showEditor = false;
        if (ImGui.BeginTabItem(LM.Get("GUI_LuaEnv_TabEditor"), editorFlags))
        {
            file.Editor.Render(deltaTime, () => Save(file));
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem(LM.Get("GUI_LuaEnv_TabDisassembly")))
        {
            file.Disassembly.Render(deltaTime);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem(LM.Get("GUI_LuaEnv_TabTheme")))
        {
            _themeEditor.Draw(deltaTime);
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void RenderTheme(double deltaTime)
    {
        if (!ImGui.CollapsingHeader(LM.Get("GUI_LuaEnv_TabTheme"))) return;
        _themeEditor.Draw(deltaTime);
    }
}
