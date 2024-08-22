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
}
