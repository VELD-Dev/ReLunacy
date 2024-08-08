using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.MenuBar;

internal static class ViewMenuDraw
{
    internal static void ShowOverlay()
    {
        if(ImGui.Button("Show Overlay", "", Overlay.showOverlay, true)) return;
        
        Overlay.showOverlay = !Overlay.showOverlay;
    }
}
