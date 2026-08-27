using System.Numerics;

namespace ReLunacy.Utility;

/// <summary>
/// Small always-visible orientation indicator drawn in a corner of a 3D viewport: three colored
/// axes (X=red, Y=green, Z=blue - the common Unity/Blender/Godot convention) projected using the
/// camera's own right/up basis. Purely informational, unlike a full interactive view-cube.
/// </summary>
public static class AxisGizmoRenderer
{
    private static readonly (Vector3 Axis, string Label, Vector4 Color)[] Axes =
    [
        (Vector3.UnitX, "X", new Vector4(0.85f, 0.25f, 0.25f, 1f)),
        (Vector3.UnitY, "Y", new Vector4(0.35f, 0.75f, 0.25f, 1f)),
        (Vector3.UnitZ, "Z", new Vector4(0.30f, 0.50f, 0.95f, 1f)),
    ];

    public static void Draw(Engine.Rendering.EditorCamera camera, Vector2 center, float radius)
    {
        var drawList = ImGui.GetWindowDrawList();

        Vector3 forward = camera.GetForward();
        Vector3 right = Vector3.Normalize(camera.GetRight());
        // GetRight() = Cross(forward, world Up); re-orthogonalize against forward to get the
        // camera's true screen-space up: the camera's Up itself stays world-Y and doesn't tilt with
        // pitch, so using it directly here would make the gizmo drift out of sync while looking
        // up/down.
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));

        drawList.AddCircleFilled(center, radius + 10f, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.25f)));

        // Draw the axis pointing most toward the camera last, so at the shared origin point it
        // renders on top of the ones receding into the screen.
        foreach (var (axis, label, color) in Axes.OrderByDescending(a => Vector3.Dot(a.Axis, forward)))
        {
            float sx = Vector3.Dot(axis, right);
            float sy = -Vector3.Dot(axis, up); // screen Y grows downward, view-space up doesn't
            float depth = Vector3.Dot(axis, forward);

            var tip = center + new Vector2(sx, sy) * radius;
            // depth < 0 means this axis points toward the camera ("out of the screen") - drawn
            // brighter than one receding into it, for a cheap sense of depth without real 3D.
            float shade = depth < 0f ? 1f : 0.55f;
            uint col = ImGui.GetColorU32(color * new Vector4(shade, shade, shade, 1f));

            drawList.AddLine(center, tip, col, 2f);
            drawList.AddCircleFilled(tip, 6f, col);

            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(tip - textSize / 2f, ImGui.GetColorU32(Vector4.One), label);
        }
    }
}
