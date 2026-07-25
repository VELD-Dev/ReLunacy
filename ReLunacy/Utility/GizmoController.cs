using System.Numerics;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Transformations;
using Hexa.NET.ImGuizmo;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Utility;

public class GizmoController
{
    public ImGuizmoOperation CurrentOperation { get; set; } = ImGuizmoOperation.Translate;
    public ImGuizmoMode CurrentMode { get; set; } = ImGuizmoMode.World;

    public bool IsUsing => ImGuizmo.IsUsingAny();

    /// <summary>
    /// True while the cursor is over a gizmo handle. IsUsingAny() lags a frame behind an initial
    /// click (it wants a drag delta first), so on the very first click-down on a handle it would
    /// still read false — checking IsOver too catches that frame so the click isn't mistaken for
    /// a pick request. Gated on _manipulatedThisFrame since IsOver() reflects stale state from
    /// whatever the last Manipulate() call drew when there's no selection to manipulate now.
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

    public void Render(Cam3D camera, Entity? entity, Vector2 viewportPos, Vector2 viewportSize)
    {
        EnsureInitialized();
        ImGuizmo.BeginFrame();

        _manipulatedThisFrame = entity != null;
        if (entity == null) return;

        ImGuizmo.SetDrawlist();
        ImGuizmo.SetRect(viewportPos.X, viewportPos.Y, viewportSize.X, viewportSize.Y);

        var view = camera.GetView();
        var projection = camera.GetProjection();

        var transform = entity.Transform;
        var matrix = Matrix4x4.CreateScale(transform.Scale)
                   * Matrix4x4.CreateFromQuaternion(transform.Rotation)
                   * Matrix4x4.CreateTranslation(transform.Translation);

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

        if (ImGuizmo.IsUsingAny() && Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
        {
            entity.Transform = new Transform { Translation = translation, Rotation = rotation, Scale = scale };
        }
    }
}
