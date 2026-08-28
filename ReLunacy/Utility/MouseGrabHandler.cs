using System.Numerics;

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
            if (!allowNewGrab) return false;

            isGrabbed = true;
            GrabPosition = Input.GetMousePosition();
            Input.EnableRelativeMouseMode();
        }

        return isGrabbed;
    }
}
