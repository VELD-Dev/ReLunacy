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
                RenderSettings(moby.Settings, moby.References, mobyClass, moby: true);
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
                var linked = clueSettings.References.Select(r => FindInstance(r.Kind, r.Name)).OfType<Entity>().Distinct().ToList();
                if (linked.Count > 0 && ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_SelectWithReferences", linked.Count)))
                    SelectionManager.Singleton.Set([.. linked, volumeEntity], volumeEntity);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_SelectWithReferencesHint"));
                RenderSettings(clueSettings.Settings, clueSettings.References, clueSettings.Class);
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
                ImGui.TextDisabled(LM.Get(authored ? "GUI_Frame_InstanceInspector_CameraFov" : "GUI_Frame_InstanceInspector_CameraFovDefault", fov.ToString("0.#")));
            }
            ImGui.TextWrapped(LM.Get("GUI_Frame_InstanceInspector_CameraNote"));
            if (controller.Settings.Length > 4)
                RenderSettings(controller.Settings, controller.References, controller.Class);
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

    /// <summary>One line of the settings: a known field, a reference, or a word nothing names.</summary>
    private sealed record SettingRow(uint Owner, int Offset, int Size, Engine.Assets.LevelElements.ClassField? Field,
        Engine.Assets.LevelElements.ClueReference? Reference, Engine.Assets.LevelElements.ClassField? Component)
    {
        public bool RunTime => Field is { RunTime: true };
    }

    /// <summary>
    /// A Clue's, a controller's or a Moby's settings, folded by the class that declares each field: the instance's
    /// own class open, the classes it inherits from below it, closed, each with the count of what it shows. Two
    /// columns, name and value; the offset, the type, the description and what the other instances of the class
    /// hold are in the name's tooltip. An embedded object and the fields the game overwrites at start-up (read-only)
    /// are folds of their own inside their class. Fields at zero stay hidden until "Show unset" is ticked.
    /// </summary>
    private static void RenderSettings(byte[] settings, IReadOnlyList<Engine.Assets.LevelElements.ClueReference> fieldReferences, uint cls, bool moby = false)
    {
        var game = Core.LunaWindow.Instance.Level?.GameDefinition?.Id;
        var registry = Engine.Loading.Readers.ClassRegistry.Find(game);
        if (registry != null && registry.Sizes.TryGetValue(cls, out int classSize) && classSize != settings.Length)
            ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), LM.Get("GUI_Frame_InstanceInspector_ClassSizeMismatch", settings.Length, classSize));

        // What the settings held when first shown, so an edit stands out and can be undone.
        if (!LoadedSettings.TryGetValue(settings, out var loaded))
            LoadedSettings.Add(settings, loaded = (byte[])settings.Clone());
        var settingsBefore = new Lazy<Core.Undo.UndoManager.Session>(() => Core.Undo.UndoManager.Singleton.Begin([settings]));
        if (!ImGui.IsAnyItemActive()) _ = settingsBefore.Value;
        ImGui.Checkbox(LM.Get("GUI_Frame_InstanceInspector_SettingsShowUnset"), ref _showZeroWords);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_SettingsHint"));
        var survey = Survey(cls, moby);

        // The rows, by the class that owns each.
        var schema = Engine.Assets.LevelElements.ClassFields.Of(game, cls);
        var components = schema.Where(f => f.Kind == Engine.Assets.LevelElements.ClassFieldKind.Component).ToList();
        var fieldsByWord = schema.Where(f => f.Kind is not (Engine.Assets.LevelElements.ClassFieldKind.Component or Engine.Assets.LevelElements.ClassFieldKind.Reference))
            .ToLookup(f => f.Offset & ~3);
        var referencesAt = fieldReferences.GroupBy(r => r.Offset).ToDictionary(g => g.Key, g => g.First());
        Engine.Assets.LevelElements.ClassField? ComponentOf(int offset) =>
            components.LastOrDefault(c => c.Offset <= offset && offset < c.Offset + c.Length);
        uint OwnerOf(int offset) =>
            ComponentOf(offset)?.Owner ?? Engine.Assets.LevelElements.ClassFields.At(game, cls, offset)?.Owner ?? registry?.Owner(cls, offset) ?? cls;
        bool Unset(int offset, int size) =>
            settings.AsSpan(offset, size).IndexOfAnyExcept((byte)0) < 0 && settings.AsSpan(offset, size).SequenceEqual(loaded.AsSpan(offset, size));

        var rows = new List<SettingRow>();
        for (int offset = 4; offset + 4 <= settings.Length; offset += 4)
        {
            if (referencesAt.TryGetValue(offset, out var reference))
            {
                rows.Add(new SettingRow(OwnerOf(offset), offset, 4, Engine.Assets.LevelElements.ClassFields.At(game, cls, offset), reference, ComponentOf(offset)));
                continue;
            }
            int covered = 0;
            foreach (var field in fieldsByWord[offset])
            {
                int size = field.Kind switch { Engine.Assets.LevelElements.ClassFieldKind.U8 => 1, Engine.Assets.LevelElements.ClassFieldKind.U16 => 2, _ => 4 };
                if (field.Offset + size > settings.Length) continue;
                for (int b = 0; b < size; b++) covered |= 1 << (field.Offset - offset + b);
                // A field the game overwrites always shows (read-only); any other only once it holds something.
                if (!field.RunTime && Unset(field.Offset, size) && !_showZeroWords) continue;
                rows.Add(new SettingRow(field.Owner, field.Offset, size, field, null, ComponentOf(field.Offset)));
            }
            bool rest = covered == 0;
            for (int b = 0; b < 4 && !rest; b++)
                if ((covered & (1 << b)) == 0 && !Unset(offset + b, 1)) rest = true;
            if (!rest || (Unset(offset, 4) && !_showZeroWords)) continue;
            // A word nothing types: raw. A known reference field left unset lands here too, under its name.
            rows.Add(new SettingRow(OwnerOf(offset), offset, 4, null, null, ComponentOf(offset)));
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
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30f);
            ImGui.TextDisabled($"+0x{row.Offset:X2}  {type}");
            if (row.Field is { } field)
            {
                ImGui.TextUnformatted(field.Description);
                if (field.RunTime) ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), LM.Get("GUI_Frame_InstanceInspector_FieldRunTimeHint"));
                ImGui.TextDisabled(LM.Get("GUI_Frame_InstanceInspector_FieldEvidence", field.Evidence));
            }
            // Across the class: what every instance of it in the level holds in this word.
            if (survey.Count > 1 && survey.Words.TryGetValue(row.Offset & ~3, out var across))
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

        void RowMenu(SettingRow row, bool edited)
        {
            if (!ImGui.BeginPopupContextItem("row_menu")) return;
            int at = row.Offset, length = row.Size;
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
            // A word nothing types is read as it was guessed; this changes the guess.
            if (row.Field == null && row.Reference == null && ImGui.BeginMenu(LM.Get("GUI_Frame_InstanceInspector_SettingsReadAs")))
            {
                foreach (var option in Enum.GetValues<SettingType>())
                    if (ImGui.MenuItem(option.ToString(), "", SettingTypes.GetValueOrDefault((cls, at)) == option)) SettingTypes[(cls, at)] = option;
                ImGui.EndMenu();
            }
            ImGui.EndPopup();
        }

        void Row(SettingRow row)
        {
            var bytes = settings.AsSpan(row.Offset, row.Size);
            bool edited = !bytes.SequenceEqual(loaded.AsSpan(row.Offset, row.Size));
            ImGui.PushID(row.Offset);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            string name = ClueClasses.FieldName(game, cls, row.Offset) ?? $"+0x{row.Offset:X2}";
            if (edited) ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), name);
            else if (row.RunTime || row.Field is null or { CodeOnly: true }) ImGui.TextDisabled(name);
            else ImGui.TextUnformatted(name);

            if (row.Reference is { } reference)
            {
                Tooltip(row, LM.Get("GUI_Frame_InstanceInspector_FieldReference"));
                ImGui.TableNextColumn();
                ReferenceLink(reference.Kind, reference.Name);
            }
            else if (row.Field is { } field)
            {
                string kind = field.Kind switch
                {
                    Engine.Assets.LevelElements.ClassFieldKind.U8 => "u8",
                    Engine.Assets.LevelElements.ClassFieldKind.U16 => "u16",
                    Engine.Assets.LevelElements.ClassFieldKind.S32 => "s32",
                    Engine.Assets.LevelElements.ClassFieldKind.F32 => "float",
                    Engine.Assets.LevelElements.ClassFieldKind.TextId => "text id",
                    _ => "u32",
                };
                Tooltip(row, field.RunTime ? LM.Get("GUI_Frame_InstanceInspector_FieldRunTime", kind) : kind);
                if (!field.RunTime) RowMenu(row, edited);
                ImGui.TableNextColumn();
                ImGui.BeginDisabled(field.RunTime);
                ImGui.SetNextItemWidth(-1f);
                switch (field.Kind)
                {
                    case Engine.Assets.LevelElements.ClassFieldKind.F32:
                    {
                        float f = System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(bytes);
                        if (ImGui.DragFloat("##v", ref f, MathF.Max(0.01f, MathF.Abs(f) * 0.01f), 0f, 0f, "%.6g"))
                            System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes, f);
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
                if (!field.RunTime && field.Kind != Engine.Assets.LevelElements.ClassFieldKind.TextId) TrackField(settingsBefore, "GUI_Undo_EditSetting");
                ImGui.EndDisabled();
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
            if (hint != null && ImGui.IsItemHovered()) ImGui.SetTooltip(hint);
            if (!open) return;
            foreach (var row in group) Row(row);
            ImGui.TreePop();
        }

        foreach (uint owner in owners)
        {
            var own = rows.Where(r => r.Owner == owner).ToList();
            // What counts: the fields a level can set. The run-time ones are there whatever the instance.
            int set = own.Count(r => !r.RunTime);
            string title = Engine.Assets.LevelElements.ClassFields.ClassName(game, owner) is { } known ? known
                : registry != null ? ClassLabel(registry, owner) : $"0x{owner:X}";
            bool open = ImGui.CollapsingHeader($"{title}  ({set})###class{owner}", owner == cls ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_ClassFoldHint", $"0x{owner:X}",
                    registry != null && registry.Sizes.TryGetValue(owner, out int size) ? $"0x{size:X}" : "?"));
            if (!open) continue;
            if (!ImGui.BeginTable($"##settings{owner}", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings)) continue;
            ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthStretch, 0.45f);
            ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch, 0.55f);
            foreach (var row in own.Where(r => r.Component == null && !r.RunTime)) Row(row);
            foreach (var component in own.Where(r => r.Component != null).Select(r => r.Component!).Distinct())
            {
                var inside = own.Where(r => r.Component == component).ToList();
                SubFold($"{(component.Name.Length > 0 ? component.Name : $"+0x{component.Offset:X2}")}  ({inside.Count(r => !r.RunTime)})###component{component.Offset}",
                    $"+0x{component.Offset:X2}, {LM.Get("GUI_Frame_InstanceInspector_FieldComponent", $"0x{component.Length:X}")}\n{component.Description}", inside);
            }
            var runTime = own.Where(r => r.Component == null && r.RunTime).ToList();
            if (runTime.Count > 0)
                SubFold($"{LM.Get("GUI_Frame_InstanceInspector_RunTimeGroup", runTime.Count)}###runtime", LM.Get("GUI_Frame_InstanceInspector_FieldRunTimeHint"), runTime);
            ImGui.EndTable();
        }
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
    private static bool _showZeroWords;

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
    /// the 3D view's selection follows. Names are matched as written; several instances can share one.</summary>
    private static void ReferenceLink(string kind, string name)
    {
        var target = FindInstance(kind, name);
        string label = $"{kind} {name}";
        if (target == null) { ImGui.TextUnformatted(label); return; }
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.75f, 1f, 1f));
        bool clicked = ImGui.Selectable($"{label}##ref_{kind}_{name}", false, ImGuiSelectableFlags.None, ImGui.CalcTextSize(label));
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_ReferenceSelect"));
        if (clicked) SelectionManager.Singleton.Select(target);
    }

    private static Entity? FindInstance(string kind, string name)
    {
        foreach (var region in EntityManager.Singleton.Regions)
        {
            IEnumerable<Entity> candidates = kind switch
            {
                "Volume" => region.Volumes.Entities.Where(e => e is EntityVolume v && v.BaseVolume.Name == name),
                "Clue" => region.Volumes.Entities.Where(e => e is EntityVolume v && v.BaseVolume.Clue?.Name == name),
                "Path" => region.Paths.Entities.Where(e => e is EntityPath p && p.Path.Name == name),
                "Controller" => region.Cameras.Entities.Where(e => e is EntityCamera c && c.Controller.Name == name),
                "Moby" => region.MobyInstances.Entities.Where(e => e.Name == name),
                _ => [],
            };
            if (candidates.FirstOrDefault() is { } found) return found;
        }
        return null;
    }

    private EntityVolume? _usedByVolume;
    private List<(string Kind, string User, string Field)> _usedBy = [];

    /// <summary>What names this volume in its settings (a moon's area, a camera's look-at volume...),
    /// each a link to it.</summary>
    private void RenderUsedBy(EntityVolume volume)
    {
        var name = volume.BaseVolume.Name;
        if (name == null) return;
        if (!ReferenceEquals(volume, _usedByVolume))
        {
            _usedByVolume = volume;
            var game = Core.LunaWindow.Instance.Level?.GameDefinition?.Id;
            _usedBy = [];
            foreach (var region in EntityManager.Singleton.Regions)
            {
                foreach (var other in region.Volumes.Entities.OfType<EntityVolume>())
                    if (other.BaseVolume.Clue is { } clue)
                        foreach (var reference in clue.References)
                            if (reference.Kind == "Volume" && reference.Name == name)
                                _usedBy.Add(("Clue", clue.Name, $"{clue.ClassName} +0x{reference.Offset:X}" +
                                    (ClueClasses.FieldName(game, clue.Class, reference.Offset) is { } field ? $" {field}" : "")));
                foreach (var camera in region.Cameras.Entities.OfType<EntityCamera>())
                    foreach (var reference in camera.Controller.References)
                        if (reference.Kind == "Volume" && reference.Name == name)
                            _usedBy.Add(("Controller", camera.Controller.Name, $"{camera.Controller.ClassName ?? $"0x{camera.Controller.Class:X}"} +0x{reference.Offset:X}"));
                foreach (var moby in region.MobyInstances.Entities.OfType<EntityMoby>())
                    foreach (var reference in moby.References)
                        if (reference.Kind == "Volume" && reference.Name == name)
                            _usedBy.Add(("Moby", moby.Name, $"{moby.BaseMoby.Name?.Split('/')[^1]} +0x{reference.Offset:X}"));
            }
        }
        if (_usedBy.Count == 0) return;
        ImGui.TextUnformatted(LM.Get("GUI_Frame_InstanceInspector_UsedBy"));
        foreach (var (kind, user, field) in _usedBy)
        {
            ImGui.Bullet();
            ReferenceLink(kind, user);
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
