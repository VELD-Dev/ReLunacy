using System.Numerics;
using ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;
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
public class LuaEnvironmentFrame : DockedFrame
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

    public LuaEnvironmentFrame()
    {
        FrameName = LM.Get("GUI_Frame_LuaEnvironment");
        // Picking a script fills both views: the source to edit, and its compiled counterpart to
        // read beside it. The .lc is found by the path mapping verified across all 524 chunks.
        _scripts.ScriptSelected = (path, source, compiled) =>
        {
            _editor.Load(source, path);
            if (compiled is not null) _disassembly.Load(compiled, LuaScriptBrowser.CompiledPathFor(path));
        };
        // The level's other scripts share one global table at runtime, so a function declared in
        // any of them is callable from the file being edited. The browser already has the archive
        // open, so it is what indexes them.
        _editor.SharedScripts = _scripts.SharedScripts;
    }

    /// <summary>Opens source in the editor - how the PSARC browser will hand a script over.</summary>
    public void LoadSource(string text, string label) => _editor.Load(text, label);

    /// <summary>Opens a chunk in the disassembly, bytes straight from wherever they came from.</summary>
    public void LoadChunk(byte[] bytes, string label) => _disassembly.Load(bytes, label);

    protected override void Render(double deltaTime)
    {
        ImGui.Checkbox(LM.Get("GUI_LuaEnv_Scripts"), ref _showScripts);
        ImGui.SameLine();
        ImGui.Checkbox(LM.Get("GUI_LuaEnv_Split"), ref _split);
        ImGui.SameLine();
        ImGui.TextDisabled(LM.Get("GUI_LuaEnv_SplitHint"));
        ImGui.Separator();

        if (_showScripts)
        {
            if (ImGui.BeginChild("lua_env_scripts", new Vector2(260f, 0f), ImGuiChildFlags.Borders
                                                                        | ImGuiChildFlags.ResizeX))
                _scripts.Render();
            ImGui.EndChild();
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
            float half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
            if (ImGui.BeginChild("lua_env_left", new Vector2(half, 0f)))
                _editor.Render(deltaTime);
            ImGui.EndChild();
            ImGui.SameLine();
            if (ImGui.BeginChild("lua_env_right", new Vector2(0f, 0f)))
                _disassembly.Render(deltaTime);
            ImGui.EndChild();
            return;
        }

        if (!ImGui.BeginTabBar("lua_env_tabs")) return;
        if (ImGui.BeginTabItem(LM.Get("GUI_LuaEnv_TabEditor")))
        {
            _editor.Render(deltaTime);
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
