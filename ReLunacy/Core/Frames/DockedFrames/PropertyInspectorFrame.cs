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

    /// <summary>A Clue's or a controller's settings: the fields that name another instance, then every non-zero
    /// field raw (their meaning is not decoded yet), typed as flags, a float or an integer.</summary>
    private static void RenderSettings(byte[] settings, IReadOnlyList<Engine.Assets.LevelElements.ClueReference> fieldReferences, uint cls)
    {
        var game = Core.LunaWindow.Instance.Level?.GameDefinition?.Id;
        // References first, named where the field is known, each a link to what it names.
        foreach (var reference in fieldReferences)
        {
            string? field = ClueClasses.FieldName(game, cls, reference.Offset);
            ImGui.TextDisabled($"+0x{reference.Offset:X2}");
            ImGui.SameLine();
            if (field != null) { ImGui.TextUnformatted(field + ":"); ImGui.SameLine(); }
            ReferenceLink(reference.Kind, reference.Name);
        }
        if (!ImGui.TreeNode(LM.Get("GUI_Frame_InstanceInspector_ClueSettings", settings.Length))) return;
        var references = fieldReferences.Select(r => r.Offset).ToHashSet();
        // What the settings held when first shown, so an edit stands out and can be undone.
        if (!LoadedSettings.TryGetValue(settings, out var loaded))
            LoadedSettings.Add(settings, loaded = (byte[])settings.Clone());
        var settingsBefore = new Lazy<Core.Undo.UndoManager.Session>(() => Core.Undo.UndoManager.Singleton.Begin([settings]));
        if (!ImGui.IsAnyItemActive()) _ = settingsBefore.Value;
        ImGui.Checkbox(LM.Get("GUI_Frame_InstanceInspector_SettingsShowZero"), ref _showZeroWords);
        ImGui.SameLine();
        ImGui.TextDisabled(LM.Get("GUI_Frame_InstanceInspector_SettingsHint"));
        if (ImGui.BeginTable("##cluesettings", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("offset");
            ImGui.TableSetupColumn("type");
            ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch);
            for (int offset = 4; offset + 4 <= settings.Length; offset += 4)
            {
                var word = settings.AsSpan(offset, 4);
                uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(word);
                bool edited = !word.SequenceEqual(loaded.AsSpan(offset, 4));
                if (references.Contains(offset) || (value == 0 && !edited && !_showZeroWords)) continue;
                ImGui.PushID(offset);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                string label = ClueClasses.FieldName(game, cls, offset) is { } name ? $"+0x{offset:X2} {name}" : $"+0x{offset:X2}";
                if (edited) ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), label);
                else ImGui.TextDisabled(label);
                if (ImGui.BeginPopupContextItem("word_menu"))
                {
                    if (ImGui.MenuItem(LM.Get("GUI_Frame_InstanceInspector_SettingsCopyToClass")))
                    {
                        var source = word.ToArray();
                        int at = offset;
                        var targets = SettingsOfClass(cls).ToList();
                        Core.Undo.UndoManager.Singleton.Record(LM.Get("GUI_Undo_CopySetting"), targets, () =>
                        {
                            foreach (var other in targets)
                                if (other.Length >= at + 4) source.CopyTo(other.AsSpan(at));
                        });
                    }
                    if (ImGui.MenuItem(LM.Get("GUI_Frame_InstanceInspector_SettingsRevert"), "", false, edited))
                    {
                        int at = offset;
                        Core.Undo.UndoManager.Singleton.Record(LM.Get("GUI_Undo_EditSetting"), [settings],
                            () => loaded.AsSpan(at, 4).CopyTo(settings.AsSpan(at, 4)));
                    }
                    ImGui.EndPopup();
                }

                // How the word is read: as last chosen for this class and offset, else guessed once from the value it
                // was loaded with and then kept. Guessing from the current value made an edit change the editor under
                // the cursor (a flag byte set to 2 is no longer 0-or-1 bytes, and the row turned hex).
                ImGui.TableNextColumn();
                if (!SettingTypes.TryGetValue((cls, offset), out var type))
                    SettingTypes[(cls, offset)] = type = GuessType(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(loaded.AsSpan(offset)));
                ImGui.SetNextItemWidth(70f);
                if (ImGui.BeginCombo("##type", type.ToString()))
                {
                    foreach (var option in Enum.GetValues<SettingType>())
                        if (ImGui.Selectable(option.ToString(), option == type)) SettingTypes[(cls, offset)] = type = option;
                    ImGui.EndCombo();
                }

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1f);
                switch (type)
                {
                    case SettingType.Float:
                    {
                        float f = BitConverter.UInt32BitsToSingle(value);
                        if (ImGui.DragFloat("##v", ref f, MathF.Max(0.01f, MathF.Abs(f) * 0.01f), 0f, 0f, "%.6g"))
                            System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(word, f);
                        TrackField(settingsBefore, "GUI_Undo_EditSetting");
                        break;
                    }
                    case SettingType.Int:
                    {
                        int n = (int)value;
                        if (ImGui.InputInt("##v", ref n)) System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(word, n);
                        TrackField(settingsBefore, "GUI_Undo_EditSetting");
                        break;
                    }
                    case SettingType.Hex:
                    {
                        string hex = value.ToString("X8");
                        // The size counts the terminating zero: 8 digits need 9 (8 made ImGui assert on a full buffer).
                        if (ImGui.InputText("##v", ref hex, 9, ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue)
                            && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
                            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(word, parsed);
                        TrackField(settingsBefore, "GUI_Undo_EditSetting");
                        break;
                    }
                    default:
                    {
                        // Four bytes, each its own field: most Clue flags are bytes of 0 or 1 (01 00 00 00).
                        for (int b = 0; b < 4; b++)
                        {
                            if (b > 0) ImGui.SameLine();
                            int v = word[b];
                            ImGui.SetNextItemWidth(42f);
                            if (ImGui.DragInt($"##b{b}", ref v, 0.1f, 0, 255)) word[b] = (byte)v;
                            TrackField(settingsBefore, "GUI_Undo_EditSetting");
                        }
                        break;
                    }
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.TreePop();
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
    private static bool _showZeroWords = true;

    /// <summary>The settings of every Clue and camera of a class in the level.</summary>
    private static IEnumerable<byte[]> SettingsOfClass(uint cls)
    {
        foreach (var region in EntityManager.Singleton.Regions)
        {
            foreach (var volume in region.Volumes.Entities.OfType<EntityVolume>())
                if (volume.BaseVolume.Clue is { } clue && clue.Class == cls) yield return clue.Settings;
            foreach (var camera in region.Cameras.Entities.OfType<EntityCamera>())
                if (camera.Controller.Class == cls) yield return camera.Controller.Settings;
        }
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
