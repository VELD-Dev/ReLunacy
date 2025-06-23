using ImGuiNET;
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
using Vortice.Mathematics;

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

    private bool selectionChangeHandled = false;

    public Entity? SelectedEntity
    {
        get
        {
            if (!LunaWindow.Instance.IsAnyFrameOpened<View3D>())
                return null;
            return null; //LunaWindow.Instance.GetFirstFrame<View3D>().SelectedEntity;
        }
    }

    public PropertyInspectorFrame() : base()
    {
        FrameName = LM.Get("GUI_Frame_InstanceInspector");

        if (LunaWindow.Instance.IsAnyFrameOpened<View3D>())
        {
            var v3d = LunaWindow.Instance.GetFirstFrame<View3D>();
            //v3d.SelectedEntityChanged += UpdateEntity;
            selectionChangeHandled = true;
        }

        LunaWindow.Instance.OnFrameAdded += CheckIfNewFrameIsV3D;
        LunaWindow.Instance.OnFrameRemoved += CheckIfRemFrameIsV3D;
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
            /*
            ImGui.BeginGroup();

            ImGui.BeginGroup();
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_InstanceName"));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_InstanceType"));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_Vertices"));
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text(SelectedEntity.name.Split('/')[^1]);
            ImGui.SameLine();
            ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_InstanceInspector_NameChangeNotice"));
            ImGui.Text(SelectedEntity.EntityType.ToString());
            ImGui.Text(SelectedEntity.Model.StaticVerticesCount.ToString());
            ImGui.EndGroup();

            ImGui.BeginGroup();
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_ObjectPath"));
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextWrapped(SelectedEntity.name);
            ImGui.EndGroup();

            ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_TransformCategory"));

            if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Position"), ref selectedPosition, "%.3fm"))
            {
                SelectedEntity.Transform.Position = selectedPosition;
            }
            //if (ImGui.IsItemDeactivatedAfterEdit()) UpdateEntity();
            if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Rotation"), ref selectedAngle, "%.1f°"))
            {
                SelectedEntity.Transform.EulerRotation = selectedAngle * (MathF.PI / 180f);
            }
            //if (ImGui.IsItemDeactivatedAfterEdit()) UpdateEntity();
            if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Scale"), ref selectedScale, "%.3f"))
            {
                SelectedEntity.Transform.Scale = selectedScale;
            }
            //if (ImGui.IsItemDeactivatedAfterEdit()) UpdateEntity();

            ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_RenderingCategory"));

            if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_BoundingSpherePos"), ref selectedBSphere, "%.3fm", ImGuiInputTextFlags.ReadOnly))
            {
                SelectedEntity.boundingSphere.XYZ = selectedBSphere;
            }
            ImGui.InputFloat(LM.Get("GUI_Frame_InstanceInspector_BoundingSphereSize"), ref SelectedEntity.boundingSphere.W, 0, 0, "%.3f", ImGuiInputTextFlags.ReadOnly);

            ImGui.Separator();

            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_ViewToEntity")))
            {
                Camera.Main.transform.Position = -(SelectedEntity.Transform.Position + (Camera.Main.transform.Forward * 10f));
            }
            ImGui.SameLine();
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_DistanceViewEntity", SelectedEntity.Transform.Position.DistanceFrom(-Camera.Main.transform.Position)));

            ImGui.EndGroup();
            */
        }
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(new(200, 400), ImGuiCond.Once);
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Once, new(0.5f));
        base.RenderAsWindow(deltaTime);
    }

    private void CheckIfNewFrameIsV3D(Frame frame)
    {
        /*
        if (!selectionChangeHandled)
            if (frame is View3D v3d)
                v3d.SelectedEntityChanged += UpdateEntity;
        */
    }

    private void CheckIfRemFrameIsV3D(Frame frame)
    {
        if (selectionChangeHandled)
        {
            if (frame is View3D v3d)
            {
                // v3d.SelectedEntityChanged -= UpdateEntity;
                selectionChangeHandled = false;
            }
        }

        if (frame is PropertyInspectorFrame self)
        {
            LunaWindow.Instance.OnFrameAdded -= CheckIfNewFrameIsV3D;
            LunaWindow.Instance.OnFrameRemoved -= CheckIfRemFrameIsV3D;
        }
    }

    private void UpdateEntity(Entity? newSelection)
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

        LunaLog.LogDebug($"Moving entity.");
    }
}
