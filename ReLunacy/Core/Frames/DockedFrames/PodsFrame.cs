using System.Numerics;
using ReLunacy.Core.Selection;
using ReLunacy.Core.Undo;
using ReLunacy.Engine.Config;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// View > Pods: the level's Pods - named groups of Mobys (EntityPod) - one at a time. A Pod has no place of its
/// own, so it is shown through its members: "Select members" selects them all, which outlines them in the
/// viewport. Members are added from the viewport's selection and removed one by one or from the selection; every
/// change is undoable and written by the level save (PodWriter).
///
/// A Crack in Time's level segments are Pods (level.csv's Pod row: SEGMENT_SS1_MOON1A is moon 1A's crates, enemies,
/// Zoni...), so a segment Pod shows its column of the level's config beside its members - pools, Zoni, gold bolts.
/// </summary>
public sealed class PodsFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private string _filter = "";
    private string _memberFilter = "";
    private EntityPod? _pod;
    private EntityRegion? _podRegion;

    private string? _configFor;
    private ConfigCsv? _levelCsv;
    private ConfigLayout? _levelLayout;
    private ConfigSection? _levelSection;

    public PodsFrame()
    {
        FrameName = LM.Get("GUI_Frame_Pods");
    }

    /// <summary>Shows a Pod (from the inspector's "in Pods" line).</summary>
    public void Show(EntityPod pod)
    {
        _pod = pod;
        _podRegion = EntityManager.Singleton.Regions.FirstOrDefault(r => r.Pods.Contains(pod));
        _filter = "";
    }

    protected override void Render(double deltaTime)
    {
        var regions = EntityManager.Singleton.Regions;
        if (regions.Count == 0 || regions.All(r => r.Pods.Count == 0))
        {
            ImGui.TextDisabled(LM.Get("GUI_Pods_None"));
            return;
        }
        if (_pod != null && !regions.Any(r => r.Pods.Contains(_pod))) { _pod = null; _podRegion = null; }

        if (ImGui.BeginChild("##podlist", new Vector2(260f, 0f), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##podfilter", LM.Get("GUI_Pods_Filter"), ref _filter, 64);
            bool several = regions.Count(r => r.Pods.Count > 0) > 1;
            foreach (var region in regions)
            {
                if (region.Pods.Count == 0) continue;
                if (several) ImGui.TextDisabled(region.RegionName);
                for (int i = 0; i < region.Pods.Count; i++)
                {
                    var pod = region.Pods[i];
                    if (_filter.Length > 0 && !pod.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) &&
                        !(pod.Layer?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                    string label = $"{pod.Name}{(pod.Changed ? " *" : "")}  ({pod.Members.Count})##{region.RegionName}{i}";
                    if (ImGui.Selectable(label, ReferenceEquals(pod, _pod))) { _pod = pod; _podRegion = region; }
                    if (ImGui.IsItemHovered() && pod.Layer != null) ImGui.SetTooltip(pod.Layer);
                }
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();

        if (ImGui.BeginChild("##pod", Vector2.Zero))
        {
            if (_pod == null || _podRegion == null) ImGui.TextDisabled(LM.Get("GUI_Pods_Pick"));
            else RenderPod(_pod, _podRegion);
        }
        ImGui.EndChild();
    }

    private void RenderPod(EntityPod pod, EntityRegion region)
    {
        ImGui.TextUnformatted(pod.Name);
        ImGui.TextDisabled(LM.Get("GUI_Pods_Info", pod.Members.Count, pod.Layer ?? "-", region.RegionName, $"0x{pod.Tuid:X16}"));
        if (pod.Unresolved > 0) ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), LM.Get("GUI_Pods_Unresolved", pod.Unresolved));
        if (pod.Changed) ImGui.TextColored(new Vector4(0.35f, 0.8f, 1f, 1f), LM.Get("GUI_Pods_Unsaved"));

        var selection = SelectionManager.Singleton;
        var selectedMobys = selection.Selection.OfType<EntityMoby>().ToList();
        // Only this region's Mobys can be members: a Pod lists its own region's instances.
        var regionMobys = region.MobyInstances.Entities.OfType<EntityMoby>().ToHashSet();
        var addable = selectedMobys.Where(m => regionMobys.Contains(m) && !pod.Contains(m)).ToList();
        var removable = selectedMobys.Where(pod.Contains).ToList();

        ImGui.BeginDisabled(pod.Members.Count == 0);
        if (ImGui.Button(LM.Get("GUI_Pods_SelectMembers")))
        {
            selection.Set([.. pod.Members], pod.Members[0]);
            FocusOn(pod.Members);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(addable.Count == 0);
        if (ImGui.Button(LM.Get("GUI_Pods_AddSelected", addable.Count))) Edit(pod, LM.Get("GUI_Pods_UndoAdd", pod.Name), () => addable.ForEach(m => pod.Add(m)));
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(removable.Count == 0);
        if (ImGui.Button(LM.Get("GUI_Pods_RemoveSelected", removable.Count))) Edit(pod, LM.Get("GUI_Pods_UndoRemove", pod.Name), () => removable.ForEach(m => pod.Remove(m)));
        ImGui.EndDisabled();
        if (selectedMobys.Count > addable.Count + removable.Count)
            ImGui.TextDisabled(LM.Get("GUI_Pods_OtherRegion", selectedMobys.Count - addable.Count - removable.Count));

        RenderSegment(pod);

        ImGui.Separator();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##memberfilter", LM.Get("GUI_Pods_FilterMembers"), ref _memberFilter, 64);

        var members = pod.Members.Where(m => _memberFilter.Length == 0 ||
            m.Name.Contains(_memberFilter, StringComparison.OrdinalIgnoreCase) ||
            (MobyClass(m)?.Contains(_memberFilter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        if (!ImGui.BeginTable("##members", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(LM.Get("GUI_Pods_Member"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn(LM.Get("GUI_Pods_Class"), ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableHeadersRow();
        EntityMoby? remove = null;
        var clipper = new ImGuiListClipper();
        clipper.Begin(members.Count, ImGui.GetFrameHeightWithSpacing());
        while (clipper.Step())
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var m = members[i];
                ImGui.TableNextRow();
                ImGui.PushID(i);
                ImGui.TableNextColumn();
                if (ImGui.Selectable(m.Name, m.selected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap))
                {
                    if (ImGui.GetIO().KeyCtrl) selection.Toggle(m); else selection.Select(m);
                }
                // Double-click frames the member in the viewport.
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) FocusOn([m]);
                ImGui.TableNextColumn();
                ImGui.TextDisabled(MobyClass(m) ?? "-");
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("x")) remove = m;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Pods_RemoveOne"));
                ImGui.PopID();
            }
        clipper.End();
        ImGui.EndTable();
        if (remove != null) Edit(pod, LM.Get("GUI_Pods_UndoRemove", pod.Name), () => pod.Remove(remove));
    }

    /// <summary>A membership change, undoable: the members before and after are swapped back and forth.</summary>
    private static void Edit(EntityPod pod, string label, Action edit)
    {
        var before = pod.Members.ToList();
        edit();
        var after = pod.Members.ToList();
        if (before.SequenceEqual(after)) return;
        UndoManager.Singleton.Push(label, () => pod.SetMembers(before), () => pod.SetMembers(after));
    }

    private static string? MobyClass(EntityMoby moby)
    {
        string? name = moby.BaseMoby?.Name;
        if (string.IsNullOrEmpty(name)) return null;
        // "shared/gameplay_objects/crate/breakable_crate/breakable_crate.entity.irb" -> "breakable_crate"
        string file = name.Replace('\\', '/').Split('/')[^1];
        int dot = file.IndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    /// <summary>A segment Pod's column of the level's level.csv block: the column its Pod row names it in.</summary>
    private void RenderSegment(EntityPod pod)
    {
        string source = Program.ProvidedPath;
        if (_configFor != source)
        {
            _configFor = source;
            _levelCsv = null; _levelLayout = null; _levelSection = null;
            try
            {
                if (GameConfigs.GameFolderFor(source) is { } game && GameConfigs.LevelTable(game) is { } table)
                {
                    _levelCsv = GameConfigs.Read(table);
                    _levelLayout = ConfigLayout.Of(_levelCsv);
                    if (GameConfigs.LevelSection(_levelCsv, GameLibraryScanner.GetLevelNameFromPath(source)) is { } found)
                        _levelSection = _levelLayout.Sections.FirstOrDefault(s => s.Row == found.Rows.Start);
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException) { }
        }
        if (_levelCsv == null || _levelSection == null) return;
        int column = _levelSection.ColumnNames.FirstOrDefault(kv => kv.Value == pod.Name).Key;
        if (column <= 0) return;

        if (!ImGui.CollapsingHeader(LM.Get("GUI_Pods_Segment"), ImGuiTreeNodeFlags.DefaultOpen)) return;
        if (!ImGui.BeginTable("##segment", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp)) return;
        for (int r = _levelSection.Row + 1; r < _levelSection.End; r++)
        {
            string field = _levelCsv.Get(r, 1);
            string value = _levelCsv.Get(r, column);
            if (field.Length == 0 || field == "Pod" || value.Length == 0) continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(field);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(value);
        }
        ImGui.EndTable();
        ImGui.TextDisabled(LM.Get("GUI_Pods_SegmentHint"));
    }

    /// <summary>Frames entities in the 3D view, keeping the view's direction.</summary>
    private static void FocusOn(IReadOnlyList<Entity> entities)
    {
        var v3d = LunaWindow.Instance.GetFirstFrame<View3D>();
        if (v3d == null || entities.Count == 0) return;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var e in entities)
        {
            var s = e.WorldBoundingSphere;
            var c = s.W > 0f ? new Vector3(s.X, s.Y, s.Z) : e.Transform.Translation;
            float r = MathF.Max(s.W, 0.5f);
            min = Vector3.Min(min, c - new Vector3(r));
            max = Vector3.Max(max, c + new Vector3(r));
        }
        var centre = (min + max) * 0.5f;
        float distance = MathF.Max(3f, (max - min).Length() * 1.2f);
        var forward = v3d.Camera.GetForward();
        v3d.ExitPov(restore: false);
        v3d.Camera.Position = centre - forward * distance;
        v3d.Camera.Target = centre;
    }
}
