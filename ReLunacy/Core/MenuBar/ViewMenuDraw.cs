using ReLunacy.Core;
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

    internal static void LayoutPresets()
    {
        if (ImGui.BeginMenu("Layout"))
        {
            uint dockspaceId = ImGui.GetID("dockspace");
            var frameNames = LunaWindow.Instance.openFrames.Select(f => f.FrameName).ToList();

            if (ImGui.MenuItem("Default")) DockspaceLayoutManager.ForceApplyLayout(dockspaceId, DockspacePreset.Default, frameNames);
            if (ImGui.MenuItem("Compact")) DockspaceLayoutManager.ForceApplyLayout(dockspaceId, DockspacePreset.Compact, frameNames);
            if (ImGui.MenuItem("Wide")) DockspaceLayoutManager.ForceApplyLayout(dockspaceId, DockspacePreset.Wide, frameNames);

            ImGui.EndMenu();
        }
    }
}
