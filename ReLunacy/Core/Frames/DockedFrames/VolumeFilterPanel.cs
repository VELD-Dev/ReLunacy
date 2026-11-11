using System.Numerics;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The 3D viewport's volume panel: which volumes are drawn, by kind (plain, or the class of the Clue a
/// volume is the shape of), by the editor layer they were placed in, and by Area or Scent, and what their
/// colour says. Each list doubles as the legend of its colour mode. The filters are the level's own, so
/// they start over with every level.
/// </summary>
internal sealed class VolumeFilterPanel
{
    public bool Open;
    private object? _level;

    public void Draw(ViewportOverlay overlay, float anchorLeft)
    {
        if (!Open || !overlay.BeginPanel("volumes", new Vector2(340f, 0f), anchorLeft)) return;
        var em = EntityManager.Singleton;
        var volumes = em.Regions.SelectMany(r => r.Volumes.Entities).OfType<EntityVolume>().Select(v => v.BaseVolume).ToList();

        var level = LunaWindow.Instance.Level;
        if (!ReferenceEquals(level, _level))
        {
            _level = level;
            em.HiddenVolumeKinds.Clear();
            em.HiddenVolumeLayers.Clear();
            em.VolumeGroupFilter = null;
        }

        if (ImGui.Checkbox(LM.Get("GUI_Volumes_Show"), ref em.renderVolumes))
            MenuBar.RenderMenuDraw.PersistRenderSettings();
        ImGui.SameLine();
        ImGui.TextDisabled(LM.Get("GUI_Volumes_Summary", volumes.Count(em.IsVolumeShown), volumes.Count));

        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo(LM.Get("GUI_Volumes_ColorBy"), LM.Get("GUI_Volumes_Color_" + em.VolumeColorMode)))
        {
            foreach (var mode in Enum.GetValues<VolumeColorMode>())
                if (ImGui.Selectable(LM.Get("GUI_Volumes_Color_" + mode), em.VolumeColorMode == mode))
                    em.VolumeColorMode = mode;
            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo(LM.Get("GUI_Volumes_Fill"), LM.Get("GUI_Volumes_Fill_" + em.VolumeFillMode)))
        {
            foreach (var mode in Enum.GetValues<VolumeFillMode>())
                if (ImGui.Selectable(LM.Get("GUI_Volumes_Fill_" + mode), em.VolumeFillMode == mode))
                    em.VolumeFillMode = mode;
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Volumes_FillHint"));
        if (em.VolumeFillMode != VolumeFillMode.None)
        {
            ImGui.SetNextItemWidth(160f);
            ImGui.SliderFloat(LM.Get("GUI_Volumes_FillOpacity"), ref em.VolumeFillOpacity, 0.05f, 0.9f, "%.2f");
        }

        // Kinds: plain volumes first, then the Clue classes, what kills first, then by count.
        if (ImGui.CollapsingHeader(LM.Get("GUI_Volumes_Kinds"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            var kinds = volumes.GroupBy(EntityManager.VolumeKind)
                .OrderByDescending(g => g.Key == EntityManager.PlainVolumeKind)
                .ThenBy(g => ClueClasses.Hazard(g.Key) == ClueHazard.None)
                .ThenByDescending(g => g.Count())
                .Select(g => (g.Key, Count: g.Count(), Classes: g.Select(v => v.Clue?.Class).Distinct().OfType<uint>().ToList()))
                .ToList();
            List("kind", kinds.Select(k => k.Key), em.HiddenVolumeKinds, k =>
            {
                var entry = kinds.First(x => x.Key == k);
                return k == EntityManager.PlainVolumeKind
                    ? (LM.Get("GUI_Volumes_Plain", entry.Count), new Vector3(em.VolumeColor.X, em.VolumeColor.Y, em.VolumeColor.Z),
                        LM.Get("GUI_Volumes_PlainHint"))
                    : ($"{k} ({entry.Count})", ClueClasses.Colour(k),
                        LM.Get("GUI_Volumes_ClueHint", string.Join(", ", entry.Classes.Select(c => $"0x{c:X}"))));
            });
        }

        var layers = volumes.Where(v => v.Layer != null).GroupBy(v => v.Layer!).OrderBy(g => g.Key).ToList();
        if (layers.Count > 0 && ImGui.CollapsingHeader(LM.Get("GUI_Volumes_Layers")))
            List("layer", layers.Select(g => g.Key), em.HiddenVolumeLayers,
                l => ($"{l} ({layers.First(g => g.Key == l).Count()})", ClueClasses.ColourOf(l), null));

        // Areas and Scents: show one group's volumes alone.
        var areas = volumes.SelectMany(v => v.Areas).Distinct().Order().ToList();
        var scents = volumes.SelectMany(v => v.Scents).Distinct().Order().ToList();
        if ((areas.Count > 0 || scents.Count > 0) && ImGui.CollapsingHeader(LM.Get("GUI_Volumes_Groups")))
        {
            ImGui.TextDisabled(LM.Get("GUI_Volumes_GroupsHint"));
            string current = em.VolumeGroupFilter is { } g
                ? LM.Get(g.Scent ? "GUI_Volumes_Scent" : "GUI_Volumes_Area", g.Name)
                : LM.Get("GUI_Volumes_AllGroups");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo("##volumegroup", current))
            {
                if (ImGui.Selectable(LM.Get("GUI_Volumes_AllGroups"), em.VolumeGroupFilter == null)) em.VolumeGroupFilter = null;
                foreach (var (scent, list) in new[] { (true, scents), (false, areas) })
                    foreach (var name in list)
                    {
                        int count = volumes.Count(v => (scent ? v.Scents : v.Areas).Contains(name));
                        if (ImGui.Selectable($"{LM.Get(scent ? "GUI_Volumes_Scent" : "GUI_Volumes_Area", name)} ({count})##{scent}{name}",
                                em.VolumeGroupFilter == (scent, name)))
                            em.VolumeGroupFilter = (scent, name);
                    }
                ImGui.EndCombo();
            }
        }
        overlay.EndPanel();
    }

    /// <summary>A checkbox per value with its colour, plus All / None; <paramref name="hidden"/> holds the unticked.
    /// Returns whether anything was ticked or unticked.</summary>
    internal static bool List(string id, IEnumerable<string> values, HashSet<string> hidden, Func<string, (string Label, Vector3 Colour, string? Tooltip)> describe)
    {
        var list = values.ToList();
        bool changed = false;
        if (ImGui.SmallButton($"{LM.Get("GUI_Collision_All")}##{id}")) { hidden.Clear(); changed = true; }
        ImGui.SameLine();
        if (ImGui.SmallButton($"{LM.Get("GUI_Collision_None")}##{id}")) { hidden.UnionWith(list); changed = true; }
        float rowHeight = ImGui.GetFrameHeightWithSpacing();
        bool scroll = list.Count > 12;
        if (scroll) ImGui.BeginChild($"##{id}list", new Vector2(0f, rowHeight * 12), ImGuiChildFlags.None);
        foreach (var value in list)
        {
            var (label, colour, tooltip) = describe(value);
            float size = ImGui.GetTextLineHeight();
            ImGui.ColorButton($"##swatch_{id}{value}", new Vector4(colour, 1f),
                ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop, new Vector2(size, size));
            ImGui.SameLine();
            bool on = !hidden.Contains(value);
            if (ImGui.Checkbox($"{label}##{id}{value}", ref on))
            {
                if (on) hidden.Remove(value);
                else hidden.Add(value);
                changed = true;
            }
            if (tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        }
        if (scroll) ImGui.EndChild();
        return changed;
    }
}
