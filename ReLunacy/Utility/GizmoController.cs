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

    public bool SnapEnabled { get; set; }
    public float SnapTranslation { get; set; } = 1.0f;
    public float SnapRotation { get; set; } = 15.0f;
    public float SnapScale { get; set; } = 0.25f;

    public bool IsUsing => ImGuizmo.IsUsingAny();

    public void Render(Cam3D camera, Entity? entity, Vector2 viewportPos, Vector2 viewportSize)
    {
        if (entity == null) return;

        ImGuizmo.BeginFrame();
        ImGuizmo.SetRect(viewportPos.X, viewportPos.Y, viewportSize.X, viewportSize.Y);

        var view = camera.GetView();
        var projection = camera.GetProjection();

        var transform = entity.Transform;
        var matrix = Matrix4x4.CreateScale(transform.Scale)
                   * Matrix4x4.CreateFromQuaternion(transform.Rotation)
                   * Matrix4x4.CreateTranslation(transform.Translation);

        if (SnapEnabled)
        {
            float snap = CurrentOperation switch
            {
                ImGuizmoOperation.Translate => SnapTranslation,
                ImGuizmoOperation.Rotate => SnapRotation,
                ImGuizmoOperation.Scale => SnapScale,
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
