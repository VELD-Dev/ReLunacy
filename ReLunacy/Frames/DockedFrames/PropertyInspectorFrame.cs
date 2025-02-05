using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Frames.DockedFrames;

public class PropertyInspectorFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vec2 DefaultPosition { get; set; } = ImGui.GetMainViewport().WorkSize;
    protected override ImGuiWindowFlags WindowFlags { get; set; }

    private System.Numerics.Vector3 selectedPosition;
    private System.Numerics.Vector3 selectedAngle;
    private System.Numerics.Vector3 selectedScale;
    private System.Numerics.Vector3 selectedBSphere;

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
            ImGui.Text("Type:");
            ImGui.Text("Vertices:");
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text(SelectedEntity.name.Split('/')[^1]);
            ImGui.SameLine();
            ImGuiPlus.HelpMarker("The entity name cannot be changed.");
            ImGui.Text(SelectedEntity.ID.ToString());
            ImGui.SameLine();
            ImGuiPlus.HelpMarker("An internal generated ID for rendering. Irrelevant.");
            ImGui.Text(SelectedEntity.EntityType.ToString());
            ImGui.Text("---");
            ImGui.EndGroup();

            ImGui.BeginGroup();
            ImGui.Text("Object path:");
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextWrapped(SelectedEntity.name);
            ImGui.EndGroup();

            ImGui.SeparatorText("Transform");

            if (ImGui.InputFloat3("Position", ref selectedPosition, "%.3fm"))
            {
                SelectedEntity.Transform.Position = selectedPosition;
            }
            //if (ImGui.IsItemDeactivatedAfterEdit()) UpdateEntity();
            if (ImGui.InputFloat3("Rotation", ref selectedAngle, "%.1f°"))
            {
                SelectedEntity.Transform.EulerRotation = selectedAngle * (MathF.PI / 180f);
            }
            //if (ImGui.IsItemDeactivatedAfterEdit()) UpdateEntity();
            if(ImGui.InputFloat3("Scale", ref selectedScale, "%.3f"))
            {
                SelectedEntity.Transform.Scale = selectedScale;
            }
            //if (ImGui.IsItemDeactivatedAfterEdit()) UpdateEntity();

            ImGui.SeparatorText("Rendering");

            if(ImGui.InputFloat3("Bounding Sphere Pos.", ref selectedBSphere, "%.3fm", ImGuiInputTextFlags.ReadOnly))
            {
                SelectedEntity.boundingSphere.XYZ = selectedBSphere;
            }
            ImGui.InputFloat("Bounding Sphere Size", ref SelectedEntity.boundingSphere.W, 0, 0, "%.3f", ImGuiInputTextFlags.ReadOnly);

            ImGui.Spacing();
            
            if(ImGui.Button("Teleport to Entity"))
            {
                Camera.Main.transform.Position = -(SelectedEntity.Transform.Position + (Camera.Main.transform.Forward * 10f));
            }
            ImGui.SameLine();
            ImGui.Text($"({SelectedEntity.Transform.Position.DistanceFrom(-Camera.Main.transform.Position):N3}m away)");

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
        if (SelectedEntity is null) return;

        selectedAngle = SelectedEntity.Transform.EulerRotation * (180f / MathF.PI);

        LunaLog.LogDebug($"Moving entity.");
    }
}
