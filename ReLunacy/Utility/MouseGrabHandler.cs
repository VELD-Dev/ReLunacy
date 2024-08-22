using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Utility;

public class MouseGrabHandler
{
    private bool isGrabbed;

    public MouseButton mouseButton { get; set; }

    public bool TryGrabMouse(bool allowNewGrab)
    {
        bool isDown = Window.Singleton.MouseState.IsButtonDown(mouseButton);
        bool wasDown = Window.Singleton.MouseState.WasButtonDown(mouseButton);

        if(!isDown)
        {
            if(wasDown && isGrabbed)
            {
                isGrabbed = false;
                Window.Singleton.CursorState = CursorState.Normal;
            }
            return false;
        }

        if(!wasDown)
        {
            if (!allowNewGrab)
                return false;

            isGrabbed = true;
            Window.Singleton.CursorState = CursorState.Grabbed;
        }

        return isGrabbed;
    }
}
