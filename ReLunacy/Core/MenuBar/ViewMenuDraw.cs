using ImGuiNET;
using ReLunacy.Core;
using ReLunacy.Core.EntityManagement;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

internal static class ViewMenuDraw
{
    internal static void ShowOverlay()
    {
        if(!ImGui.MenuItem(LM.Get("GUI_MenuItem_ShowOverlay"), "", Overlay.showOverlay , true)) return;
        
        Overlay.showOverlay = !Overlay.showOverlay;
    }

    internal static void ShowView3D()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<View3D>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_View3D"), "", frameAlreadyOpen, true))
            return;

        if(frameAlreadyOpen)
        {
            LunaWindow.Instance.TryCloseFirstFrame<View3D>();
        }
        else
        {
            LunaWindow.Instance.AddFrame(new View3D(LunaWindow.Instance.GraphicsDevice));
        }
    }

    internal static void ShowEntityExplorer()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<BasicEntityExplorer>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer"), "", frameAlreadyOpen, true))
            return;

        if(frameAlreadyOpen)
        {
            LunaWindow.Instance.TryCloseFirstFrame<BasicEntityExplorer>();
        }
        else
        {
            if(Program.ProvidedPath != "")
            {
                //LunaWindow.Instance.AddFrame(new BasicEntityExplorer(EntityManager.Singleton.GetAllEntities()));
            }
            else
            {
                LunaWindow.Instance.AddFrame(new BasicEntityExplorer());
            }
        }
    }

    internal static void ShowInstanceInspector()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<PropertyInspectorFrame>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_InstanceInspector"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
        {
            LunaWindow.Instance.TryCloseFirstFrame<PropertyInspectorFrame>();
        }
        else
        {
            LunaWindow.Instance.AddFrame(new PropertyInspectorFrame());
        }
    }

    internal static void ShowTextureExplorer()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<TexturesExplorer>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_TextureExplorer"), "", frameAlreadyOpen, true))
            return;

        if(frameAlreadyOpen)
        {
            LunaWindow.Instance.TryCloseFirstFrame<TexturesExplorer>();
        }
        else
        {
            var frame = new TexturesExplorer();
            if (LunaWindow.Instance.AssetManager is not null && LunaWindow.Instance.Loader is not null && LunaWindow.Instance.AssetManager.Textures.Count > 0)
                Task.Run(() => frame.TransmitTextures(LunaWindow.Instance.AssetManager, LunaWindow.Instance.Loader));
            LunaWindow.Instance.AddFrame(frame);
        }
    }

    internal static void ShowAssetViewer()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<AssetViewer>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_AssetViewer"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
        {
            LunaWindow.Instance.TryCloseFirstFrame<AssetViewer>();
        }
        else
        {
            var frame = new AssetViewer(LunaWindow.Instance.GraphicsDevice);
            if (LunaWindow.Instance.AssetManager is not null & LunaWindow.Instance.Loader is not null && LunaWindow.Instance.Loader.Loaded)
                frame.TransmitAssets(LunaWindow.Instance.AssetManager, LunaWindow.Instance.Loader);
            LunaWindow.Instance.AddFrame(frame);
        }
    }

    internal static void ShowConsoleFrame()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<LogsFrame>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_Logs"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
        {
            LunaWindow.Instance.TryCloseFirstFrame<LogsFrame>();
        }
        else
        {
            LunaWindow.Instance.AddFrame(new LogsFrame());
        }
    }

    internal static void ShowPSArcExplorer()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<PSArcExplorer>();
        if (!ImGui.MenuItem("PSArc Explorer", "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
        {
            LunaWindow.Instance.TryCloseFirstFrame<PSArcExplorer>();
        }
        else
        {
            LunaWindow.Instance.AddFrame(new PSArcExplorer());
        }
    }
}
