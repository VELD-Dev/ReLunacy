using Bliss.CSharp.Transformations;
using Hexa.NET.ImGui;
using LibLunacy.Numerics;
using ReLunacy.Core.EntityManagement;
using ReLunacy.Core.Gizmo;
using ReLunacy.Core.Selection;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System.Numerics;

namespace ReLunacy.Core.Frames.DockedFrames;

public class PropertyInspectorFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().WorkSize;
    protected override ImGuiWindowFlags WindowFlags { get; set; }

    private Vector3 selectedPosition;
    private Vector3 selectedAngle;
    private Vector3 selectedScale;
    private Vector3 selectedBSphere;
    private float selectedBSphereRadius;

    public Entity? SelectedEntity => SelectionManager.Singleton.SelectedEntity;

    public PropertyInspectorFrame() : base()
    {
        FrameName = LM.Get("GUI_Frame_InstanceInspector");
        SelectionManager.Singleton.SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(Entity? oldEntity, Entity? newEntity)
    {
        if (newEntity is null)
        {
            selectedAngle = Vector3.Zero;
            selectedBSphere = Vector3.Zero;
            selectedBSphereRadius = 0f;
            selectedPosition = Vector3.Zero;
            selectedScale = Vector3.One;
            return;
        }

        selectedPosition = newEntity.Transform.Translation;
        selectedAngle = QuaternionToEulerDegrees(newEntity.Transform.Rotation);
        selectedScale = newEntity.Transform.Scale;
        selectedBSphere = newEntity.BoundingSphere.GetXYZ();
        selectedBSphereRadius = newEntity.BoundingSphere.W;
    }

    private static Vector3 QuaternionToEulerDegrees(Quaternion q)
    {
        Vector3 euler = ToEulerAngles(q);
        return euler * (180f / MathF.PI);
    }

    private static Vector3 ToEulerAngles(Quaternion q)
    {
        Vector3 angles;

        // Roll (x-axis rotation)
        float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.X = MathF.Atan2(sinr_cosp, cosr_cosp);

        // Pitch (y-axis rotation)
        float sinp = 2 * (q.W * q.Y - q.Z * q.X);
        if (MathF.Abs(sinp) >= 1)
            angles.Y = MathF.CopySign(MathF.PI / 2, sinp);
        else
            angles.Y = MathF.Asin(sinp);

        // Yaw (z-axis rotation)
        float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Z = MathF.Atan2(siny_cosp, cosy_cosp);

        return angles;
    }

    private static Quaternion EulerDegreesToQuaternion(Vector3 euler)
    {
        Vector3 radians = euler * (MathF.PI / 180f);
        return Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
    }

    protected override void Render(double deltaTime)
    {
        if (SelectedEntity == null)
        {
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_WaitingForSelection"));
            RenderGizmoControls();
            return;
        }

        ImGui.BeginGroup();

        ImGui.Text($"Name: {SelectedEntity.Name}");
        ImGui.Text($"ID: {SelectedEntity.ID}");

        ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_TransformCategory"));

        bool transformChanged = false;

        if (ImGui.DragFloat3(LM.Get("GUI_Frame_InstanceInspector_Position"), ref selectedPosition, 0.1f))
        {
            transformChanged = true;
        }

        if (ImGui.DragFloat3(LM.Get("GUI_Frame_InstanceInspector_Rotation"), ref selectedAngle, 1f))
        {
            transformChanged = true;
        }

        if (ImGui.DragFloat3(LM.Get("GUI_Frame_InstanceInspector_Scale"), ref selectedScale, 0.01f))
        {
            transformChanged = true;
        }

        if (transformChanged)
        {
            SelectedEntity.Transform = new Transform
            {
                Translation = selectedPosition,
                Rotation = EulerDegreesToQuaternion(selectedAngle),
                Scale = selectedScale
            };
            SelectedEntity.IsDirty = true;
        }

        ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_RenderingCategory"));

        ImGui.InputFloat3("Bounding Sphere", ref selectedBSphere, "%.3f", ImGuiInputTextFlags.ReadOnly);
        ImGui.InputFloat("Sphere Radius", ref selectedBSphereRadius, 0, 0, "%.3f", ImGuiInputTextFlags.ReadOnly);

        ImGui.Separator();

        RenderGizmoControls();

        ImGui.EndGroup();
    }

    private void RenderGizmoControls()
    {
        ImGui.SeparatorText("Gizmo");

        string[] operations = ["Translate (1)", "Rotate (2)", "Scale (3)"];
        int currentOp = (int)GizmoManager.CurrentOperation;
        if (ImGui.Combo("Operation", ref currentOp, operations, operations.Length))
        {
            GizmoManager.SetOperation((GizmoOperation)currentOp);
        }

        string[] spaces = ["Local", "World"];
        int currentSpace = (int)GizmoManager.CurrentSpace;
        if (ImGui.Combo("Space (X)", ref currentSpace, spaces, spaces.Length))
        {
            GizmoManager.SetSpace((GizmoSpace)currentSpace);
        }
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(new(200, 400), ImGuiCond.Once);
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Once, new(0.5f));
        base.RenderAsWindow(deltaTime);
    }
}