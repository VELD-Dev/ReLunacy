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
    private bool _split;

    public LuaEnvironmentFrame() => FrameName = LM.Get("GUI_Frame_LuaEnvironment");

    /// <summary>Opens source in the editor - how the PSARC browser will hand a script over.</summary>
    public void LoadSource(string text, string label) => _editor.Load(text, label);

    /// <summary>Opens a chunk in the disassembly, bytes straight from wherever they came from.</summary>
    public void LoadChunk(byte[] bytes, string label) => _disassembly.Load(bytes, label);

    protected override void Render(double deltaTime)
    {
        ImGui.Checkbox(LM.Get("GUI_LuaEnv_Split"), ref _split);
        ImGui.SameLine();
        ImGui.TextDisabled(LM.Get("GUI_LuaEnv_SplitHint"));
        ImGui.Separator();

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
        ImGui.EndTabBar();
    }
}
