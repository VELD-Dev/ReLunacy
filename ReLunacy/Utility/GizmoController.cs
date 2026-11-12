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

    // Undo: the selection's state taken while the cursor is over a handle (so it is the state before the drag's
    // first frame moves it), opened as a step when a drag starts and committed when it ends.
    private bool _wasUsing;
    private Core.Undo.UndoManager.Session? _hovered;
    private Core.Undo.UndoManager.Session? _drag;

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
    /// <summary>A Moby's scale is one number in its file (MobyInstanceNew +0x2C), so a scale drag on one axis scales
    /// all three: the axis that moved furthest from <paramref name="before"/> gives the value.</summary>
    private static Vector3 Uniform(Entity entity, Vector3 scale, Vector3 before)
    {
        if (entity is not EntityMoby) return scale;
        var change = Vector3.Abs(scale - before);
        float value = change.X >= change.Y && change.X >= change.Z ? scale.X : change.Y >= change.Z ? scale.Y : scale.Z;
        return new Vector3(value);
    }

    private void EndDrag()
    {
        _wasUsing = false;
        if (_drag == null) return;
        _hovered = null;
        _drag.Commit(Utility.Localization.LM.Get(CurrentOperation switch
        {
            ImGuizmoOperation.Rotate => "GUI_Undo_Rotate",
            ImGuizmoOperation.Scale => "GUI_Undo_Scale",
            _ => "GUI_Undo_Move",
        }));
        _drag = null;
    }

    public bool Render(Engine.Rendering.EditorCamera camera, Entity? entity, IReadOnlyList<Entity> others, Vector2 viewportPos, Vector2 viewportSize)
    {
        EnsureInitialized();
        ImGuizmo.BeginFrame();

        _manipulatedThisFrame = entity != null;
        if (entity == null)
        {
            EndDrag();
            _hovered = null;
            return false;
        }
        if (!_wasUsing && ImGuizmo.IsOver())
            _hovered ??= Core.Undo.UndoManager.Singleton.Begin([entity, .. others]);
        else if (!_wasUsing)
            _hovered = null;

        ImGuizmo.SetDrawlist();
        ImGuizmo.SetRect(viewportPos.X, viewportPos.Y, viewportSize.X, viewportSize.Y);
        // Clip-space size, so the gizmo keeps its on-screen scale at any distance. Pushed per frame
        // rather than in EnsureInitialized so the Editor Settings slider takes effect live.
        ImGuizmo.SetGizmoSizeClipSpace(Program.Settings.ToolsGizmoSize);

        var view = camera.GetView();
        var projection = camera.GetProjection();

        var transform = entity.Transform;
        // A volume is handled through its whole matrix, size included (EntityVolume.SetMatrix), so a scale drag
        // changes its size and never its Transform's scale, which would thicken its wireframe.
        var matrix = entity is EntityVolume volume
            ? volume.FillMatrix
            : Matrix4x4.CreateScale(transform.Scale)
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

        bool using_ = ImGuizmo.IsUsingAny();
        if (using_ && !_wasUsing) _drag = _hovered ?? Core.Undo.UndoManager.Singleton.Begin([entity, .. others]);
        _wasUsing = using_;
        if (!using_) EndDrag();
        if (!using_ || !Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
            return false;
        if (entity is EntityVolume active) active.SetMatrix(matrix);
        else entity.Transform = new Transform { Translation = translation, Rotation = rotation, Scale = Uniform(entity, scale, transform.Scale) };
        if (others.Count == 0 || !Matrix4x4.Invert(before, out var inverse)) return true;

        // What the drag did to the active entity, applied to the rest: world' = world * before^-1 * after.
        var delta = inverse * matrix;
        if (delta.IsIdentity) return true;
        foreach (var other in others)
        {
            if (ReferenceEquals(other, entity)) continue;
            if (other is EntityVolume otherVolume) { otherVolume.SetMatrix(otherVolume.FillMatrix * delta); continue; }
            if (Matrix4x4.Decompose(other.Transform.GetMatrix() * delta, out var s, out var r, out var t))
                other.Transform = new Transform { Translation = t, Rotation = r, Scale = Uniform(other, s, other.Transform.Scale) };
        }
        return true;
    }
}
