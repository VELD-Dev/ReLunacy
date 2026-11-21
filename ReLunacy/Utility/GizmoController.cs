using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using Hexa.NET.ImGuizmo;
using ReLunacy.Core;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Utility;

public enum GizmoSpace { World, Local, Moon }

public class GizmoController
{
    public ImGuizmoOperation CurrentOperation { get; set; } = ImGuizmoOperation.Translate;
    // Moon space is not saved: it only exists while moon mode does.
    private bool _moonSpace;

    /// <summary>The gizmo works in the tangent frame of the moon being edited (see <see cref="Core.Moon.MoonMode"/>).</summary>
    public bool InMoonSpace => _moonSpace && Core.Moon.MoonMode.Active;

    // Moon space is drawn Local, in a frame that is the tangent frame instead of the object's own.
    public ImGuizmoMode CurrentMode => InMoonSpace || Program.Settings.GizmoLocal ? ImGuizmoMode.Local : ImGuizmoMode.World;

    /// <summary>The space the gizmo works in now.</summary>
    public GizmoSpace Space => InMoonSpace ? GizmoSpace.Moon : Program.Settings.GizmoLocal ? GizmoSpace.Local : GizmoSpace.World;

    public void SetSpace(GizmoSpace space)
    {
        if (space == GizmoSpace.Moon) { if (Core.Moon.MoonMode.Active) _moonSpace = true; return; }
        _moonSpace = false;
        Program.Settings.GizmoLocal = space == GizmoSpace.Local;
        Program.Settings.SaveSettingsToFile();
    }

    public void ToggleLocal()
    {
        _moonSpace = false;
        Program.Settings.GizmoLocal = !Program.Settings.GizmoLocal;
        Program.Settings.SaveSettingsToFile();
    }

    public void SetMoonSpace(bool on)
    {
        _moonSpace = on && Core.Moon.MoonMode.Active;
        if (!_moonSpace) { Program.Settings.GizmoLocal = false; Program.Settings.SaveSettingsToFile(); }
    }

    /// <summary>World, Local and, in moon mode, Moon, in turn.</summary>
    public void CycleSpace()
    {
        if (!Core.Moon.MoonMode.Active) { ToggleLocal(); return; }
        if (InMoonSpace) { _moonSpace = false; Program.Settings.GizmoLocal = false; }
        else if (Program.Settings.GizmoLocal) _moonSpace = true;
        else Program.Settings.GizmoLocal = true;
        Program.Settings.SaveSettingsToFile();
    }

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

    // Moon space: the handle under the cursor when the drag began (which of east / up / north it may move along), and
    // the frame carried along the drag by the smallest turn between radials, so it never flips near a pole.
    private Vector3 _moonAxes = Vector3.One;
    private Quaternion? _moonDragFrame;
    private Vector3 _moonDragUp;

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
        if (!_wasUsing) _moonAxes = HoveredAxes();
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

        // A path part's gizmo is oriented along the path in Local mode; the edit stays a pure translation.
        var pathPart = entity as EntityPathPoint;
        if (pathPart != null && CurrentMode == ImGuizmoMode.Local)
            matrix = Matrix4x4.CreateFromQuaternion(pathPart.Frame()) * Matrix4x4.CreateTranslation(transform.Translation);

        // In Moon space the frame is the tangent frame at the object, with no scale.
        bool moon = InMoonSpace && (CurrentOperation == ImGuizmoOperation.Translate || (pathPart == null && CurrentOperation == ImGuizmoOperation.Rotate));
        var operation = moon && CurrentOperation == ImGuizmoOperation.Rotate ? ImGuizmoOperation.RotateY : CurrentOperation;
        Quaternion moonFrame = default;
        if (moon)
        {
            moonFrame = _wasUsing && _moonDragFrame is { } carried ? Carry(carried, _moonDragUp, transform.Translation) : Core.Moon.MoonMode.TangentFrame(transform.Translation);
            _moonDragFrame = moonFrame;
            _moonDragUp = Core.Moon.MoonMode.Radial(transform.Translation);
            matrix = Matrix4x4.CreateFromQuaternion(moonFrame) * Matrix4x4.CreateTranslation(transform.Translation);
        }

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
            ImGuizmo.Manipulate(ref view, ref projection, operation, CurrentMode, ref matrix, ref snap);
        }
        else
        {
            ImGuizmo.Manipulate(ref view, ref projection, operation, CurrentMode, ref matrix);
        }

        bool using_ = ImGuizmo.IsUsingAny();
        if (using_ && !_wasUsing) _drag = _hovered ?? Core.Undo.UndoManager.Singleton.Begin([entity, .. others]);
        _wasUsing = using_;
        if (!using_) { EndDrag(); _moonDragFrame = null; }
        if (!using_ || !Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
            return false;
        if (moon) return ApplyMoon(entity, others, transform.Translation, moonFrame, rotation, translation, CurrentOperation == ImGuizmoOperation.Rotate, _moonAxes);
        if (pathPart != null) translation = matrix.Translation;
        if (pathPart != null) { rotation = Quaternion.Identity; scale = Vector3.One; matrix = Matrix4x4.CreateTranslation(translation); before = Matrix4x4.CreateTranslation(transform.Translation); }
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

    /// <summary>A drag in Moon space. The tangent axes carry the object over the sphere and the radial one changes its
    /// altitude; a turn is about the radial axis. Either is one rotation about the moon's centre (which keeps every
    /// object's up along its radius) and a radial shift, applied to the rest of the selection as well.</summary>
    private static bool ApplyMoon(Entity entity, IReadOnlyList<Entity> others, Vector3 position, Quaternion frame, Quaternion draggedFrame, Vector3 draggedPosition, bool rotating, Vector3 axes)
    {
        var moonRotation = Quaternion.Identity;
        float altitude = 0f;
        if (rotating && Turned(draggedFrame, frame, out float turn))
            moonRotation = Quaternion.CreateFromAxisAngle(Core.Moon.MoonMode.Radial(position), turn);
        else
        {
            var up = Core.Moon.MoonMode.Radial(position);
            var delta = draggedPosition - position;
            var east = Vector3.Transform(Vector3.UnitX, frame);
            var north = Vector3.Transform(Vector3.UnitZ, frame);
            // ImGuizmo keeps a plane drag on the plane it started on, which leaves the sphere: only the handle's axes count.
            float de = Vector3.Dot(delta, east) * axes.X, dn = Vector3.Dot(delta, north) * axes.Z;
            altitude = Vector3.Dot(delta, up) * axes.Y;
            float along = MathF.Sqrt(de * de + dn * dn);
            float radius = (position - Core.Moon.MoonMode.Centre).Length();
            if (along > 1e-6f && radius > 1e-3f)
            {
                var direction = (east * de + north * dn) / along;
                moonRotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(up, direction)), along / radius);
            }
        }
        if (moonRotation.IsIdentity && altitude == 0f) return false;

        var centre = Core.Moon.MoonMode.Centre;
        var turnAbout = Matrix4x4.CreateTranslation(-centre) * Matrix4x4.CreateFromQuaternion(moonRotation) * Matrix4x4.CreateTranslation(centre);
        foreach (var target in new[] { entity }.Concat(others).Distinct())
        {
            var matrix = target is EntityVolume volume ? volume.FillMatrix : target.Transform.GetMatrix();
            var moved = matrix * turnAbout;
            if (altitude != 0f)
                moved.Translation += Core.Moon.MoonMode.Radial(moved.Translation) * altitude;
            if (target is EntityVolume v) { v.SetMatrix(moved); continue; }
            // A path key or handle is only a place.
            if (target is EntityPathPoint) { target.Transform = new Transform { Translation = moved.Translation, Rotation = Quaternion.Identity, Scale = Vector3.One }; continue; }
            if (Matrix4x4.Decompose(moved, out var s, out var r, out var t))
                target.Transform = new Transform { Translation = t, Rotation = r, Scale = Uniform(target, s, target.Transform.Scale) };
        }
        return true;
    }

    /// <summary>The frame axes (east, up, north) the translate handle under the cursor moves along. The centre square moves
    /// over the surface.</summary>
    private static Vector3 HoveredAxes()
    {
        if (ImGuizmo.IsOver(ImGuizmoOperation.TranslateX)) return Vector3.UnitX;
        if (ImGuizmo.IsOver(ImGuizmoOperation.TranslateY)) return Vector3.UnitY;
        if (ImGuizmo.IsOver(ImGuizmoOperation.TranslateZ)) return Vector3.UnitZ;
        // No single axis is hovered, so a two-axis test can only be its plane.
        if (ImGuizmo.IsOver(ImGuizmoOperation.TranslateX | ImGuizmoOperation.TranslateY)) return new Vector3(1f, 1f, 0f);
        if (ImGuizmo.IsOver(ImGuizmoOperation.TranslateY | ImGuizmoOperation.TranslateZ)) return new Vector3(0f, 1f, 1f);
        return new Vector3(1f, 0f, 1f);
    }

    /// <summary><paramref name="frame"/> turned by the smallest rotation from <paramref name="up"/> to the radial at <paramref name="point"/>.</summary>
    private static Quaternion Carry(Quaternion frame, Vector3 up, Vector3 point)
    {
        var to = Core.Moon.MoonMode.Radial(point);
        var axis = Vector3.Cross(up, to);
        float sin = axis.Length(), cos = Vector3.Dot(up, to);
        if (sin < 1e-7f) return frame;
        return Quaternion.Normalize(Quaternion.Concatenate(frame, Quaternion.CreateFromAxisAngle(axis / sin, MathF.Atan2(sin, cos))));
    }

    /// <summary>The turn about the frame's own up that a rotate drag made: <paramref name="dragged"/> is the frame after
    /// the gizmo's turn, <paramref name="frame"/> before it.</summary>
    private static bool Turned(Quaternion dragged, Quaternion frame, out float angle)
    {
        // The gizmo's rotation applies in the frame, so its Y component is the turn about the radial axis.
        var change = Quaternion.Concatenate(dragged, Quaternion.Inverse(frame));
        if (change.W < 0f) change = Quaternion.Negate(change);
        angle = 2f * MathF.Atan2(change.Y, change.W);
        return MathF.Abs(angle) > 1e-6f;
    }
}
