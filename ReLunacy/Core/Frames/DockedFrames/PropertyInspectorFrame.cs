using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Rendering;
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
    private float selectedCullDistance;
    private float selectedUpdateDistance;

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
        ImGui.TextWrapped(SelectedEntity.Name);
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
        if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Rotation"), ref selectedAngle, "%.1f deg"))
        {
            var t = SelectedEntity.Transform;
            t.Rotation = (selectedAngle * (MathF.PI / 180f)).QuaternionFromEuler();
            SelectedEntity.Transform = t;
        }
        if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Scale"), ref selectedScale, "%.3f"))
        {
            // EntityVolume keeps its real box size in its own `scale` field, not Transform.Scale.
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

        if (v3d != null)
        {
            ImGui.Text($"{(v3d.Camera.Position - selectedPosition).Length():N03}m away");
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
            if (ImGui.InputFloat(LM.Get("GUI_Frame_InstanceInspector_CullDistance"), ref selectedCullDistance, 0, 0,
                    "%.3f", ImGuiInputTextFlags.ReadOnly))
            {
                ((EntityMoby)SelectedEntity).DisplayDistance = selectedCullDistance;
            }
            if (ImGui.InputFloat(LM.Get("GUI_Frame_InstanceInspector_UpdateDistance"), ref selectedUpdateDistance, 0, 0,
                    "%.3f", ImGuiInputTextFlags.ReadOnly))
            {
                ((EntityMoby)SelectedEntity).UpdateDistance = selectedUpdateDistance;
            }

            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_OpenInAssetViewer")))
                OpenMobyInAssetViewer(moby.BaseMoby.Id);

            ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_AnimationCategory"));
            RenderAnimationSection(moby);
        }
        else if (SelectedEntity is EntityTie tie)
        {
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_OpenInAssetViewer")))
                OpenTieInAssetViewer(tie.BaseTie.Id);
        }
        else if (SelectedEntity is EntityUFrag ufrag)
        {
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_OpenInAssetViewer")))
                OpenUFragInAssetViewer(ufrag.UFrag);
        }
        else if (SelectedEntity is EntityVolume volumeEntity)
        {
            // Volumes carry nothing beyond a transform in the level format itself - this is all there is to show.
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_VolumeId", volumeEntity.BaseVolume.Id));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_VolumeGroup", volumeEntity.BaseVolume.group));
        }

        ImGui.Separator();

        if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_ViewToEntity")) && v3d != null)
        {
            // Only the entity's position needs negating to match Camera.Position's convention.
            v3d.Camera.Position = SelectedEntity.Transform.Translation - v3d.Camera.GetForward() * 10;
        }
        ImGui.SameLine();
        if (v3d != null)
        {
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_DistanceViewEntity", SelectedEntity.Transform.Translation.DistanceFrom(-v3d.Camera.Position)));
        }

        ImGui.EndGroup();
    }

    // No RenderAsWindow override - see ShaderBrowser's comment: SetNextWindowPos on first appearance
    // cancels the dockspace preset's placement, which this frame is a target of ("Inspector").

    /// <summary>Off-by-default GPU playback in the 3D View itself, distinct from the Asset Viewer's
    /// own CPU-preview animation panel. Membership in EntityManager.PlayingMobyAnimations (what
    /// actually drives per-frame sampling - see View3D.UpdateAnimatedMobys) is owned entirely by the
    /// Play/Stop buttons here.</summary>
    private static void RenderAnimationSection(EntityMoby moby)
    {
        var skeleton = moby.BaseMoby.Skeleton;
        var clips = moby.BaseMoby.Animations;
        if (skeleton == null || clips.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_InstanceInspector_Animation_None"));
            return;
        }

        var player = moby.AnimationPlayer;
        string preview = player.Clip?.Name ?? LM.Get("GUI_Frame_InstanceInspector_Animation_NoneSelected");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##anim_clip", preview))
        {
            for (int i = 0; i < clips.Count; i++)
            {
                // ##anim_{i} disambiguates the ImGui ID from the label text: some mobys have several
                // clips sharing the same name, and Selectable's ID is derived from the label alone -
                // without a unique suffix, duplicate names collide and corrupt the ID stack (visible
                // as PopID errors in the dropdown).
                if (ImGui.Selectable($"{clips[i].Name}##anim_{i}", ReferenceEquals(player.Clip, clips[i])))
                {
                    bool wasPlaying = player.IsPlaying;
                    player.SetClip(clips[i]);
                    if (wasPlaying) player.Play();
                }
            }
            ImGui.EndCombo();
        }

        if (player.Clip == null) return;

        if (!player.CanSamplePose)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f), player.UnsupportedReason ?? LM.Get("GUI_Frame_InstanceInspector_Animation_Unsupported"));
            return;
        }

        if (!player.IsPlaying)
        {
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_Animation_Play")))
            {
                player.Play();
                if (!EntityManager.Singleton.PlayingMobyAnimations.Contains(moby))
                    EntityManager.Singleton.PlayingMobyAnimations.Add(moby);
            }
        }
        else if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_Animation_Pause")))
        {
            player.Pause();
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_Animation_Stop")))
        {
            player.Stop();
            EntityManager.Singleton.PlayingMobyAnimations.Remove(moby);
            Core.LunaWindow.Instance.AssetManager?.SceneRenderer?.ClearAnimatedInstance(moby);
        }
        ImGui.SameLine();
        bool loop = player.Loop;
        if (ImGui.Checkbox(LM.Get("GUI_Frame_InstanceInspector_Animation_Loop"), ref loop))
            player.Loop = loop;
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

    // Takes the IUFrag instance itself, not an id - IUFrag.Id is only unique within its own zone
    // (ZoneReader assigns it as a local per-zone loop index), so a level with more than one zone
    // routinely has several UFrags sharing the same Id. See AssetViewer.SelectUFrag.
    private static void OpenUFragInAssetViewer(IUFrag ufrag)
    {
        var viewer = OpenAssetViewer();
        if (viewer != null)
        {
            viewer.SelectUFrag(ufrag);
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
            selectedCullDistance = 0f;
            selectedUpdateDistance = 0f;
            return;
        }

        selectedPosition = SelectedEntity.Transform.Translation;
        selectedAngle = SelectedEntity.Transform.Rotation.ToEuler() * (180f / MathF.PI);
        selectedScale = SelectedEntity is EntityVolume volume ? volume.scale : SelectedEntity.Transform.Scale;
        selectedBSphere = SelectedEntity.BoundingSphere.GetXYZ();
        selectedBSphereRadius = SelectedEntity.BoundingSphere.W;
        if (SelectedEntity is EntityMoby moby)
        {
            selectedCullDistance = moby.DisplayDistance;
            selectedUpdateDistance = moby.UpdateDistance;
        }
    }
}
