using ReLunacy.Core;
using ReLunacy.Core.Frames;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

internal static class ViewMenuDraw
{
    internal static void ShowOverlay()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_ShowOverlay"), "", Overlay.showOverlay, true)) return;
        Overlay.showOverlay = !Overlay.showOverlay;
    }

    internal static void ShowView3D()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<View3D>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_View3D"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
            LunaWindow.Instance.TryCloseFirstFrame<View3D>();
        else
            LunaWindow.Instance.AddFrame(new View3D(LunaWindow.Instance.GraphicsDevice));
    }

    internal static void ShowLevelData()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<LevelDataFrame>();
        if (!ImGui.MenuItem(ImGuiPlus.Label(Icons.Map, LM.Get("GUI_Frame_LevelData")), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
            LunaWindow.Instance.TryCloseFirstFrame<LevelDataFrame>();
        else
            LunaWindow.Instance.AddFrame(new LevelDataFrame());
    }

    internal static void ShowProfiler()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<ProfilerFrame>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_Profiler"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
            LunaWindow.Instance.TryCloseFirstFrame<ProfilerFrame>();
        else
            LunaWindow.Instance.AddFrame(new ProfilerFrame());
    }

    internal static void ShowEntityExplorer()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<BasicEntityExplorer>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
            LunaWindow.Instance.TryCloseFirstFrame<BasicEntityExplorer>();
        else
            LunaWindow.Instance.AddFrame(new BasicEntityExplorer());
    }

    internal static void ShowInstanceInspector()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<PropertyInspectorFrame>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_InstanceInspector"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
            LunaWindow.Instance.TryCloseFirstFrame<PropertyInspectorFrame>();
        else
            LunaWindow.Instance.AddFrame(new PropertyInspectorFrame());
    }

    internal static void ShowAssetViewer()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<AssetViewer>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_AssetViewer"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
            LunaWindow.Instance.TryCloseFirstFrame<AssetViewer>();
        else
        {
            var frame = new AssetViewer(LunaWindow.Instance.GraphicsDevice);
            if (LunaWindow.Instance.AssetManager != null && LunaWindow.Instance.Level != null)
                frame.TransmitAssets(LunaWindow.Instance.AssetManager, LunaWindow.Instance.Level.Mobys, LunaWindow.Instance.Level.Ties);
            LunaWindow.Instance.AddFrame(frame);
        }
    }

    internal static void ShowTextureExplorer()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<TexturesExplorer>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_TextureExplorer"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
            LunaWindow.Instance.TryCloseFirstFrame<TexturesExplorer>();
        else
        {
            var frame = new TexturesExplorer();
            if (LunaWindow.Instance.AssetManager != null)
                frame.TransmitTextures(LunaWindow.Instance.AssetManager);
            LunaWindow.Instance.AddFrame(frame);
        }
    }

    internal static void ShowShaderBrowser()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<ShaderBrowser>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_ShaderBrowser"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
            LunaWindow.Instance.TryCloseFirstFrame<ShaderBrowser>();
        else
        {
            var frame = new ShaderBrowser();
            if (LunaWindow.Instance.Level != null)
                frame.TransmitShaders(LunaWindow.Instance.Level);
            LunaWindow.Instance.AddFrame(frame);
        }
    }

    internal static void ShowPSArcExplorer()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<PSArcExplorer>();
        if (!ImGui.MenuItem("PSArc Explorer", "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
            LunaWindow.Instance.TryCloseFirstFrame<PSArcExplorer>();
        else
            LunaWindow.Instance.AddFrame(new PSArcExplorer());
    }

    internal static void ShowConsoleFrame()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<LogsFrame>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_Logs"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
            LunaWindow.Instance.TryCloseFirstFrame<LogsFrame>();
        else
            LunaWindow.Instance.AddFrame(new LogsFrame());
    }

    /// <summary>Construct-or-return-existing dispatcher, by Frame type name, for
    /// DockspaceLayoutManager.TryLoadLayout to reopen a frame a saved layout had open that isn't
    /// currently - mirrors each Show* method's own "not open" branch above, since those are gated
    /// behind their own MenuItem (only reachable through a real click) and can't be reused directly
    /// as a callback. Only lists frame types that can actually appear in a SavedLayout.FrameDockIds
    /// (see CaptureFrameDockIds): floating/dialog-style frames (EditorSettingsFrame, GameBrowserFrame)
    /// never get an entry there since they're never docked, so there's nothing to reopen for those.
    /// Unrecognized names (a saved layout from a future build with a frame type this build doesn't
    /// know about) fall through to null - the caller just skips it.</summary>
    internal static Frame? EnsureFrameTypeOpen(string typeName)
    {
        var window = LunaWindow.Instance;
        switch (typeName)
        {
            case nameof(View3D):
                if (window.IsAnyFrameOpened<View3D>()) return window.GetFirstFrame<View3D>();
                var view3d = new View3D(window.GraphicsDevice);
                window.AddFrame(view3d);
                return view3d;

            case nameof(LevelDataFrame):
                if (window.IsAnyFrameOpened<LevelDataFrame>()) return window.GetFirstFrame<LevelDataFrame>();
                var levelData = new LevelDataFrame();
                window.AddFrame(levelData);
                return levelData;

            case nameof(ProfilerFrame):
                if (window.IsAnyFrameOpened<ProfilerFrame>()) return window.GetFirstFrame<ProfilerFrame>();
                var profiler = new ProfilerFrame();
                window.AddFrame(profiler);
                return profiler;

            case nameof(BasicEntityExplorer):
                if (window.IsAnyFrameOpened<BasicEntityExplorer>()) return window.GetFirstFrame<BasicEntityExplorer>();
                var explorer = new BasicEntityExplorer();
                window.AddFrame(explorer);
                return explorer;

            case nameof(PropertyInspectorFrame):
                if (window.IsAnyFrameOpened<PropertyInspectorFrame>()) return window.GetFirstFrame<PropertyInspectorFrame>();
                var inspector = new PropertyInspectorFrame();
                window.AddFrame(inspector);
                return inspector;

            case nameof(AssetViewer):
                if (window.IsAnyFrameOpened<AssetViewer>()) return window.GetFirstFrame<AssetViewer>();
                var assetViewer = new AssetViewer(window.GraphicsDevice);
                if (window.AssetManager != null && window.Level != null)
                    assetViewer.TransmitAssets(window.AssetManager, window.Level.Mobys, window.Level.Ties);
                window.AddFrame(assetViewer);
                return assetViewer;

            case nameof(TexturesExplorer):
                if (window.IsAnyFrameOpened<TexturesExplorer>()) return window.GetFirstFrame<TexturesExplorer>();
                var textureExplorer = new TexturesExplorer();
                if (window.AssetManager != null)
                    textureExplorer.TransmitTextures(window.AssetManager);
                window.AddFrame(textureExplorer);
                return textureExplorer;

            case nameof(ShaderBrowser):
                if (window.IsAnyFrameOpened<ShaderBrowser>()) return window.GetFirstFrame<ShaderBrowser>();
                var shaderBrowser = new ShaderBrowser();
                if (window.Level != null)
                    shaderBrowser.TransmitShaders(window.Level);
                window.AddFrame(shaderBrowser);
                return shaderBrowser;

            case nameof(PSArcExplorer):
                if (window.IsAnyFrameOpened<PSArcExplorer>()) return window.GetFirstFrame<PSArcExplorer>();
                var psArcExplorer = new PSArcExplorer();
                window.AddFrame(psArcExplorer);
                return psArcExplorer;

            case nameof(LogsFrame):
                if (window.IsAnyFrameOpened<LogsFrame>()) return window.GetFirstFrame<LogsFrame>();
                var logs = new LogsFrame();
                window.AddFrame(logs);
                return logs;

            default:
                return null;
        }
    }

    private static string _newLayoutName = string.Empty;
    private static bool _wantOpenSaveLayoutPopup;

    internal static void LayoutPresets()
    {
        var settings = LunaWindow.Instance.EditorSettings;

        if (ImGui.BeginMenu("Layout"))
        {
            // NOT ImGui.GetID("dockspace") here - that hashes against whatever window's ID stack is
            // currently active (this menu bar's, not the dockspace's), producing a number that
            // doesn't identify the real dockspace node at all. See LunaWindow.DockspaceId.
            uint dockspaceId = LunaWindow.Instance.DockspaceId;
            var frameNames = LunaWindow.Instance.openFrames.Select(f => f.WindowId).ToList();

            if (ImGui.MenuItem("Default")) DockspaceLayoutManager.ForceApplyLayout(dockspaceId, DockspacePreset.Default, frameNames);
            if (ImGui.MenuItem("Compact")) DockspaceLayoutManager.ForceApplyLayout(dockspaceId, DockspacePreset.Compact, frameNames);
            if (ImGui.MenuItem("Wide")) DockspaceLayoutManager.ForceApplyLayout(dockspaceId, DockspacePreset.Wide, frameNames);

            // LatestLayoutName is an internal auto-save slot (see its doc comment), not something a
            // user names/loads/deletes by hand - excluded here, including from the Count check
            // (otherwise a session that never saved a named layout would show an empty separator).
            var namedLayouts = settings.SavedLayouts.Keys
                .Where(n => n != DockspaceLayoutManager.LatestLayoutName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (namedLayouts.Count > 0)
            {
                ImGui.Separator();
                foreach (var name in namedLayouts)
                {
                    // ###-scoped ID, distinct from the hardcoded preset MenuItems above (and from
                    // each other) regardless of what the saved layout is named - SaveLayoutAs already
                    // blocks the three reserved preset names, but this keeps a name collision from
                    // ever being able to produce a duplicate-ID assertion here again.
                    if (ImGui.BeginMenu($"{name}###savedlayout_{name}"))
                    {
                        if (ImGui.MenuItem("Load")) DockspaceLayoutManager.TryLoadLayout(settings, name, dockspaceId, LunaWindow.Instance.openFrames, EnsureFrameTypeOpen);
                        if (ImGui.MenuItem("Overwrite with current layout")) DockspaceLayoutManager.SaveLayoutAs(settings, name, LunaWindow.Instance.openFrames);
                        if (ImGui.MenuItem("Delete")) DockspaceLayoutManager.DeleteLayout(settings, name);
                        ImGui.EndMenu();
                    }
                }
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Save Current Layout As..."))
            {
                _newLayoutName = string.Empty;
                // Just a flag - see RenderSaveLayoutPopup for why the actual OpenPopup/BeginPopupModal
                // pair can't live here.
                _wantOpenSaveLayoutPopup = true;
            }

            ImGui.EndMenu();
        }
    }

    /// <summary>Must be called unconditionally, every frame, from OUTSIDE any BeginMenu block -
    /// unlike LayoutPresets, which only runs while the View menu happens to be open.
    ///
    /// Two bugs, now both fixed by moving this here: (1) ID-stack scoping - ImGui.OpenPopup/
    /// BeginPopupModal hash their ID against whatever window is current when called, so calling
    /// OpenPopup from inside the nested "Layout" submenu (one level deeper than where
    /// BeginPopupModal used to sit, back in the "View" menu's own scope) gave the two calls
    /// different IDs for the same string - the modal never found the popup that was "opened".
    /// (2) Gating - clicking a MenuItem closes the menu bar on the very next frame, so BeginMenu
    /// ("View") starts returning false and LayoutPresets's body (including BeginPopupModal) stops
    /// running entirely - a modal popup has to be drawn every frame regardless of whatever UI
    /// state triggered it, or it can never actually appear once the triggering menu closes.
    /// Both bugs independently meant this popup could never show; fixing only one wasn't enough.</summary>
    internal static void RenderSaveLayoutPopup()
    {
        var settings = LunaWindow.Instance.EditorSettings;

        if (_wantOpenSaveLayoutPopup)
        {
            _wantOpenSaveLayoutPopup = false;
            ImGui.OpenPopup("SaveLayoutPopup");
        }

        if (ImGui.BeginPopupModal("SaveLayoutPopup", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Layout name:");
            bool confirmed = ImGui.InputText("##LayoutName", ref _newLayoutName, 64, ImGuiInputTextFlags.EnterReturnsTrue);

            bool canSave = !string.IsNullOrWhiteSpace(_newLayoutName);
            ImGui.BeginDisabled(!canSave);
            if ((ImGui.Button("Save") || confirmed) && canSave)
            {
                DockspaceLayoutManager.SaveLayoutAs(settings, _newLayoutName.Trim(), LunaWindow.Instance.openFrames);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }
}
