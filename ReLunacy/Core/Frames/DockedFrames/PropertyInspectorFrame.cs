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
                : LM.Get("GUI_Frame_InstanceInspector_VolumePlain"));
            if (baseVolume.Clue is { Settings.Length: > 4 } clueSettings)
                RenderSettings(clueSettings.Settings, clueSettings.References);
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
            ImGui.TextWrapped(LM.Get("GUI_Frame_InstanceInspector_CameraNote"));
            if (controller.Settings.Length > 4)
                RenderSettings(controller.Settings, controller.References);
        }
        else if (SelectedEntity is EntityPath pathEntity)
        {
            var path = pathEntity.Path;
            if (path.Layer != null)
                ImGui.Text(LM.Get("GUI_Frame_InstanceInspector_VolumeLayer", path.Layer));
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
    private static void RenderSettings(byte[] settings, IReadOnlyList<Engine.Assets.LevelElements.ClueReference> fieldReferences)
    {
        if (!ImGui.TreeNode(LM.Get("GUI_Frame_InstanceInspector_ClueSettings", settings.Length))) return;
        foreach (var reference in fieldReferences)
            ImGui.Text($"+0x{reference.Offset:X2}  {reference.Kind} {reference.Name}");
        var references = fieldReferences.Select(r => r.Offset).ToHashSet();
        if (ImGui.BeginTable("##cluesettings", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            for (int offset = 4; offset + 4 <= settings.Length; offset += 4)
            {
                uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(settings.AsSpan(offset));
                if (value == 0 || references.Contains(offset)) continue;
                float asFloat = BitConverter.UInt32BitsToSingle(value);
                bool floatLike = float.IsFinite(asFloat) && MathF.Abs(asFloat) is > 1e-6f and < 1e7f;
                // Four bytes each 0 or 1 are packed booleans (most Clue flags look like 01000000).
                bool flags = (value & 0xFEFEFEFE) == 0;
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextDisabled($"+0x{offset:X2}");
                ImGui.TableNextColumn(); ImGui.Text($"{value:X8}");
                ImGui.TableNextColumn();
                ImGui.Text(flags ? LM.Get("GUI_Frame_InstanceInspector_ClueFlags", value >> 24, value >> 16 & 0xFF, value >> 8 & 0xFF, value & 0xFF)
                    : floatLike ? $"{asFloat:G6}" : $"{value}");
            }
            ImGui.EndTable();
        }
        ImGui.TreePop();
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
            path.SetMode(part.Index, (PathHandleMode)mode);
            // Applies the mode at once: the outgoing handle (or the incoming one, on the last key) drives.
            if (path.OutHandleAt(part.Index) is { } outHandle) path.SetHandle(part.Index, true, outHandle);
            else if (path.InHandleAt(part.Index) is { } inHandle) path.SetHandle(part.Index, false, inHandle);
            part.Refresh();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_PathHandlesHint"));

        if (part.Index < path.KeyCount - 1 && ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_PathInsertKey")))
        {
            path.InsertKey(part.Index);
            SelectionManager.Singleton.Select(new EntityPathPoint(path, part.Index + 1, PathPart.Key));
            return;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_PathInsertKeyHint"));
        if (part.Index > 0 && part.Index < path.KeyCount - 1)
        {
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Frame_InstanceInspector_PathRemoveKey")))
            {
                path.RemoveKey(part.Index);
                SelectionManager.Singleton.Select(path);
                return;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_PathRemoveKeyHint"));
        }
    }
}
