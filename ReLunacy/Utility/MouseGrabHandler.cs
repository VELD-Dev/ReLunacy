using Bliss.CSharp.Interact;
using Bliss.CSharp.Interact.Mice;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Utility;

public class MouseGrabHandler
{
    private bool isGrabbed;

    public MouseButton mouseButton { get; set; }

    public Vector2 GrabPosition;

    public bool TryGrabMouse(bool allowNewGrab)
    {
        bool isDown = Input.IsMouseButtonDown(mouseButton);

        if (!isDown)
        {
            if (isGrabbed)
            {
                isGrabbed = false;
                Input.DisableRelativeMouseMode();
                Input.SetMousePosition(GrabPosition);
                GrabPosition = Vector2.Zero;
            }
            return false;
        }

        if (!isGrabbed)
        {
            if (!allowNewGrab)
                return false;

            isGrabbed = true;
            GrabPosition = Input.GetMousePosition();
            Input.EnableRelativeMouseMode();
        }

        return isGrabbed;
    }
}
