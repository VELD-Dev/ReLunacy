using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using Hexa.NET.ImGuizmo;
using ReLunacy.Core;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Utility;

public class GizmoController
{
    public ImGuizmoOperation CurrentOperation { get; set; } = ImGuizmoOperation.Translate;
    public ImGuizmoMode CurrentMode { get; set; } = ImGuizmoMode.World;

    public bool IsUsing => ImGuizmo.IsUsingAny();

    /// <summary>
    /// True while the cursor is over a gizmo handle. Gated on _manipulatedThisFrame since
    /// ImGuizmo.IsOver() reflects stale state from the last Manipulate() call otherwise.
    /// </summary>
    public bool IsOver => _manipulatedThisFrame && ImGuizmo.IsOver();

    private bool _initialized;
    private bool _manipulatedThisFrame;

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
        ImGuizmo.Enable(true);
        ImGuizmo.SetOrthographic(false);
    }

    /// <summary>Draws the gizmo on <paramref name="entity"/> (the active one) and applies a drag to it and,
    /// relative to it, to <paramref name="others"/>, as one rigid group about the active entity's pivot.
    /// Returns whether anything moved.</summary>
    public bool Render(Engine.Rendering.EditorCamera camera, Entity? entity, IReadOnlyList<Entity> others, Vector2 viewportPos, Vector2 viewportSize)
    {
        EnsureInitialized();
        ImGuizmo.BeginFrame();

        _manipulatedThisFrame = entity != null;
        if (entity == null) return false;

        ImGuizmo.SetDrawlist();
        ImGuizmo.SetRect(viewportPos.X, viewportPos.Y, viewportSize.X, viewportSize.Y);
        // Clip-space size, so the gizmo keeps its on-screen scale at any distance. Pushed per frame
        // rather than in EnsureInitialized so the Editor Settings slider takes effect live.
        ImGuizmo.SetGizmoSizeClipSpace(Program.Settings.ToolsGizmoSize);

        var view = camera.GetView();
        var projection = camera.GetProjection();

        var transform = entity.Transform;
        var matrix = Matrix4x4.CreateScale(transform.Scale)
                   * Matrix4x4.CreateFromQuaternion(transform.Rotation)
                   * Matrix4x4.CreateTranslation(transform.Translation);

        // The same composition the drag ends in, so the delta below holds only the drag.
        var before = matrix;
        var settings = Program.Settings;
        if (settings.GizmoSnapEnabled)
        {
            float snap = CurrentOperation switch
            {
                ImGuizmoOperation.Translate => settings.GizmoSnapTranslation,
                ImGuizmoOperation.Rotate => settings.GizmoSnapRotation,
                ImGuizmoOperation.Scale => settings.GizmoSnapScale,
                _ => 1.0f
            };
            ImGuizmo.Manipulate(ref view, ref projection, CurrentOperation, CurrentMode, ref matrix, ref snap);
        }
        else
        {
            ImGuizmo.Manipulate(ref view, ref projection, CurrentOperation, CurrentMode, ref matrix);
        }

        if (!ImGuizmo.IsUsingAny() || !Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
            return false;
        entity.Transform = new Transform { Translation = translation, Rotation = rotation, Scale = scale };
        if (others.Count == 0 || !Matrix4x4.Invert(before, out var inverse)) return true;

        // What the drag did to the active entity, applied to the rest: world' = world * before^-1 * after.
        var delta = inverse * matrix;
        if (delta.IsIdentity) return true;
        foreach (var other in others)
        {
            if (ReferenceEquals(other, entity)) continue;
            if (Matrix4x4.Decompose(other.Transform.GetMatrix() * delta, out var s, out var r, out var t))
                other.Transform = new Transform { Translation = t, Rotation = r, Scale = s };
        }
        return true;
    }
}
