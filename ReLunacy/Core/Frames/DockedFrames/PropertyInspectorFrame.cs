
using LibLunacy.Numerics;
using ReLunacy.Core.EntityManagement;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Bliss.CSharp.Transformations;
using ReLunacy.Core.Selection;
using Vortice.Mathematics;
using ReLunacy.Utility;

namespace ReLunacy.Core.Frames.DockedFrames;

public class PropertyInspectorFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().WorkSize;
    protected override ImGuiWindowFlags WindowFlags { get; set; }

    private System.Numerics.Vector3 selectedPosition;
    private System.Numerics.Vector3 selectedAngle;
    private System.Numerics.Vector3 selectedScale;
    private System.Numerics.Vector3 selectedBSphere;
    private float selectedBSphereRadius;
    
    private bool selectionChangeHandled = false;

    public Entity? SelectedEntity => SelectionManager.Singleton.SelectedEntity;

    public PropertyInspectorFrame() : base()
    {
        FrameName = LM.Get("GUI_Frame_InstanceInspector");

        if (LunaWindow.Instance.IsAnyFrameOpened<View3D>())
        {
            SelectionManager.Singleton.SelectionChanged += UpdateEntity;
            selectionChangeHandled = true;
        }
    }

    protected override void Render(double deltaTime)
    {
        if (SelectedEntity == null)
        {
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_WaitingForSelection"));
            return;
        }
        else
        {
            var v3d = LunaWindow.Instance.GetFirstFrame<View3D>();
            
            ImGui.BeginGroup();

            ImGui.BeginGroup();
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_InstanceName"));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_InstanceType"));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_Vertices"));
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text(SelectedEntity.Name.Split('/')[^1]);
            ImGui.SameLine();
            ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_InstanceInspector_NameChangeNotice"));
            ImGui.Text(SelectedEntity.GetType().Name);
            ImGui.Text("unsupported");
            ImGui.EndGroup();

            ImGui.BeginGroup();
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_ObjectPath"));
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextWrapped(SelectedEntity.Name);
            ImGui.EndGroup();

            ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_TransformCategory"));

            if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Position"), ref selectedPosition, "%.3fm"))
            {
                SelectedEntity.SetTranslation(selectedPosition);
            }
            if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Rotation"), ref selectedAngle, "%.1f°"))
            {
                SelectedEntity.SetRotation(
                    (SelectedEntity.Transform.Rotation.ToEuler() + selectedAngle * (MathF.PI / 180f))
                    .QuaternionFromEuler());
            }
            if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Scale"), ref selectedScale, "%.3f"))
            {
                SelectedEntity.SetScale(selectedScale);
            }

            ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_RenderingCategory"));

            if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_BoundingSpherePos"), ref selectedBSphere, "%.3fm", ImGuiInputTextFlags.ReadOnly))
            {
                SelectedEntity.SetBoundingSpherePosition(selectedBSphere);
            }
            if(ImGui.InputFloat(LM.Get("GUI_Frame_InstanceInspector_BoundingSphereSize"), ref selectedBSphereRadius, 0, 0, "%.3f", ImGuiInputTextFlags.ReadOnly)) 
            {
                SelectedEntity.SetBoundingSphereRadius(selectedBSphereRadius);
            }

            ImGui.Separator();

            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_ViewToEntity")) && v3d != null)
            {
                v3d.Camera.Position = -(SelectedEntity.Transform.Translation + (v3d.Camera.GetForward() * 10));
            }
            ImGui.SameLine();
            if (v3d != null)
            {
                ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_DistanceViewEntity", SelectedEntity.Transform.Translation.DistanceFrom(-v3d.Camera.Position)));
            }

            ImGui.EndGroup();
        }
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(new(200, 400), ImGuiCond.Once);
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Once, new(0.5f));
        base.RenderAsWindow(deltaTime);
    }

    private void UpdateEntity(Entity? oldSelection, Entity? newSelection)
    {
        if (SelectedEntity is null)
        {
            selectedAngle = Vec3.Zero;
            selectedBSphere = Vec3.Zero;
            selectedPosition = Vec3.Zero;
            selectedScale = Vec3.Zero;
            return;
        }

        selectedPosition = SelectedEntity.Transform.Translation;
        selectedAngle = SelectedEntity.Transform.Rotation.ToEuler() * (180f / MathF.PI);
        selectedScale = SelectedEntity.Transform.Scale;
        selectedBSphere = SelectedEntity.BoundingSphere.GetXYZ();

        LunaLog.LogDebug($"Moving entity {SelectedEntity.Name}.");
    }
}
