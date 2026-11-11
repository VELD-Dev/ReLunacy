using System.Numerics;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// Editing a path in the 3D view, beside the gizmo: dragging its keys, handles or points straight on the
/// screen (in the horizontal plane through them, or up and down with Shift held), and the viewport panel a
/// selected path opens - stepping from key to key, a key's curvature (Bezier or none) and handle mode,
/// adding a key before or after the selected one, removing it (also with Delete), and turning a polyline
/// into a spline.
/// </summary>
internal sealed class PathEditor
{
    private EntityPathPoint? _dragging;
    private bool _vertical;
    private Vector3 _grabOffset;

    public bool IsDragging => _dragging != null;

    /// <summary>Starts dragging the part a click picked; the drag lasts while the left button is held.</summary>
    public void BeginDrag(EntityPathPoint part, Vector2 mouse, Vector2 viewport, EditorCamera camera)
    {
        _dragging = part;
        _vertical = Input.IsKeyDown(KeyboardKey.ShiftLeft) || Input.IsKeyDown(KeyboardKey.ShiftRight);
        Vector3 position = part.Transform.Translation;
        _grabOffset = PlaneHit(mouse, viewport, camera, position) is { } hit ? position - hit : Vector3.Zero;
    }

    /// <summary>Follows the mouse while the drag lasts.</summary>
    public void UpdateDrag(Vector2 mouse, Vector2 viewport, EditorCamera camera)
    {
        if (_dragging is not { } part) return;
        if (!Input.IsMouseButtonDown(MouseButton.Left) || !ReferenceEquals(SelectionManager.Singleton.SelectedEntity, part))
        {
            _dragging = null;
            return;
        }
        Vector3 position = part.Transform.Translation;
        if (PlaneHit(mouse, viewport, camera, position) is not { } hit) return;
        Vector3 target = hit + _grabOffset;
        var transform = part.Transform;
        transform.Translation = _vertical ? new Vector3(position.X, target.Y, position.Z) : new Vector3(target.X, position.Y, target.Z);
        part.Transform = transform;
    }

    /// <summary>Where the ray under the mouse meets the drag's plane through <paramref name="through"/>: the
    /// horizontal plane, or with Shift the vertical one facing the camera.</summary>
    private Vector3? PlaneHit(Vector2 mouse, Vector2 viewport, EditorCamera camera, Vector3 through)
    {
        if (!Matrix4x4.Invert(camera.GetView() * camera.GetProjection(), out var inverse)) return null;
        var ndc = new Vector2(mouse.X / viewport.X * 2f - 1f, 1f - mouse.Y / viewport.Y * 2f);
        var far = Vector4.Transform(new Vector4(ndc, 0.5f, 1f), inverse);
        Vector3 origin = camera.Position;
        Vector3 direction = Vector3.Normalize(new Vector3(far.X, far.Y, far.Z) / far.W - origin);
        Vector3 normal = _vertical ? camera.GetForward() with { Y = 0f } : Vector3.UnitY;
        if (normal.LengthSquared() < 1e-6f) normal = Vector3.UnitZ;
        normal = Vector3.Normalize(normal);
        float facing = Vector3.Dot(direction, normal);
        if (MathF.Abs(facing) < 1e-4f) return null;
        float t = Vector3.Dot(through - origin, normal) / facing;
        return t > 0f ? origin + direction * t : null;
    }

    /// <summary>The Delete key on a selected key or point removes it.</summary>
    public void HandleShortcuts()
    {
        if (Input.IsKeyPressed(KeyboardKey.Delete) && SelectionManager.Singleton.SelectedEntity is EntityPathPoint { Part: PathPart.Key or PathPart.Point } part)
            Remove(part.Owner, part.Index);
    }

    /// <summary>The path panel, open while a path or a part of one is selected.</summary>
    public void DrawPanel(ViewportOverlay overlay, float anchorLeft)
    {
        var selected = SelectionManager.Singleton.SelectedEntity;
        var part = selected as EntityPathPoint;
        if ((selected as EntityPath ?? part?.Owner) is not { } path) return;
        if (!overlay.BeginPanel("path", new Vector2(330f, 0f), anchorLeft)) return;

        ImGui.TextUnformatted(path.Name);
        ImGui.TextDisabled(path.IsSpline
            ? LM.Get("GUI_PathPanel_SplineSummary", path.KeyCount, path.Length.ToString("0.#"))
            : LM.Get("GUI_PathPanel_PolylineSummary", path.KeyCount, path.Length.ToString("0.#")));

        // The selected key (a handle's key counts), stepped through with the arrows.
        int key = part?.Index ?? -1;
        if (ImGui.ArrowButton("##prevkey", ImGuiDir.Left)) Select(path, key <= 0 ? path.KeyCount - 1 : key - 1);
        ImGui.SameLine();
        ImGui.TextUnformatted(key >= 0 ? LM.Get("GUI_PathPanel_Key", key, path.KeyCount - 1) : LM.Get("GUI_PathPanel_NoKey"));
        ImGui.SameLine();
        if (ImGui.ArrowButton("##nextkey", ImGuiDir.Right)) Select(path, key < 0 || key >= path.KeyCount - 1 ? 0 : key + 1);

        if (key >= 0)
        {
            if (path.IsSpline)
            {
                // Curvature: the quick switch between a smooth key and a corner.
                bool corner = path.IsCorner(key);
                ImGui.TextUnformatted(LM.Get("GUI_PathPanel_Curvature"));
                ImGui.SameLine();
                if (Choice(LM.Get("GUI_PathPanel_Bezier"), !corner)) { path.SetCurvature(key, bezier: true); part!.Refresh(); }
                ImGui.SameLine();
                if (Choice(LM.Get("GUI_PathPanel_None"), corner)) { path.SetCurvature(key, bezier: false); part!.Refresh(); }

                ImGui.TextUnformatted(LM.Get("GUI_Frame_InstanceInspector_PathHandles"));
                var current = path.ModeAt(key);
                foreach (var mode in Enum.GetValues<PathHandleMode>())
                {
                    ImGui.SameLine();
                    if (Choice(LM.Get("GUI_PathHandle_" + mode), current == mode) && mode != current)
                    {
                        path.SetMode(key, mode);
                        if (path.OutHandleAt(key) is { } outHandle) path.SetHandle(key, true, outHandle);
                        else if (path.InHandleAt(key) is { } inHandle) path.SetHandle(key, false, inHandle);
                        part!.Refresh();
                    }
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_PathHandlesHint"));
            }

            if (ImGui.Button(LM.Get("GUI_PathPanel_AddBefore"))) Select(path, path.InsertBefore(key));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_PathPanel_AddHint"));
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_PathPanel_AddAfter"))) Select(path, path.InsertAfter(key));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_PathPanel_AddHint"));
            ImGui.SameLine();
            ImGui.BeginDisabled(path.KeyCount <= 2);
            if (ImGui.Button(LM.Get("GUI_PathPanel_Remove"))) Remove(path, key);
            ImGui.EndDisabled();
        }

        if (!path.IsSpline && path.KeyCount >= 2)
        {
            if (ImGui.Button(LM.Get("GUI_PathPanel_ConvertToSpline")))
            {
                path.ConvertToSpline();
                if (key >= 0) Select(path, key);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_PathPanel_ConvertToSplineHint"));
        }

        ImGui.Separator();
        ImGui.TextWrapped(LM.Get("GUI_PathPanel_Hint"));
        overlay.EndPanel();
    }

    /// <summary>A button lit while <paramref name="active"/>.</summary>
    private static bool Choice(string label, bool active)
    {
        if (active) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
        bool clicked = ImGui.SmallButton(label);
        if (active) ImGui.PopStyleColor();
        return clicked;
    }

    private static void Select(EntityPath path, int key)
    {
        if (key < 0 || key >= path.KeyCount) return;
        SelectionManager.Singleton.Select(new EntityPathPoint(path, key, path.IsSpline ? PathPart.Key : PathPart.Point));
    }

    private static void Remove(EntityPath path, int key)
    {
        if (!path.Remove(key)) return;
        if (path.KeyCount > 0) Select(path, Math.Min(key, path.KeyCount - 1));
        else SelectionManager.Singleton.Select(path);
    }
}
