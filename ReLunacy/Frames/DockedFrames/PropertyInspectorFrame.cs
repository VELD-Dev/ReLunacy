using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Frames.DockedFrames;

public class PropertyInspectorFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override System.Numerics.Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().WorkSize;
    protected override ImGuiWindowFlags WindowFlags { get; set; }

    public Entity? SelectedEntity
    { 
        get
        {
            if(!Window.Singleton.IsAnyFrameOpened<View3DFrame>())
                return null;
            return Window.Singleton.GetFirstFrame<View3DFrame>().SelectedEntity;
        }
    }

    public PropertyInspectorFrame() : base()
    {
        FrameName = "Instance Properties";
    }

    protected override void Render(float deltaTime)
    {
        if(SelectedEntity == null)
        {
            ImGui.Text("Select an entity...");
            return;
        }
        else
        {
            ImGui.BeginGroup();

            ImGui.BeginGroup();
            ImGui.Text("Name:");
            ImGui.Text("Instance ID:");
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text(SelectedEntity.name.Split('/')[^1]);
            ImGui.SameLine();
            ImGuiPlus.HelpMarker("The entity name cannot be changed.");
            ImGui.Text(SelectedEntity.id.ToString());
            ImGui.SameLine();
            ImGuiPlus.HelpMarker("An internal generated ID for rendering. Irrelevant.");
            ImGui.EndGroup();

            ImGui.SeparatorText("Transform");

            ImGui.InputFloat3("Position", ref SelectedEntity.transform.position, "%.3f");
            if (ImGui.IsItemDeactivatedAfterEdit()) UpdateEntity();
            ImGui.InputFloat3("Rotation (rad)", ref SelectedEntity.transform.eulerRotation, "%.4f");
            if (ImGui.IsItemDeactivatedAfterEdit()) UpdateEntity();
            ImGui.InputFloat3("Scale", ref SelectedEntity.transform.scale, "%.3f");
            if (ImGui.IsItemDeactivatedAfterEdit()) UpdateEntity();

            ImGui.SeparatorText("Rendering");

            var bs = new System.Numerics.Vector3(SelectedEntity.boundingSphere.X, SelectedEntity.boundingSphere.Y, SelectedEntity.boundingSphere.Z);
            ImGui.InputFloat3("Bounding Sphere Pos.", ref bs, "%.3f", ImGuiInputTextFlags.ReadOnly);
            ImGui.InputFloat("Bounding Sphere Size", ref SelectedEntity.boundingSphere.W, 0, 0, "%.3f", ImGuiInputTextFlags.ReadOnly);

            ImGui.Spacing();
            
            if(ImGui.Button("Teleport to Entity"))
            {
                Camera.Main.transform.position = -SelectedEntity.transform.position;
            }
            ImGui.SameLine();
            ImGui.Text($"({SelectedEntity.transform.position.DistanceFrom(-Camera.Main.transform.position):N3}m away)");

            ImGui.EndGroup();
        }
    }

    public override void RenderAsWindow(float deltaTime)
    {
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Once, new(1f));
        base.RenderAsWindow(deltaTime);
    }

    public void UpdateEntity()
    {
        SelectedEntity?.UpdateTransform();
        LunaLog.LogDebug($"Moving entity.");
    }
}
