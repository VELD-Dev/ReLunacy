using ReLunacy.Engine.Assets.LevelElements;
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
    /// <summary>Opens (or focuses) the Cubemap Viewer on a cubemaps.dat resource.</summary>
    private static void OpenCubemapInViewer(ulong resourceTuid)
    {
        var window = LunaWindow.Instance;
        CubemapViewer viewer;
        if (window.IsAnyFrameOpened<CubemapViewer>())
            viewer = window.GetFirstFrame<CubemapViewer>()!;
        else
        {
            viewer = new CubemapViewer();
            viewer.TransmitCubemaps(window.Level);
            window.AddFrame(viewer);
        }
        viewer.SelectByResource(resourceTuid);
        viewer.Focus();
    }

    /// <summary>Which environment cubemap this instance actually reflects, and why.
    ///
    /// Two independent lookups decide it. The RESOURCE comes from the instance's own authored cube
    /// TUID, resolved through the active region's resource map (VulkanRenderer: _cubeResourceMap);
    /// the SLOT within that resource comes from the material's 0x5000 +0x40. The resource map is
    /// rebuilt whenever the rendered region changes, because a region owns its cubemap set - so an
    /// instance whose TUID is not in the current region's set silently falls back to resource 0,
    /// which is exactly the case worth seeing rather than guessing at.</summary>
    private static void RenderCubemapSection(ulong cubeTuid)
    {
        var am = LunaWindow.Instance.AssetManager;
        var level = LunaWindow.Instance.Level;
        if (am is null || level is null) return;

        ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_CubemapCategory"));
        ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_CubemapRegion", am.ActiveEnvironmentRegion ?? "-"));
        ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_CubemapTuidLabel"));
        ImGui.SameLine();
        TuidLink.Draw(cubeTuid);

        if (cubeTuid == 0)
        {
            ImGui.TextWrapped(LM.Get("GUI_Frame_InstanceInspector_CubemapNoTuid"));
            return;
        }

        bool resolved = am.EnvironmentCubeResources.TryGetValue(cubeTuid, out int resource);
        if (!resolved) resource = 0;
        ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_CubemapResource", resource,
            am.EnvironmentCubeResources.Count));
        if (!resolved)
            ImGui.TextWrapped(LM.Get("GUI_Frame_InstanceInspector_CubemapFallback"));

        // The four slots of that resource, in the order AssetManager packs them.
        var ofRegion = am.ActiveEnvironmentRegion is { } r && level.CubemapsByRegion.TryGetValue(r, out var byRegion)
            ? byRegion : level.Cubemaps;
        var ordered = ofRegion.Where(c => c.HalfFloatFaces is { Count: 6 })
            .GroupBy(c => c.ResourceTuid)
            .Select(g => g.OrderBy(c => c.IndexInResource).ToList())
            .ToList();
        if (resource >= 0 && resource < ordered.Count)
        {
            var slots = ordered[resource];
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_CubemapSlotsLabel"));
            foreach (var slot in slots) { ImGui.SameLine(); TuidLink.Draw(slot.Id); }

            // The instance names a RESOURCE, so the jump selects that resource in the viewer rather
            // than a cube key - the two are different identifiers and get confused for each other.
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_CubemapOpen")))
                OpenCubemapInViewer(resolved ? cubeTuid : slots[0].ResourceTuid);
        }
    }

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
        // The fields follow the entity as the gizmo moves, rotates or sizes it, except while one is being typed in.
        if (!ImGui.IsAnyItemActive()) UpdateEntity(null, SelectedEntity);

        var v3d = Core.LunaWindow.Instance.GetFirstFrame<View3D>();

        RenderMultipleSelection();

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

        if (ClueClasses.KnownBreakingEffect(Core.LunaWindow.Instance.Level?.GameDefinition?.Id, SelectedEntity.Name) is { } breaking)
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.35f, 1f), LM.Get("GUI_Frame_InstanceInspector_KnownBreaking", breaking));
        ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_TransformCategory"));
        // Undo: the entity as it was at the start of this frame, taken as the step's "before" when a field is
        // clicked into (a drag can change the value on that same frame).
        var entity = SelectedEntity;
        var entityBefore = new Lazy<Core.Undo.UndoManager.Session>(() => Core.Undo.UndoManager.Singleton.Begin([entity]));
        if (!ImGui.IsAnyItemActive()) _ = entityBefore.Value;

        if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Position"), ref selectedPosition, "%.3fm"))
        {
            var t = SelectedEntity.Transform;
            t.Translation = selectedPosition;
            SelectedEntity.Transform = t;
        }
        TrackField(entityBefore, "GUI_Undo_Move");
        if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Rotation"), ref selectedAngle, "%.1f deg"))
        {
            var t = SelectedEntity.Transform;
            t.Rotation = (selectedAngle * (MathF.PI / 180f)).QuaternionFromEuler();
            SelectedEntity.Transform = t;
        }
        TrackField(entityBefore, "GUI_Undo_Rotate");
        // A Moby's scale is one number in the file (MobyInstanceNew +0x2C): one field, applied to the three axes.
        if (SelectedEntity is EntityMoby)
        {
            float uniform = selectedScale.X;
            if (ImGui.InputFloat(LM.Get("GUI_Frame_InstanceInspector_Scale"), ref uniform, 0f, 0f, "%.3f"))
            {
                selectedScale = new Vector3(uniform);
                var t = SelectedEntity.Transform;
                t.Scale = selectedScale;
                SelectedEntity.Transform = t;
            }
            TrackField(entityBefore, "GUI_Undo_Scale");
        }
        else if (ImGui.InputFloat3(LM.Get("GUI_Frame_InstanceInspector_Scale"), ref selectedScale, "%.3f"))
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
        if (SelectedEntity is not EntityMoby) TrackField(entityBefore, "GUI_Undo_Scale");

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

        RenderReferenceIds(SelectedEntity);

        if (SelectedEntity is EntityMoby moby)
        {
            RenderPodMembership(moby);
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

            // The instance's class data (new engine): the class struct, its references, editable like a Clue's.
            if (moby.Settings.Length >= 4)
            {
                uint mobyClass = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(moby.Settings);
                ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_MobySettings"));
                ImGui.TextDisabled(LM.Get("GUI_Frame_InstanceInspector_MobyClass", $"0x{mobyClass:X}", moby.BaseMoby.Name?.Split('/')[^1] ?? "?"));
                RenderSettings(moby.Settings, moby.References, mobyClass, moby: true, region: RegionOf(moby));
            }

            ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_AnimationCategory"));
            RenderAnimationSection(moby);
        }
        else if (SelectedEntity is EntityTie tie)
        {
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_OpenInAssetViewer")))
                OpenTieInAssetViewer(tie.BaseTie.Id);
            RenderCubemapSection(tie.NewEngineCubeTuid);
        }
        else if (SelectedEntity is EntityUFrag ufrag)
        {
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_OpenInAssetViewer")))
                OpenUFragInAssetViewer(ufrag.UFrag);
            RenderCubemapSection(ufrag.UFrag.NewEngineCubeTuid);
        }
        else if (SelectedEntity is EntityVolume volumeEntity)
        {
            // A volume is its transform and its shape (the shape only on the new engine; ToD's are all boxes).
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_VolumeId", volumeEntity.BaseVolume.Id));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_VolumeGroup", volumeEntity.BaseVolume.group));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_VolumeShape", volumeEntity.BaseVolume.Shape));
            var baseVolume = volumeEntity.BaseVolume;
            if (baseVolume.Layer != null)
                ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_VolumeLayer", baseVolume.Layer));
            ImGui.Text(baseVolume.Clue is { } clue
                ? LM.Get("GUI_Frame_InstanceInspector_VolumeClue", clue.ClassName, $"0x{clue.Class:X}", clue.Name)
                : baseVolume.Role != null
                    ? LM.Get("GUI_Frame_InstanceInspector_VolumeRole", baseVolume.Role)
                    : LM.Get("GUI_Frame_InstanceInspector_VolumePlain"));
            if (baseVolume.GravityRegion)
                ImGui.TextColored(new Vector4(0.4f, 0.85f, 1f, 1f), LM.Get("GUI_Frame_InstanceInspector_GravityRegion"));
            if (baseVolume.Clue is { Settings.Length: > 4 } clueSettings)
            {
                // The Clue with what it names (a moon: its area and its landing pad beam), so the gizmo
                // moves them as one.
                var clueRegion = RegionOf(volumeEntity);
                var linked = clueSettings.References.Select(r => FindInstance(clueRegion, r.Kind, r.Index)).OfType<Entity>().Distinct().ToList();
                if (linked.Count > 0 && ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_SelectWithReferences", linked.Count)))
                    SelectionManager.Singleton.Set([.. linked, volumeEntity], volumeEntity);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_SelectWithReferencesHint"));
                RenderSettings(clueSettings.Settings, clueSettings.References, clueSettings.Class, region: clueRegion);
            }
            RenderUsedBy(volumeEntity);
            if (baseVolume.Areas.Count > 0)
                ImGui.TextWrapped(LM.Get("GUI_Frame_InstanceInspector_VolumeAreas", string.Join(", ", baseVolume.Areas)));
            if (baseVolume.Scents.Count > 0)
                ImGui.TextWrapped(LM.Get("GUI_Frame_InstanceInspector_VolumeScents", string.Join(", ", baseVolume.Scents)));
        }
        else if (SelectedEntity is EntityCamera camera)
        {
            var controller = camera.Controller;
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_CameraClass",
                controller.ClassName != null ? $"{controller.ClassName} (0x{controller.Class:X})" : $"0x{controller.Class:X}"));
            if (controller.Layer != null)
                ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_VolumeLayer", controller.Layer));
            if (v3d != null)
            {
                bool looking = ReferenceEquals(v3d.PovCamera, camera);
                if (ImGui.Button(LM.Get(looking ? "GUI_Frame_InstanceInspector_CameraPovLeave" : "GUI_Frame_InstanceInspector_CameraPov")))
                {
                    if (looking) v3d.ExitPov(restore: true);
                    else v3d.EnterPov(camera);
                }
                var (fov, authored) = View3D.PovFieldOfView(camera);
                ImGui.SameLine();
                var povGame = Core.LunaWindow.Instance.Level?.GameDefinition?.Id;
                var fovKey = authored ? "GUI_Frame_InstanceInspector_CameraFov"
                    : View3D.GameDefaultFieldOfView(povGame) != null ? "GUI_Frame_InstanceInspector_CameraFovGameDefault"
                    : "GUI_Frame_InstanceInspector_CameraFovDefault";
                ImGui.TextDisabled(LM.Get(fovKey, fov.ToString("0.#")));
            }
            ImGui.TextWrapped(LM.Get("GUI_Frame_InstanceInspector_CameraNote"));
            if (controller.Settings.Length > 4)
                RenderSettings(controller.Settings, controller.References, controller.Class, region: RegionOf(SelectedEntity));
        }
        else if (SelectedEntity is EntityPath pathEntity)
        {
            var path = pathEntity.Path;
            if (path.Layer != null)
                ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_VolumeLayer", path.Layer));
            ImGui.ColorButton("##pathrole", new Vector4(PathRoles.Colour(path.Role), 1f),
                ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop,
                new Vector2(ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            ImGui.TextWrapped(path.RoleSource != null
                ? LM.Get("GUI_Frame_InstanceInspector_PathRoleLinked", path.Role, path.RoleSource)
                : path.Role == PathRoles.Unassigned
                    ? LM.Get("GUI_Frame_InstanceInspector_PathRoleNone")
                    : LM.Get("GUI_Frame_InstanceInspector_PathRoleNamed", path.Role));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_PathSummary", pathEntity.PointCount, pathEntity.Length.ToString("0.##"),
                LM.Get(path.Closed ? "GUI_Frame_InstanceInspector_PathClosed" : "GUI_Frame_InstanceInspector_PathOpen"), path.Kind));
            ImGui.TextWrapped(pathEntity.IsSpline
                ? LM.Get("GUI_Frame_InstanceInspector_PathSpline", pathEntity.KeyCount, EntityPath.SamplesPerSpan)
                : LM.Get("GUI_Frame_InstanceInspector_PathPolyline"));
            ImGui.TextWrapped(LM.Get("GUI_Frame_InstanceInspector_PathHint"));
        }
        else if (SelectedEntity is EntityPathPoint pathPoint)
            RenderPathPart(pathPoint);
        else if (SelectedEntity is EntityNavmesh navmeshChunk)
        {
            // The chunk table's own counts, not recomputed ones, so a disagreement with what is drawn
            // would be visible rather than smoothed over.
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_NavmeshZone", navmeshChunk.ZoneName));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_NavmeshChunk", navmeshChunk.ChunkIndex));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_NavmeshCounts",
                navmeshChunk.VertexCount, navmeshChunk.TriangleCount, navmeshChunk.EdgeCount));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_NavmeshLinks",
                navmeshChunk.LinkedEdgeCount, navmeshChunk.EdgeCount));
            var neighbours = navmeshChunk.Chunk.EdgeNeighbours
                .Where(n => n.HasValue).Select(n => n!.Value).Distinct().Order().ToList();
            ImGui.TextWrapped(LM.Get("GUI_Frame_InstanceInspector_NavmeshNeighbours",
                neighbours.Count > 0 ? string.Join(", ", neighbours) : "-"));
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

    /// <summary>With several entities selected: how many of what, and that what follows is the active one's,
    /// which the gizmo moves the others with.</summary>
    private static void RenderMultipleSelection()
    {
        var selection = SelectionManager.Singleton;
        if (selection.Count < 2) return;
        string kinds = string.Join(", ", selection.Selection
            .GroupBy(e => e.GetType().Name.StartsWith("Entity") ? e.GetType().Name[6..] : e.GetType().Name)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()} {g.Key}"));
        ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_MultipleSelection", selection.Count));
        ImGui.TextWrapped(kinds);
        ImGui.TextDisabled(LM.Get("GUI_Frame_InstanceInspector_MultipleSelectionHint"));
        if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_KeepOnlyActive")))
            selection.Select(selection.SelectedEntity);
        ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_ActiveEntity"));
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
                // ##anim_{i} disambiguates the ImGui ID from the label text: some Mobys have several
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

    /// <summary>The Pods a Moby belongs to (A Crack in Time's level segments among them), each opening the Pods window.</summary>
    private static void RenderPodMembership(EntityMoby moby)
    {
        var pods = EntityManager.Singleton.Regions.SelectMany(r => r.Pods).Where(p => p.Contains(moby)).ToList();
        ImGui.TextDisabled(LM.Get("GUI_Frame_InstanceInspector_Pods"));
        ImGui.SameLine();
        if (pods.Count == 0) { ImGui.TextDisabled("-"); return; }
        foreach (var pod in pods)
        {
            if (ImGui.SmallButton($"{pod.Name}##pod{pod.Tuid}"))
            {
                var frame = LunaWindow.Instance.IsAnyFrameOpened<PodsFrame>() ? LunaWindow.Instance.GetFirstFrame<PodsFrame>() : null;
                if (frame == null) { frame = new PodsFrame(); LunaWindow.Instance.AddFrame(frame); }
                frame.Show(pod);
            }
            ImGui.SameLine();
        }
        ImGui.NewLine();
    }

    /// <summary>One line of the settings: a documented field, or a word nothing documents.</summary>
    private sealed record SettingRow(uint Owner, int Offset, int Size, Engine.Assets.LevelElements.ClassField? Field,
        Engine.Assets.LevelElements.ClassField? Component)
    {
        public bool RunTime => Field is { RunTime: true };
    }

    /// <summary>The instance being dragged from the hierarchy onto a reference field (set by the hierarchy when a
    /// drag starts; the payload itself carries nothing).</summary>
    public static Entity? DraggedEntity;
    public const string DragPayload = "RELUNACY_ENTITY";

    /// <summary>The region an entity is in: its references name that region's instances, and its
    /// <see cref="EntityRegion.References"/> reads and writes them.</summary>
    private static EntityRegion? RegionOf(Entity? entity)
    {
        if (entity == null) return null;
        foreach (var region in EntityManager.Singleton.Regions)
            if (region.MobyInstances.Entities.Contains(entity) || region.Volumes.Entities.Contains(entity)
                || region.Cameras.Entities.Contains(entity) || region.Paths.Entities.Contains(entity))
                return region;
        return null;
    }

    /// <summary>The kind a reference to this entity carries and its place in that kind's table (names repeat, so
    /// they cannot stand for it). A volume with a Clue counts as the Clue when the field wants one.</summary>
    private static (string Kind, int Index)? ReferenceTo(Entity entity, string? wanted) => entity switch
    {
        EntityMoby moby => ("Moby", moby.GameplayIndex),
        EntityVolume { BaseVolume.Clue: { } clue } when wanted == "Clue" => ("Clue", clue.Index),
        EntityVolume volume => ("Volume", volume.BaseVolume.Index),
        EntityPath path => ("Path", path.Path.Index),
        EntityCamera camera => ("Controller", camera.Controller.Index),
        _ => null,
    };

    // The field being documented in the edit popup.
    private static (uint Owner, int Offset, string Name, string Type, bool RunTime, string Description, bool Existing)? _fieldEdit2;
    private static bool _openFieldEdit, _openEnums;
    private static Engine.Assets.LevelElements.ClassEnum? _enumEdit;
    private static string? _enumEditOriginal;
    private static string _referenceFilter = "", _className = "";

    /// <summary>
    /// A Clue's, a controller's or a Moby's settings, folded by the class that declares each field: the instance's
    /// own class open, the classes it inherits from below it, closed. Two columns, name and value. Every documented
    /// field shows, set or not; the words nothing documents show with "Show undocumented". "Edit fields" adds a
    /// button to each row to name it, type it and describe it (kept in ClassFields' user file), and "Enums" manages
    /// the lists a field can be typed with.
    /// </summary>
    private static void RenderSettings(byte[] settings, IReadOnlyList<Engine.Assets.LevelElements.ClueReference> fieldReferences, uint cls,
        bool moby = false, EntityRegion? region = null)
    {
        var refs = region?.References;
        var game = Core.LunaWindow.Instance.Level?.GameDefinition?.Id;
        var registry = Engine.Loading.Readers.ClassRegistry.Find(game);
        if (registry != null && registry.Sizes.TryGetValue(cls, out int classSize) && classSize != settings.Length)
            ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), LM.Get("GUI_Frame_InstanceInspector_ClassSizeMismatch", settings.Length, classSize));
        bool canWriteReferences = refs is { Editable: true };

        // What the settings held when first shown, so an edit stands out and can be undone.
        if (!LoadedSettings.TryGetValue(settings, out var loaded))
            LoadedSettings.Add(settings, loaded = (byte[])settings.Clone());
        var settingsBefore = new Lazy<Core.Undo.UndoManager.Session>(() => Core.Undo.UndoManager.Singleton.Begin([settings]));
        if (!ImGui.IsAnyItemActive()) _ = settingsBefore.Value;
        ImGui.Checkbox(LM.Get("GUI_Frame_InstanceInspector_SettingsShowUndocumented"), ref _showUndocumented);
        ImGui.SameLine();
        ImGui.BeginDisabled(game == null);
        ImGui.Checkbox(LM.Get("GUI_Frame_InstanceInspector_SettingsEditFields"), ref _editFields);
        ImGui.EndDisabled();
        if (_editFields && game is { } tableOf)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(LM.Get("GUI_Frame_InstanceInspector_SettingsEnums"))) _openEnums = true;
            ImGui.SameLine();
            // The table is a text file anyone can also edit by hand: this reads it again.
            if (ImGui.SmallButton(LM.Get("GUI_Frame_InstanceInspector_SettingsReload"))) Engine.Assets.LevelElements.ClassFields.Reload();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_FieldsFileHint", Engine.Assets.LevelElements.ClassFields.FilePath(tableOf)));
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_SettingsHint"));
        var survey = Survey(cls, moby);

        // The rows, by the class that owns each.
        var schema = Engine.Assets.LevelElements.ClassFields.Of(game, cls);
        var components = schema.Where(f => f.Kind == Engine.Assets.LevelElements.ClassFieldKind.Component).ToList();
        var fieldsByWord = schema.Where(f => f.Kind != Engine.Assets.LevelElements.ClassFieldKind.Component).ToLookup(f => f.Offset & ~3);
        var staticReferences = fieldReferences.GroupBy(r => r.Offset).ToDictionary(g => g.Key, g => g.First());
        Engine.Assets.LevelElements.ClassField? ComponentOf(int offset) =>
            components.LastOrDefault(c => c.Offset <= offset && offset < c.Offset + c.Length);
        uint OwnerOf(int offset) =>
            ComponentOf(offset)?.Owner ?? registry?.Owner(cls, offset) ?? cls;
        // The instance a word names: read from the bytes when the region's references can be, so an edit shows at once.
        (string Kind, string Name, int Index)? ReferenceAt(int offset)
        {
            if (canWriteReferences) return refs!.Decode(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(settings.AsSpan(offset)));
            return staticReferences.TryGetValue(offset, out var r) ? (r.Kind, r.Name, r.Index) : null;
        }

        var rows = new List<SettingRow>();
        for (int offset = 4; offset + 4 <= settings.Length; offset += 4)
        {
            int covered = 0;
            foreach (var field in fieldsByWord[offset])
            {
                if (field.Offset + field.Length > settings.Length || field.Length is < 1 or > 4) continue;
                for (int b = 0; b < field.Length; b++) covered |= 1 << (field.Offset - offset + b);
                rows.Add(new SettingRow(field.Owner, field.Offset, field.Length, field, ComponentOf(field.Offset)));
            }
            if (covered == 0xF) continue;
            // What no field covers: shown when asked for, and always when it names an instance or was edited.
            bool edited = !settings.AsSpan(offset, 4).SequenceEqual(loaded.AsSpan(offset, 4));
            if (covered != 0)
            {
                bool rest = false;
                for (int b = 0; b < 4; b++)
                    if ((covered & (1 << b)) == 0 && (settings[offset + b] != 0 || settings[offset + b] != loaded[offset + b])) rest = true;
                if (!rest) continue;
            }
            if (!_showUndocumented && !edited && (covered != 0 || ReferenceAt(offset) == null)) continue;
            rows.Add(new SettingRow(OwnerOf(offset), offset, 4, null, ComponentOf(offset)));
        }

        // The folds: the instance's class first, then up its parents; anything else (no table, no EBOOT) last.
        var chain = Engine.Assets.LevelElements.ClassFields.Chain(game, cls);
        if (chain.Count <= 1 && registry != null) chain = registry.Chain(cls).ToList();
        var owners = rows.Select(r => r.Owner).Distinct()
            .OrderBy(o => chain.IndexOf(o) is var i && i >= 0 ? i : int.MaxValue).ThenBy(o => o).ToList();

        void Tooltip(SettingRow row, string type)
        {
            if (!ImGui.IsItemHovered()) return;
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            if (row.Field is { Description.Length: > 0 } field) ImGui.TextUnformatted(field.Description);
            if (row.RunTime) ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), LM.Get("GUI_Frame_InstanceInspector_FieldRunTimeHint"));
            ImGui.TextDisabled($"+0x{row.Offset:X2}  {type}");
            // Across the class: what every instance of it in the level holds in this word.
            if (_showUndocumented && survey.Count > 1 && survey.Words.TryGetValue(row.Offset & ~3, out var across))
            {
                ImGui.Separator();
                ImGui.TextDisabled(across.Distinct == 1
                    ? LM.Get("GUI_Frame_InstanceInspector_SettingsSameOnAll", survey.Count)
                    : LM.Get("GUI_Frame_InstanceInspector_SettingsValues", across.Distinct, survey.Count));
                ImGui.TextDisabled(across.Sample);
            }
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        void StartEdit(SettingRow row)
        {
            _fieldEdit2 = row.Field is { } f
                ? (f.Owner, f.Offset, f.Name, f.TypeText, f.RunTime, f.Description, true)
                : (row.Owner, row.Offset, "", ReferenceAt(row.Offset) is { } r ? r.Kind
                    : GuessType(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(loaded.AsSpan(row.Offset))) == SettingType.Float ? "float" : "u32",
                    false, "", false);
            _openFieldEdit = true;
        }

        void RowMenu(SettingRow row, bool edited)
        {
            if (!ImGui.BeginPopupContextItem("row_menu")) return;
            int at = row.Offset, length = row.Size;
            if (game != null && ImGui.MenuItem(LM.Get(row.Field != null ? "GUI_Frame_InstanceInspector_FieldEdit" : "GUI_Frame_InstanceInspector_FieldDocument")))
                StartEdit(row);
            if (ImGui.MenuItem(LM.Get("GUI_Frame_InstanceInspector_SettingsCopyToClass")))
            {
                var source = settings.AsSpan(at, length).ToArray();
                var targets = SettingsOfClass(cls, moby).ToList();
                Core.Undo.UndoManager.Singleton.Record(LM.Get("GUI_Undo_CopySetting"), targets, () =>
                {
                    foreach (var other in targets)
                        if (other.Length >= at + length) source.CopyTo(other.AsSpan(at));
                });
            }
            if (ImGui.MenuItem(LM.Get("GUI_Frame_InstanceInspector_SettingsRevert"), "", false, edited))
                Core.Undo.UndoManager.Singleton.Record(LM.Get("GUI_Undo_EditSetting"), [settings],
                    () => loaded.AsSpan(at, length).CopyTo(settings.AsSpan(at, length)));
            // A word nothing documents is read as it was guessed; this changes the guess.
            if (row.Field == null && ImGui.BeginMenu(LM.Get("GUI_Frame_InstanceInspector_SettingsReadAs")))
            {
                foreach (var option in Enum.GetValues<SettingType>())
                    if (ImGui.MenuItem(option.ToString(), "", SettingTypes.GetValueOrDefault((cls, at)) == option)) SettingTypes[(cls, at)] = option;
                ImGui.EndMenu();
            }
            ImGui.EndPopup();
        }

        void WriteWord(int offset, uint value) =>
            Core.Undo.UndoManager.Singleton.Record(LM.Get("GUI_Undo_EditSetting"), [settings],
                () => System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(settings.AsSpan(offset), value));

        // A reference: what it names (a click selects it), a list to pick another from, a cross to unset it, and a
        // drop target for an instance dragged from the hierarchy.
        void ReferenceEditor(SettingRow row, string? wanted, bool locked)
        {
            var named = ReferenceAt(row.Offset);
            uint word = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(settings.AsSpan(row.Offset));
            float buttons = canWriteReferences && !locked ? ImGui.GetFrameHeight() * 2f + ImGui.GetStyle().ItemInnerSpacing.X * 2f : 0f;
            ImGui.BeginGroup();
            if (named is { } target) ReferenceLink(region, target.Kind, target.Name, target.Index);
            else ImGui.TextDisabled(word == 0 ? LM.Get("GUI_Frame_InstanceInspector_ReferenceNone") : $"0x{word:X8}");
            ImGui.EndGroup();
            if (canWriteReferences && !locked && ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload(DragPayload);
                unsafe
                {
                    if (payload.Handle != null && DraggedEntity is { } dropped && RegionOf(dropped) == region
                        && ReferenceTo(dropped, wanted) is { } to
                        && (wanted == null || wanted == to.Kind) && refs!.Encode(to.Kind, to.Index) is { } encoded)
                        WriteWord(row.Offset, encoded);
                }
                ImGui.EndDragDropTarget();
            }
            if (!canWriteReferences || locked) return;
            ImGui.SameLine();
            float room = ImGui.GetContentRegionAvail().X - buttons;
            if (room > 0f) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + room);
            if (ImGui.ArrowButton("##pick", ImGuiDir.Down)) { _referenceFilter = ""; ImGui.OpenPopup("##references"); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_ReferencePick", wanted ?? LM.Get("GUI_Frame_InstanceInspector_ReferenceAny")));
            ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.BeginDisabled(word == 0);
            if (ImGui.Button("x##unset", new Vector2(ImGui.GetFrameHeight(), 0f))) WriteWord(row.Offset, 0);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_ReferenceUnset"));
            if (ImGui.BeginPopup("##references"))
            {
                ImGui.SetNextItemWidth(260f);
                if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
                ImGui.InputTextWithHint("##filter", LM.Get("GUI_Frame_InstanceInspector_ReferenceFilter"), ref _referenceFilter, 64);
                if (ImGui.BeginChild("##list", new Vector2(320f, 260f)))
                {
                    int shown = 0;
                    foreach (string kind in wanted != null ? [wanted] : Engine.Assets.LevelElements.ClassFields.ReferenceKinds)
                    {
                        int instance = -1;
                        foreach (string name in refs!.Names(kind))
                        {
                            instance++;
                            if (name.Length == 0 || (_referenceFilter.Length > 0 && !name.Contains(_referenceFilter, StringComparison.OrdinalIgnoreCase))) continue;
                            if (++shown > 300) break;
                            bool current = named is { } n && n.Kind == kind && n.Index == instance;
                            if (ImGui.Selectable(wanted != null ? $"{name}##{kind}{shown}" : $"{kind}  {name}##{shown}", current)
                                && refs.Encode(kind, instance) is { } encoded)
                            {
                                WriteWord(row.Offset, encoded);
                                ImGui.CloseCurrentPopup();
                            }
                        }
                    }
                    if (shown > 300) ImGui.TextDisabled(LM.Get("GUI_Frame_InstanceInspector_ReferenceMore"));
                }
                ImGui.EndChild();
                ImGui.EndPopup();
            }
        }

        void Row(SettingRow row)
        {
            var bytes = settings.AsSpan(row.Offset, row.Size);
            bool edited = !bytes.SequenceEqual(loaded.AsSpan(row.Offset, row.Size));
            ImGui.PushID(row.Offset);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (_editFields && game != null)
            {
                if (ImGui.SmallButton(row.Field != null ? "..." : "+")) StartEdit(row);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get(row.Field != null ? "GUI_Frame_InstanceInspector_FieldEdit" : "GUI_Frame_InstanceInspector_FieldDocument"));
                ImGui.SameLine();
            }
            string name = row.Field is { Name.Length: > 0 } documented ? documented.Name : $"+0x{row.Offset:X2}";
            if (edited) ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), name);
            else if (row.RunTime || row.Field == null) ImGui.TextDisabled(name);
            else ImGui.TextUnformatted(name);

            if (row.Field is { } field)
            {
                Tooltip(row, field.RunTime ? LM.Get("GUI_Frame_InstanceInspector_FieldRunTime", field.TypeText) : field.TypeText);
                RowMenu(row, edited);
                ImGui.TableNextColumn();
                if (field.Kind == Engine.Assets.LevelElements.ClassFieldKind.Reference)
                {
                    ReferenceEditor(row, field.TypeName, field.RunTime);
                    ImGui.PopID();
                    return;
                }
                ImGui.BeginDisabled(field.RunTime);
                ImGui.SetNextItemWidth(-1f);
                bool tracked = true;
                switch (field.Kind)
                {
                    case Engine.Assets.LevelElements.ClassFieldKind.F32:
                    {
                        float f = System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(bytes);
                        if (ImGui.DragFloat("##v", ref f, MathF.Max(0.01f, MathF.Abs(f) * 0.01f), 0f, 0f, "%.6g"))
                            System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes, f);
                        break;
                    }
                    case Engine.Assets.LevelElements.ClassFieldKind.Bool:
                    {
                        // A tick box over one byte or four: unticking clears it, ticking writes 1.
                        bool on = bytes.IndexOfAnyExcept((byte)0) >= 0;
                        if (ImGui.Checkbox("##v", ref on))
                        {
                            bytes.Clear();
                            if (on) bytes[^1] = 1;
                        }
                        break;
                    }
                    case Engine.Assets.LevelElements.ClassFieldKind.Enum:
                    {
                        long value = row.Size switch { 1 => bytes[0], 2 => System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes), _ => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes) };
                        void Store(long v)
                        {
                            var target = settings.AsSpan(row.Offset, row.Size);
                            if (row.Size == 1) target[0] = (byte)v;
                            else if (row.Size == 2) System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(target, (ushort)v);
                            else System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(target, (uint)v);
                        }
                        var list = Engine.Assets.LevelElements.ClassFields.Enum(game, field.TypeName);
                        string preview = list == null ? value.ToString()
                            : list.Flags ? (value == 0 ? "-" : string.Join(" | ", list.Entries.Where(e => e.Value != 0 && (value & e.Value) == e.Value).Select(e => e.Name).DefaultIfEmpty($"0x{value:X}")))
                            : list.Entries.FirstOrDefault(e => e.Value == value) is { Name: { } current } ? current : $"{value} ?";
                        if (ImGui.BeginCombo("##v", preview))
                        {
                            if (list != null)
                                foreach (var (entry, entryValue) in list.Entries)
                                {
                                    if (list.Flags)
                                    {
                                        bool set = entryValue != 0 && (value & entryValue) == entryValue;
                                        if (ImGui.Checkbox($"{entry}##{entryValue}", ref set)) { value = set ? value | entryValue : value & ~entryValue; Store(value); }
                                    }
                                    else if (ImGui.Selectable($"{entry}  ({entryValue})", entryValue == value)) Store(entryValue);
                                }
                            // The number itself, for a value the list does not have yet.
                            int raw = (int)value;
                            ImGui.SetNextItemWidth(120f);
                            if (ImGui.InputInt(LM.Get("GUI_Frame_InstanceInspector_EnumRaw"), ref raw)) Store(raw);
                            ImGui.EndCombo();
                        }
                        break;
                    }
                    case Engine.Assets.LevelElements.ClassFieldKind.U8:
                    {
                        int v = bytes[0];
                        if (ImGui.DragInt("##v", ref v, 0.1f, 0, 255)) bytes[0] = (byte)Math.Clamp(v, 0, 255);
                        break;
                    }
                    case Engine.Assets.LevelElements.ClassFieldKind.U16:
                    {
                        int v = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes);
                        if (ImGui.DragInt("##v", ref v, 0.2f, 0, 0xFFFF))
                            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)Math.Clamp(v, 0, 0xFFFF));
                        break;
                    }
                    case Engine.Assets.LevelElements.ClassFieldKind.TextId:
                    {
                        // The number, then what it says in the game's first language; a click opens Text Tables on it.
                        int v = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes);
                        ImGui.SetNextItemWidth(64f);
                        if (ImGui.DragInt("##v", ref v, 0.2f, 0, 0xFFFFFF)) System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, v);
                        if (!field.RunTime) TrackField(settingsBefore, "GUI_Undo_EditSetting");
                        tracked = false;
                        string? folder = string.IsNullOrEmpty(Program.ProvidedPath) ? null : Engine.Config.GameConfigs.GameFolderFor(Program.ProvidedPath);
                        if (v != 0 && folder != null && Engine.Text.GameText.Reference(folder) is { } text)
                        {
                            ImGui.SameLine();
                            string? said = v > 0 && v < text.Entries.Count ? text.Entries[v].Text : null;
                            ImGui.EndDisabled();
                            if (said == null) ImGui.TextDisabled(LM.Get("GUI_Frame_InstanceInspector_FieldTextNone"));
                            else
                            {
                                if (ImGui.Selectable(Engine.Text.TextTable.Escape(said) + "##said"))
                                {
                                    var frame = LunaWindow.Instance.IsAnyFrameOpened<TextTablesFrame>() ? LunaWindow.Instance.GetFirstFrame<TextTablesFrame>() : null;
                                    if (frame == null) { frame = new TextTablesFrame(); LunaWindow.Instance.AddFrame(frame); }
                                    frame.Reveal(v);
                                }
                                if (ImGui.IsItemHovered())
                                    ImGui.SetTooltip((Engine.Text.GameText.TagName(game, v) is { } tag ? tag + "\n" : "") + Engine.Text.TextTable.Escape(said) + "\n" + LM.Get("GUI_Frame_InstanceInspector_FieldTextOpen"));
                            }
                            ImGui.BeginDisabled(field.RunTime);
                        }
                        break;
                    }
                    default:
                    {
                        int v = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes);
                        if (ImGui.DragInt("##v", ref v, 0.2f)) System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, v);
                        break;
                    }
                }
                if (!field.RunTime && tracked) TrackField(settingsBefore, "GUI_Undo_EditSetting");
                ImGui.EndDisabled();
            }
            else if (ReferenceAt(row.Offset) != null)
            {
                // Nothing documents the word, but it names an instance.
                Tooltip(row, LM.Get("GUI_Frame_InstanceInspector_FieldReference"));
                RowMenu(row, edited);
                ImGui.TableNextColumn();
                ReferenceEditor(row, null, false);
            }
            else
            {
                // How the word is read: as last chosen for this class and offset (right-click), else guessed once from
                // the value it was loaded with and then kept, so an edit does not change the editor under the cursor.
                if (!SettingTypes.TryGetValue((cls, row.Offset), out var type))
                    SettingTypes[(cls, row.Offset)] = type = GuessType(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(loaded.AsSpan(row.Offset)));
                Tooltip(row, type.ToString());
                RowMenu(row, edited);
                ImGui.TableNextColumn();
                uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes);
                switch (type)
                {
                    case SettingType.Float:
                    {
                        float f = BitConverter.UInt32BitsToSingle(value);
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.DragFloat("##v", ref f, MathF.Max(0.01f, MathF.Abs(f) * 0.01f), 0f, 0f, "%.6g"))
                            System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes, f);
                        TrackField(settingsBefore, "GUI_Undo_EditSetting");
                        break;
                    }
                    case SettingType.Int:
                    {
                        int n = (int)value;
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.DragInt("##v", ref n, 0.2f)) System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, n);
                        TrackField(settingsBefore, "GUI_Undo_EditSetting");
                        break;
                    }
                    case SettingType.Hex:
                    {
                        string hex = value.ToString("X8");
                        ImGui.SetNextItemWidth(-1f);
                        // The size counts the terminating zero: 8 digits need 9 (8 made ImGui assert on a full buffer).
                        if (ImGui.InputText("##v", ref hex, 9, ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue)
                            && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
                            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, parsed);
                        TrackField(settingsBefore, "GUI_Undo_EditSetting");
                        break;
                    }
                    default:
                    {
                        // Four bytes, each its own field: most flags are bytes of 0 or 1 (01 00 00 00).
                        float width = (ImGui.GetContentRegionAvail().X - 3 * ImGui.GetStyle().ItemInnerSpacing.X) / 4f;
                        for (int b = 0; b < 4; b++)
                        {
                            if (b > 0) ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
                            int v = bytes[b];
                            ImGui.SetNextItemWidth(width);
                            if (ImGui.DragInt($"##b{b}", ref v, 0.1f, 0, 255)) bytes[b] = (byte)v;
                            TrackField(settingsBefore, "GUI_Undo_EditSetting");
                        }
                        break;
                    }
                }
            }
            ImGui.PopID();
        }

        // A fold inside a class's table: an embedded object, or the run-time state.
        void SubFold(string label, string? hint, IEnumerable<SettingRow> group)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            bool open = ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.SpanAllColumns);
            if (hint is { Length: > 0 } && ImGui.IsItemHovered()) ImGui.SetTooltip(hint);
            if (!open) return;
            foreach (var row in group) Row(row);
            ImGui.TreePop();
        }

        foreach (uint owner in owners)
        {
            var own = rows.Where(r => r.Owner == owner).ToList();
            string title = Engine.Assets.LevelElements.ClassFields.ClassName(game, owner) is { } known ? known
                : registry != null ? ClassLabel(registry, owner) : $"0x{owner:X}";
            bool open = ImGui.CollapsingHeader($"{title}  ({own.Count(r => !r.RunTime)})###class{owner}", owner == cls ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_ClassFoldHint", $"0x{owner:X}",
                    registry != null && registry.Sizes.TryGetValue(owner, out int size) ? $"0x{size:X}" : "?"));
            // Edit fields: the class's own name is part of the table too.
            if (_editFields && game is { } renameIn && ImGui.BeginPopupContextItem($"##rename{owner}"))
            {
                if (ImGui.IsWindowAppearing()) _className = Engine.Assets.LevelElements.ClassFields.ClassName(game, owner) ?? "";
                ImGui.SetNextItemWidth(240f);
                ImGui.InputText(LM.Get("GUI_Frame_InstanceInspector_ClassName"), ref _className, 64);
                if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_FieldSave")))
                {
                    int? parent = chain.IndexOf(owner) is var at && at >= 0 && at + 1 < chain.Count ? (int)chain[at + 1] : null;
                    Engine.Assets.LevelElements.ClassFields.SetClassName(renameIn, owner, _className, parent);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            if (!open) continue;
            if (!ImGui.BeginTable($"##settings{owner}", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings)) continue;
            ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthStretch, 0.45f);
            ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch, 0.55f);
            foreach (var row in own.Where(r => r.Component == null && !r.RunTime)) Row(row);
            foreach (var component in own.Where(r => r.Component != null).Select(r => r.Component!).Distinct())
            {
                var inside = own.Where(r => r.Component == component).ToList();
                SubFold($"{(component.Name.Length > 0 ? component.Name : $"+0x{component.Offset:X2}")}  ({inside.Count(r => !r.RunTime)})###component{component.Offset}",
                    component.Description, inside);
            }
            var runTime = own.Where(r => r.Component == null && r.RunTime).ToList();
            if (runTime.Count > 0)
                SubFold($"{LM.Get("GUI_Frame_InstanceInspector_RunTimeGroup", runTime.Count)}###runtime", LM.Get("GUI_Frame_InstanceInspector_FieldRunTimeHint"), runTime);
            ImGui.EndTable();
        }

        if (game is { } id) RenderFieldEditors(id, cls, chain);
    }

    /// <summary>The two editors of "Edit fields": one field's name, type and description, and the game's enums.</summary>
    private static void RenderFieldEditors(Engine.Games.GameId game, uint cls, List<uint> chain)
    {
        if (_openFieldEdit) { ImGui.OpenPopup("##fieldedit"); _openFieldEdit = false; }
        if (ImGui.BeginPopup("##fieldedit") && _fieldEdit2 is { } edit)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_InstanceInspector_FieldEditTitle", $"0x{edit.Offset:X}",
                Engine.Assets.LevelElements.ClassFields.ClassName(game, edit.Owner) ?? $"0x{edit.Owner:X}"));
            string name = edit.Name, description = edit.Description, type = edit.Type;
            bool runTime = edit.RunTime;
            int offset = edit.Offset;
            ImGui.SetNextItemWidth(320f);
            ImGui.InputText(LM.Get("GUI_Frame_InstanceInspector_FieldName"), ref name, 64);
            ImGui.SetNextItemWidth(320f);
            if (ImGui.BeginCombo(LM.Get("GUI_Frame_InstanceInspector_FieldType"), type))
            {
                foreach (string option in new[] { "u8", "u16", "u32", "s32", "float", "bool", "bool32", "text", "ref" })
                    if (ImGui.Selectable(option, option == type)) type = option;
                ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_FieldTypeReferences"));
                foreach (string option in Engine.Assets.LevelElements.ClassFields.ReferenceKinds)
                    if (ImGui.Selectable(option, option == type)) type = option;
                var enums = Engine.Assets.LevelElements.ClassFields.Enums(game);
                if (enums.Count > 0) ImGui.SeparatorText(LM.Get("GUI_Frame_InstanceInspector_SettingsEnums"));
                foreach (var e in enums)
                    if (ImGui.Selectable($"{e.Name}{(e.Flags ? "  (flags)" : "")}", e.Name == type)) type = e.Name;
                ImGui.EndCombo();
            }
            if (!edit.Existing)
            {
                ImGui.SetNextItemWidth(120f);
                ImGui.InputInt(LM.Get("GUI_Frame_InstanceInspector_FieldOffset"), ref offset, 1, 4, ImGuiInputTextFlags.CharsHexadecimal);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_FieldOffsetHint"));
            }
            ImGui.Checkbox(LM.Get("GUI_Frame_InstanceInspector_FieldRunTimeBox"), ref runTime);
            ImGui.InputTextMultiline(LM.Get("GUI_Frame_InstanceInspector_FieldDescription"), ref description, 512, new Vector2(320f, ImGui.GetTextLineHeight() * 4f));
            _fieldEdit2 = (edit.Owner, Math.Max(4, offset), name, type, runTime, description, edit.Existing);

            var parsed = Engine.Assets.LevelElements.ClassFields.TypeOf(type,
                Engine.Assets.LevelElements.ClassFields.Enums(game).ToDictionary(e => e.Name));
            ImGui.BeginDisabled(parsed == null);
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_FieldSave")) && parsed is { } t)
            {
                int? parent = chain.IndexOf(edit.Owner) is var at && at >= 0 && at + 1 < chain.Count ? (int)chain[at + 1] : null;
                Engine.Assets.LevelElements.ClassFields.SetField(game,
                    new Engine.Assets.LevelElements.ClassField(edit.Owner, Math.Max(4, offset), t.Length, t.Kind, runTime, name.Trim(), description.Trim(), t.TypeName), parent);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            if (edit.Existing)
            {
                ImGui.SameLine();
                if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_FieldRemove")))
                {
                    Engine.Assets.LevelElements.ClassFields.RemoveField(game, edit.Owner, edit.Offset);
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_FieldCancel"))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (_openEnums) { ImGui.OpenPopup("##enums"); _openEnums = false; _enumEdit = null; }
        if (!ImGui.BeginPopup("##enums")) return;
        var all = Engine.Assets.LevelElements.ClassFields.Enums(game);
        if (ImGui.BeginChild("##enumlist", new Vector2(180f, 300f), ImGuiChildFlags.Borders))
        {
            foreach (var e in all)
                if (ImGui.Selectable($"{e.Name}{(e.Flags ? "  (flags)" : "")}", _enumEditOriginal == e.Name))
                {
                    _enumEdit = e.Clone();
                    _enumEditOriginal = e.Name;
                }
            if (ImGui.Selectable(LM.Get("GUI_Frame_InstanceInspector_EnumNew")))
            {
                _enumEdit = new Engine.Assets.LevelElements.ClassEnum { Name = "NewEnum" };
                _enumEditOriginal = null;
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##enumedit", new Vector2(360f, 300f)))
        {
            if (_enumEdit is not { } working) ImGui.TextDisabled(LM.Get("GUI_Frame_InstanceInspector_EnumPick"));
            else
            {
                string enumName = working.Name;
                ImGui.SetNextItemWidth(200f);
                if (ImGui.InputText(LM.Get("GUI_Frame_InstanceInspector_FieldName"), ref enumName, 48))
                    working.Name = new string(enumName.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
                bool flags = working.Flags;
                if (ImGui.Checkbox(LM.Get("GUI_Frame_InstanceInspector_EnumFlags"), ref flags)) working.Flags = flags;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80f);
                if (ImGui.BeginCombo(LM.Get("GUI_Frame_InstanceInspector_EnumSize"), working.Size.ToString()))
                {
                    foreach (int size in new[] { 1, 2, 4 })
                        if (ImGui.Selectable(size.ToString(), working.Size == size)) working.Size = size;
                    ImGui.EndCombo();
                }
                if (ImGui.BeginTable("##entries", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0f, 180f)))
                {
                    ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthFixed, 90f);
                    ImGui.TableSetupColumn("x", ImGuiTableColumnFlags.WidthFixed, 24f);
                    int remove = -1;
                    for (int i = 0; i < working.Entries.Count; i++)
                    {
                        ImGui.PushID(i);
                        var (entryName, entryValue) = working.Entries[i];
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.InputText("##n", ref entryName, 48))
                            working.Entries[i] = (new string(entryName.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray()), entryValue);
                        ImGui.TableNextColumn();
                        int number = (int)entryValue;
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.InputInt("##v", ref number, 0, 0, working.Flags ? ImGuiInputTextFlags.CharsHexadecimal : ImGuiInputTextFlags.None))
                            working.Entries[i] = (working.Entries[i].Name, number);
                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton("x")) remove = i;
                        ImGui.PopID();
                    }
                    if (remove >= 0) working.Entries.RemoveAt(remove);
                    ImGui.EndTable();
                }
                if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_EnumAddEntry")))
                {
                    long next = working.Entries.Count == 0 ? (working.Flags ? 1 : 0)
                        : working.Flags ? working.Entries.Max(e => e.Value) * 2 : working.Entries.Max(e => e.Value) + 1;
                    working.Entries.Add(($"Value{working.Entries.Count}", next));
                }
                ImGui.SameLine();
                ImGui.BeginDisabled(working.Name.Length == 0 || working.Entries.Any(e => e.Name.Length == 0));
                if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_FieldSave")))
                {
                    Engine.Assets.LevelElements.ClassFields.SetEnum(game, working.Clone(), _enumEditOriginal);
                    _enumEditOriginal = working.Name;
                }
                ImGui.EndDisabled();
                if (_enumEditOriginal != null)
                {
                    ImGui.SameLine();
                    if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_FieldRemove")))
                    {
                        Engine.Assets.LevelElements.ClassFields.RemoveEnum(game, _enumEditOriginal);
                        _enumEdit = null;
                        _enumEditOriginal = null;
                    }
                }
            }
        }
        ImGui.EndChild();
        ImGui.TextDisabled(Engine.Assets.LevelElements.ClassFields.FilePath(game));
        ImGui.EndPopup();
    }

    /// <summary>The undo step a field being edited is building: opened when the field is clicked into, from the
    /// state at the start of that frame, committed when it is left.</summary>
    private static Core.Undo.UndoManager.Session? _fieldEdit;

    private static void TrackField(Lazy<Core.Undo.UndoManager.Session> beforeThisFrame, string label)
    {
        if (ImGui.IsItemActivated()) _fieldEdit = beforeThisFrame.Value;
        if (ImGui.IsItemDeactivated() && _fieldEdit != null)
        {
            _fieldEdit.Commit(LM.Get(label));
            _fieldEdit = null;
        }
    }

    /// <summary>A class as the inspector names it: its number, and a name when one is known - the game's own class
    /// table, else All 4 One's for the same number (marked: numbers mostly, not always, mean the same class).</summary>
    private static string ClassLabel(Engine.Loading.Readers.ClassRegistry registry, uint cls)
    {
        var game = Core.LunaWindow.Instance.Level?.GameDefinition?.Id;
        string? name = GameClassNames.Name(game, cls);
        if (name == null && GameClassNames.Name(Engine.Games.GameId.All4One, cls) is { } borrowed) name = borrowed + "?";
        string size = registry.Sizes.TryGetValue(cls, out int bytes) ? $" {bytes}B" : "";
        return name != null ? $"0x{cls:X} {name}{size}" : $"0x{cls:X}{size}";
    }

    private enum SettingType { Bytes, Float, Int, Hex }

    private static SettingType GuessType(uint value)
    {
        float f = BitConverter.UInt32BitsToSingle(value);
        if (value == 0 || (value & 0xFEFEFEFE) == 0) return SettingType.Bytes;
        if (float.IsFinite(f) && MathF.Abs(f) is > 1e-6f and < 1e7f) return SettingType.Float;
        return value < 0x10000 ? SettingType.Int : SettingType.Hex;
    }

    /// <summary>How each settings word is shown, as last chosen, by class and offset.</summary>
    private static readonly Dictionary<(uint Class, int Offset), SettingType> SettingTypes = [];
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], byte[]> LoadedSettings = new();
    private static bool _showUndocumented, _editFields;

    /// <summary>The settings of every Clue and camera of a class in the level.</summary>
    private static IEnumerable<byte[]> SettingsOfClass(uint cls)
        => SettingsOfClass(cls, moby: false);

    /// <summary>The settings of every instance of a class in the level: Mobys, or Clues and cameras (whose class
    /// numbers are another family).</summary>
    private static IEnumerable<byte[]> SettingsOfClass(uint cls, bool moby)
    {
        foreach (var region in EntityManager.Singleton.Regions)
        {
            if (moby)
            {
                foreach (var m in region.MobyInstances.Entities.OfType<EntityMoby>())
                    if (m.Settings.Length >= 4 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(m.Settings) == cls)
                        yield return m.Settings;
                continue;
            }
            foreach (var volume in region.Volumes.Entities.OfType<EntityVolume>())
                if (volume.BaseVolume.Clue is { } clue && clue.Class == cls) yield return clue.Settings;
            foreach (var camera in region.Cameras.Entities.OfType<EntityCamera>())
                if (camera.Controller.Class == cls) yield return camera.Controller.Settings;
        }
    }

    private sealed record ClassSurvey(int Count, Dictionary<int, (int Distinct, string Sample)> Words);
    private static readonly Dictionary<(uint, bool), ClassSurvey> Surveys = [];
    private static int _surveyFrame = -1;

    /// <summary>
    /// What every instance of a class in the level holds at each word of its settings: how many distinct values,
    /// and the commonest. A word the same on all of them is a class default; one that varies is where instances
    /// differ - the fields worth trying first. Recomputed each second, so edits show.
    /// </summary>
    private static ClassSurvey Survey(uint cls, bool moby)
    {
        int frame = (int)(ImGui.GetTime());
        if (frame != _surveyFrame) { Surveys.Clear(); _surveyFrame = frame; }
        if (Surveys.TryGetValue((cls, moby), out var cached)) return cached;
        var all = SettingsOfClass(cls, moby).ToList();
        var words = new Dictionary<int, (int, string)>();
        int length = all.Count > 0 ? all.Min(a => a.Length) : 0;
        for (int o = 4; o + 4 <= length; o += 4)
        {
            var values = all.GroupBy(a => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(a.AsSpan(o)))
                .OrderByDescending(g => g.Count()).ToList();
            string sample = string.Join("\n", values.Take(6).Select(g =>
            {
                float f = BitConverter.UInt32BitsToSingle(g.Key);
                string asFloat = float.IsFinite(f) && MathF.Abs(f) is > 1e-4f and < 1e7f ? $"  ({f:G5})" : "";
                return $"{g.Key:X8}{asFloat}  x{g.Count()}";
            })) + (values.Count > 6 ? $"\n... {values.Count - 6} more" : "");
            words[o] = (values.Count, sample);
        }
        return Surveys[(cls, moby)] = new ClassSurvey(all.Count, words);
    }

    /// <summary>A reference shown as a link: a click selects the instance it names (its volume, for a Clue), and
    /// the 3D view's selection follows. The instance is found by its place in the region's table, not its name,
    /// which several can share.</summary>
    private static void ReferenceLink(EntityRegion? region, string kind, string name, int index)
    {
        var target = FindInstance(region, kind, index);
        string label = $"{kind} {name}";
        if (target == null) { ImGui.TextUnformatted(label); return; }
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.75f, 1f, 1f));
        bool clicked = ImGui.Selectable($"{label}##ref_{kind}_{index}", false, ImGuiSelectableFlags.None, ImGui.CalcTextSize(label));
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_ReferenceSelect")
                + (ReferenceIdText(region, kind, index) is { } id ? "\n" + LM.Get("GUI_Frame_InstanceInspector_ReferenceId", id) : ""));
        if (clicked) SelectionManager.Singleton.Select(target);
    }

    /// <summary>An instance's reference ID as a settings field holds it, spelled out: the word, then the kind, the
    /// editor layer and the place in that layer the word is made of. Null where the game's references are not
    /// such words (All 4 One, Into the Nexus).</summary>
    private static string? ReferenceIdText(EntityRegion? region, string kind, int index)
    {
        if (region?.References.Encode(kind, index) is not { } word) return null;
        int layer = (int)(word >> 16) & 0x3FF;
        string layerName = layer < region.Layers.Count ? $" {region.Layers[layer]}" : "";
        return $"0x{word:X8}  ({kind}, layer {layer}{layerName}, #{word & 0xFFF})";
    }

    /// <summary>The selected instance's reference ID - what a field naming it holds - a click copying the word. A
    /// volume with a Clue has two, one for each.</summary>
    private static void RenderReferenceIds(Entity entity)
    {
        var region = RegionOf(entity);
        if (region is not { References.Editable: true }) return;
        void Line(string kind, int index)
        {
            if (ReferenceIdText(region, kind, index) is not { } id) return;
            ImGui.TextUnformatted(LM.Get("GUI_Frame_InstanceInspector_ReferenceId", id));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_ReferenceIdHint"));
            if (ImGui.IsItemClicked()) ImGui.SetClipboardText(id[..10]);
        }
        if (ReferenceTo(entity, null) is { } self) Line(self.Kind, self.Index);
        if (entity is EntityVolume { BaseVolume.Clue: { } clue }) Line("Clue", clue.Index);
    }

    private static Entity? FindInstance(EntityRegion? region, string kind, int index)
    {
        if (region == null || index < 0) return null;
        return kind switch
        {
            "Volume" => region.Volumes.Entities.FirstOrDefault(e => e is EntityVolume v && v.BaseVolume.Index == index),
            "Clue" => region.Volumes.Entities.FirstOrDefault(e => e is EntityVolume v && v.BaseVolume.Clue?.Index == index),
            "Path" => region.Paths.Entities.FirstOrDefault(e => e is EntityPath p && p.Path.Index == index),
            "Controller" => region.Cameras.Entities.FirstOrDefault(e => e is EntityCamera c && c.Controller.Index == index),
            "Moby" => region.MobyInstances.Entities.FirstOrDefault(e => e is EntityMoby m && m.GameplayIndex == index),
            _ => null,
        };
    }

    private EntityVolume? _usedByVolume;
    private EntityRegion? _usedByRegion;
    private List<(string Kind, string User, int Index, string Field)> _usedBy = [];

    /// <summary>What names this volume in its settings (a moon's area, a camera's look-at volume...),
    /// each a link to it.</summary>
    private void RenderUsedBy(EntityVolume volume)
    {
        int index = volume.BaseVolume.Index;
        if (index < 0) return;
        if (!ReferenceEquals(volume, _usedByVolume))
        {
            _usedByVolume = volume;
            var game = Core.LunaWindow.Instance.Level?.GameDefinition?.Id;
            _usedBy = [];
            // Only its own region's instances can name it: a reference is a place in that region's tables.
            _usedByRegion = RegionOf(volume);
            if (_usedByRegion is { } region)
            {
                foreach (var other in region.Volumes.Entities.OfType<EntityVolume>())
                    if (other.BaseVolume.Clue is { } clue)
                        foreach (var reference in clue.References)
                            if (reference.Kind == "Volume" && reference.Index == index)
                                _usedBy.Add(("Clue", clue.Name, clue.Index, $"{clue.ClassName} +0x{reference.Offset:X}" +
                                    (ClueClasses.FieldName(game, clue.Class, reference.Offset) is { } field ? $" {field}" : "")));
                foreach (var camera in region.Cameras.Entities.OfType<EntityCamera>())
                    foreach (var reference in camera.Controller.References)
                        if (reference.Kind == "Volume" && reference.Index == index)
                            _usedBy.Add(("Controller", camera.Controller.Name, camera.Controller.Index, $"{camera.Controller.ClassName ?? $"0x{camera.Controller.Class:X}"} +0x{reference.Offset:X}"));
                foreach (var moby in region.MobyInstances.Entities.OfType<EntityMoby>())
                    foreach (var reference in moby.References)
                        if (reference.Kind == "Volume" && reference.Index == index)
                            _usedBy.Add(("Moby", moby.Name, moby.GameplayIndex, $"{moby.BaseMoby.Name?.Split('/')[^1]} +0x{reference.Offset:X}"));
            }
        }
        if (_usedBy.Count == 0) return;
        ImGui.TextUnformatted(LM.Get("GUI_Frame_InstanceInspector_UsedBy"));
        foreach (var (kind, user, userIndex, field) in _usedBy)
        {
            ImGui.Bullet();
            ReferenceLink(_usedByRegion, kind, user, userIndex);
            ImGui.SameLine();
            ImGui.TextDisabled(field);
        }
    }

    /// <summary>A selected key, handle or point of a path: where it is along the path and, for a spline key,
    /// its handle mode and the key insertion / removal. The selection follows an edit that renumbers the keys.</summary>
    private void RenderPathPart(EntityPathPoint part)
    {
        var path = part.Owner;
        var distances = path.Distances();
        if (part.Part == PathPart.Point)
        {
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_PathPoint", part.Index, path.PointCount, path.Name));
            ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_PathPointDistance", distances[part.Index].ToString("0.##"), distances[^1].ToString("0.##")));
            return;
        }

        ImGui.Text(LM.Get(part.Part == PathPart.Key ? "GUI_Frame_InstanceInspector_PathKey" : "GUI_Frame_InstanceInspector_PathHandle",
            part.Index, path.KeyCount, path.Name));
        int sample = part.Index * EntityPath.SamplesPerSpan;
        ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_PathPointDistance", distances[sample].ToString("0.##"), distances[^1].ToString("0.##")));

        string[] modes = [LM.Get("GUI_PathHandle_Smooth"), LM.Get("GUI_PathHandle_Aligned"), LM.Get("GUI_PathHandle_Free")];
        int mode = (int)path.ModeAt(part.Index);
        ImGui.SetNextItemWidth(160f);
        if (ImGui.Combo(LM.Get("GUI_Frame_InstanceInspector_PathHandles"), ref mode, modes, modes.Length))
        {
            Core.Undo.UndoManager.Singleton.Record(LM.Get("GUI_Undo_PathEdit"), [path], () =>
            {
                path.SetMode(part.Index, (PathHandleMode)mode);
                // Applies the mode at once: the outgoing handle (or the incoming one, on the last key) drives.
                if (path.OutHandleAt(part.Index) is { } outHandle) path.SetHandle(part.Index, true, outHandle);
                else if (path.InHandleAt(part.Index) is { } inHandle) path.SetHandle(part.Index, false, inHandle);
            });
            part.Refresh();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_PathHandlesHint"));

        if (part.Index < path.KeyCount - 1 && ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_PathInsertKey")))
        {
            Core.Undo.UndoManager.Singleton.Record(LM.Get("GUI_Undo_PathAdd"), [path], () => path.InsertKey(part.Index));
            SelectionManager.Singleton.Select(new EntityPathPoint(path, part.Index + 1, PathPart.Key));
            return;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_PathInsertKeyHint"));
        if (part.Index > 0 && part.Index < path.KeyCount - 1)
        {
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_PathRemoveKey")))
            {
                Core.Undo.UndoManager.Singleton.Record(LM.Get("GUI_Undo_PathRemove"), [path], () => path.RemoveKey(part.Index));
                SelectionManager.Singleton.Select(path);
                return;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_PathRemoveKeyHint"));
        }
    }
}
