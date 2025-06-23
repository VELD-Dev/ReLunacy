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
}
