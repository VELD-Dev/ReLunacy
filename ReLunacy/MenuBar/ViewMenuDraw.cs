namespace ReLunacy.MenuBar;

internal static class ViewMenuDraw
{
    internal static void ShowOverlay()
    {
        if(!ImGui.MenuItem("Show Overlay", "", Overlay.showOverlay, true)) return;
        
        Overlay.showOverlay = !Overlay.showOverlay;
    }

    internal static void ShowView3D()
    {
        bool frameAlreadyOpen = Window.Singleton.IsAnyFrameOpened<View3DFrame>();
        if (!ImGui.MenuItem("View 3D", "", frameAlreadyOpen, true))
            return;

        if(frameAlreadyOpen)
        {
            Window.Singleton.TryCloseFirstFrame<View3DFrame>();
        }
        else
        {
            Window.Singleton.AddFrame(new View3DFrame());
        }
    }

    internal static void ShowEntityExplorer()
    {
        bool frameAlreadyOpen = Window.Singleton.IsAnyFrameOpened<BasicEntityExplorer>();
        if (!ImGui.MenuItem("Entity Instances Explorer", "", frameAlreadyOpen, true))
            return;

        if(frameAlreadyOpen)
        {
            Window.Singleton.TryCloseFirstFrame<BasicEntityExplorer>();
        }
        else
        {
            if(Program.ProvidedPath != "")
            {
                Window.Singleton.AddFrame(new BasicEntityExplorer(EntityManager.Singleton.GetAllEntities()));
            }
            else
            {
                Window.Singleton.AddFrame(new BasicEntityExplorer());
            }
        }
    }

    internal static void ShowInstanceInspector()
    {
        bool frameAlreadyOpen = Window.Singleton.IsAnyFrameOpened<PropertyInspectorFrame>();
        if (!ImGui.MenuItem("Instance Inspector", "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
        {
            Window.Singleton.TryCloseFirstFrame<PropertyInspectorFrame>();
        }
        else
        {
            Window.Singleton.AddFrame(new PropertyInspectorFrame());
        }
    }
}
