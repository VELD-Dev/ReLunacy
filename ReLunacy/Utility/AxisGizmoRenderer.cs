using System.Numerics;

namespace ReLunacy.Utility;

/// <summary>
/// Small orientation indicator drawn in a corner of a 3D viewport: colored X/Y/Z axes projected
/// using the camera's right/up basis. Informational only, not interactive.
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
        // Re-orthogonalized against forward to get true screen-space up (camera.Up stays world-Y).
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));

        drawList.AddCircleFilled(center, radius + 10f, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.25f)));

        // Draw the axis closest to the camera last so it renders on top at the shared origin.
        foreach (var (axis, label, color) in Axes.OrderByDescending(a => Vector3.Dot(a.Axis, forward)))
        {
            float sx = Vector3.Dot(axis, right);
            float sy = -Vector3.Dot(axis, up); // screen Y grows downward, view-space up doesn't
            float depth = Vector3.Dot(axis, forward);

            var tip = center + new Vector2(sx, sy) * radius;
            // Axes pointing toward the camera are drawn brighter for a cheap depth cue.
            float shade = depth < 0f ? 1f : 0.55f;
            uint col = ImGui.GetColorU32(color * new Vector4(shade, shade, shade, 1f));

            drawList.AddLine(center, tip, col, 2f);
            drawList.AddCircleFilled(tip, 6f, col);

            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(tip - textSize / 2f, ImGui.GetColorU32(Vector4.One), label);
        }
    }
}
