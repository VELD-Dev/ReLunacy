using System.Numerics;
using Bliss.CSharp.Transformations;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

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

    public PropertyInspectorFrame()
    {
        FrameName = LM.Get("GUI_Frame_InstanceInspector");
        SelectionManager.Singleton.SelectionChanged += UpdateEntity;
    }

    protected override void Render(double deltaTime)
    {
        if (SelectedEntity == null)
        {
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_WaitingForSelection"));
            return;
        }

        var v3d = Core.LunaWindow.Instance.GetFirstFrame<View3D>();

        ImGui.BeginGroup();

        ImGui.BeginGroup();
        ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_InstanceName"));
        ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_InstanceType"));
        ImGui.EndGroup();
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.Text(SelectedEntity.Name.Split('/')[^1]);
        ImGui.SameLine();
        ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_InstanceInspector_NameChangeNotice"));
        ImGui.Text(SelectedEntity.GetType().Name);
        ImGui.EndGroup();

        ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_TransformCategory"));

        if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Position"), ref selectedPosition, "%.3fm"))
        {
            var t = SelectedEntity.Transform;
            t.Translation = selectedPosition;
            SelectedEntity.Transform = t;
        }
        if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Rotation"), ref selectedAngle, "%.1f°"))
        {
            var t = SelectedEntity.Transform;
            t.Rotation = (selectedAngle * (MathF.PI / 180f)).QuaternionFromEuler();
            SelectedEntity.Transform = t;
        }
        if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Scale"), ref selectedScale, "%.3f"))
        {
            // EntityVolume keeps its real box size in its own `scale` field rather than
            // Transform.Scale (which it always leaves at 1,1,1 — see EntityVolume's constructor
            // comment) — writing to Transform.Scale here for a Volume would silently do nothing.
            if (SelectedEntity is EntityVolume volume)
            {
                volume.SetScale(selectedScale);
            }
            else
            {
                var t = SelectedEntity.Transform;
                t.Scale = selectedScale;
                SelectedEntity.Transform = t;
            }
        }

        ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_RenderingCategory"));

        if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_BoundingSpherePos"), ref selectedBSphere, "%.3fm", ImGuiInputTextFlags.ReadOnly))
        {
            SelectedEntity.BoundingSphere = new Vector4(selectedBSphere, SelectedEntity.BoundingSphere.W);
        }
        if (ImGui.InputFloat(LM.Get("GUI_Frame_InstanceInspector_BoundingSphereSize"), ref selectedBSphereRadius, 0, 0, "%.3f", ImGuiInputTextFlags.ReadOnly))
        {
            var s = SelectedEntity.BoundingSphere;
            SelectedEntity.BoundingSphere = new Vector4(s.X, s.Y, s.Z, selectedBSphereRadius);
        }

        if (SelectedEntity is EntityMoby moby)
        {
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_OpenInAssetViewer")))
                OpenMobyInAssetViewer(moby.BaseMoby.Id);
        }
        else if (SelectedEntity is EntityTie tie)
        {
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_OpenInAssetViewer")))
                OpenTieInAssetViewer(tie.BaseTie.Id);
        }
        else if (SelectedEntity is EntityUFrag ufrag)
        {
            var mat = ufrag.UFrag.Material;
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_MaterialRenderMode", mat.RenderMode));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_MaterialAlphaClip", mat.AlphaClipThreshold));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_MaterialAlbedoFormat", mat.AlbedoTexture?.Format.ToString() ?? "None"));
        }
        else if (SelectedEntity is EntityVolume volumeEntity)
        {
            // Volumes carry nothing beyond a transform in the level format itself — old engine has
            // no ID/group at all (BaseVolume.Id is just its load-order index there), new engine adds
            // a TUID + zone group from gp_prius's instance metadata section. This is genuinely all
            // there is to show; see RegionReader.ReadVolumesOld/New.
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_VolumeId", volumeEntity.BaseVolume.Id));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_VolumeGroup", volumeEntity.BaseVolume.group));
        }

        ImGui.Separator();

        if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_ViewToEntity")) && v3d != null)
        {
            // Only the entity's position needs negating to match Camera.Position's convention
            // (see the distance readout below, which negates Camera.Position the same way to
            // compare it against a normal entity-space position) — negating the whole sum,
            // as this used to, also flipped the pull-back offset, pushing the camera away from
            // the entity along its forward vector instead of placing it just short of it.
            v3d.Camera.Position = -SelectedEntity.Transform.Translation + v3d.Camera.GetForward() * 10;
        }
        ImGui.SameLine();
        if (v3d != null)
        {
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_DistanceViewEntity", SelectedEntity.Transform.Translation.DistanceFrom(-v3d.Camera.Position)));
        }

        ImGui.EndGroup();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Once, new Vector2(0.5f));
        base.RenderAsWindow(deltaTime);
    }

    private static void OpenMobyInAssetViewer(ulong mobyId)
    {
        var viewer = OpenAssetViewer();
        if (viewer != null)
        {
            viewer.SelectMobyById(mobyId);
            viewer.Focus();
        }
    }

    private static void OpenTieInAssetViewer(ulong tieId)
    {
        var viewer = OpenAssetViewer();
        if (viewer != null)
        {
            viewer.SelectTieById(tieId);
            viewer.Focus();
        }
    }

    private static AssetViewer? OpenAssetViewer()
    {
        var viewer = LunaWindow.Instance.GetFirstFrame<AssetViewer>();
        if (viewer == null)
        {
            viewer = new AssetViewer(LunaWindow.Instance.GraphicsDevice);
            LunaWindow.Instance.AddFrame(viewer);
        }
        if (LunaWindow.Instance.AssetManager == null || LunaWindow.Instance.Level == null)
            return null;

        viewer.TransmitAssets(LunaWindow.Instance.AssetManager, LunaWindow.Instance.Level.Mobys, LunaWindow.Instance.Level.Ties);
        return viewer;
    }

    private void UpdateEntity(Entity? oldSelection, Entity? newSelection)
    {
        if (SelectedEntity is null)
        {
            selectedAngle = Vector3.Zero;
            selectedBSphere = Vector3.Zero;
            selectedPosition = Vector3.Zero;
            selectedScale = Vector3.Zero;
            return;
        }

        selectedPosition = SelectedEntity.Transform.Translation;
        selectedAngle = SelectedEntity.Transform.Rotation.ToEuler() * (180f / MathF.PI);
        selectedScale = SelectedEntity is EntityVolume volume ? volume.scale : SelectedEntity.Transform.Scale;
        selectedBSphere = SelectedEntity.BoundingSphere.GetXYZ();
        selectedBSphereRadius = SelectedEntity.BoundingSphere.W;
    }
}
