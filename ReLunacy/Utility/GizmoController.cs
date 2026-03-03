using System.Numerics;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Transformations;
using Hexa.NET.ImGuizmo;
using ReLunacy.Core.EntityManagement;

namespace ReLunacy.Utility;

public class GizmoController
{
    public ImGuizmoOperation CurrentOperation { get; set; } = ImGuizmoOperation.Translate;
    public ImGuizmoMode CurrentMode { get; set; } = ImGuizmoMode.World;

    public bool IsUsing => ImGuizmo.IsUsingAny();

    private bool _initialized;
    private bool _debugLogged = true;

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

        if (entity == null) return;

        ImGuizmo.SetDrawlist();
        ImGuizmo.SetRect(viewportPos.X, viewportPos.Y, viewportSize.X, viewportSize.Y);

        var view = camera.GetView();
        var projection = camera.GetProjection();

        // The viewport image is displayed with flipped UVs (uv0=1,0 uv1=0,1)
        // which mirrors it horizontally. Negate X scale in projection so
        // ImGuizmo's screen-space projection matches the flipped image.
        projection.M11 = -projection.M11;

        var transform = entity.Transform;
        var matrix = Matrix4x4.CreateScale(transform.Scale)
                   * Matrix4x4.CreateFromQuaternion(transform.Rotation)
                   * Matrix4x4.CreateTranslation(transform.Translation);

        if (!_debugLogged)
        {
            _debugLogged = true;
            LunaLog.LogDebug($"[Gizmo] viewportPos={viewportPos}, viewportSize={viewportSize}");
            LunaLog.LogDebug($"[Gizmo] entity.Transform: pos={transform.Translation}, rot={transform.Rotation}, scale={transform.Scale}");
            LunaLog.LogDebug($"[Gizmo] matrix={matrix}");
            LunaLog.LogDebug($"[Gizmo] view={view}");
            LunaLog.LogDebug($"[Gizmo] projection={projection}");
            LunaLog.LogDebug($"[Gizmo] operation={CurrentOperation}, mode={CurrentMode}");
        }

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

        if (ImGuizmo.IsUsingAny())
        {
            if (Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
            {
                entity.SetTransform(new Transform
                {
                    Translation = translation,
                    Rotation = rotation,
                    Scale = scale
                });
            }
        }
    }
}
