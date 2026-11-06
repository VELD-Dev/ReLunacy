using System.Numerics;
using System.Text.RegularExpressions;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

internal class BasicEntityExplorer : DockedFrame, ILevelListener
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private enum Tabs { Mobys, Ties, UFrags, Navmesh, Volumes }

    /// <summary>
    /// One zone's worth of entities under one tab.
    ///
    /// Ties, UFrags and navmesh chunks belong to a zone, so they are listed under it rather than in
    /// one flat list - levels have no path or folder structure of their own, and the zone is the
    /// grouping the data does give us. Shrubs will join them once they are placed in the scene.
    ///
    /// The label carries the region name too when the level has more than one region, because a zone
    /// can be listed by several regions and each one builds its OWN EntityZone with its own entities
    /// (see EntityRegion). Those are genuinely different objects, so merging them by zone name would
    /// hide duplicates rather than tidy them away.
    /// </summary>
    private sealed record ZoneGroup(string Label, Entity[] Entities)
    {
        public Entity[] Filtered { get; set; } = Entities;
    }

    private Entity[] mobys = [];
    private Entity[] volumes = [];
    private Entity[] filteredMobys = [];
    private Entity[] filteredVolumes = [];
    private ZoneGroup[] tieGroups = [];
    private ZoneGroup[] ufragGroups = [];
    private ZoneGroup[] navmeshGroups = [];

    private Tabs currentTab;
    private string entityResearch = "";

    public BasicEntityExplorer()
    {
        FrameName = LM.Get("GUI_Frame_EntityExplorer");
    }

    protected override void Render(double deltaTime)
    {
        ImGui.BeginGroup();
        ImGui.InputTextWithHint("##", LM.Get("GUI_Frame_EntityExplorer_SearchEntities"), ref entityResearch, 128);
        // Filtering is cached rather than run per frame: a tab can hold tens of thousands of Ties and
        // the filter is a regex per name.
        if (ImGui.IsItemDeactivatedAfterEdit()) ApplyFilter();

        if (ImGui.BeginTabBar("hierarchy_filter", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton))
        {
            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EntityExplorer_MobysTab")))
            {
                currentTab = Tabs.Mobys;
                ShowFlat(filteredMobys);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EntityExplorer_TiesTab")))
            {
                currentTab = Tabs.Ties;
                ShowGrouped(tieGroups, "tie");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EntityExplorer_UFragsTab")))
            {
                currentTab = Tabs.UFrags;
                ShowGrouped(ufragGroups, "ufrag");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EntityExplorer_NavmeshTab")))
            {
                currentTab = Tabs.Navmesh;
                if (navmeshGroups.Length == 0)
                    ImGui.TextDisabled(LM.Get("GUI_Frame_EntityExplorer_NoNavmesh"));
                else
                    ShowGrouped(navmeshGroups, "navmesh");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EntityExplorer_VolumesTab")))
            {
                currentTab = Tabs.Volumes;
                ShowFlat(filteredVolumes);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.EndGroup();
    }

    // No RenderAsWindow override - see ShaderBrowser's comment: SetNextWindowSize/Pos on first
    // appearance cancelled the dockspace preset's placement, which this frame is a target of
    // ("Entity"/"Hierarchy"/"Explorer") and is one of the three frames open by default.

    /// <summary>Mobys and Volumes belong to a region, not a zone, so they stay flat.</summary>
    private void ShowFlat(Entity[] entities)
    {
        ImGui.BeginChild("hierarchy_container", ImGui.GetContentRegionAvail());
        foreach (var entity in entities) EntityRow(entity);
        ImGui.EndChild();
    }

    private void ShowGrouped(ZoneGroup[] groups, string id)
    {
        ImGui.BeginChild($"hierarchy_{id}", ImGui.GetContentRegionAvail());
        bool searching = entityResearch.Length > 1;
        int shown = 0;
        for (int i = 0; i < groups.Length; i++)
        {
            ZoneGroup group = groups[i];
            // A zone with nothing left after filtering is dropped entirely while searching, so the
            // list does not become a wall of empty zone headers.
            if (searching && group.Filtered.Length == 0) continue;
            shown++;
            // A search is meant to find things, so open the matching zones; otherwise leave each
            // zone's own open/closed state alone.
            if (searching) ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            if (!ImGui.TreeNodeEx($"{group.Label}  ({group.Filtered.Length})###{id}_zone_{i}")) continue;
            foreach (var entity in group.Filtered) EntityRow(entity);
            ImGui.TreePop();
        }
        if (shown == 0) ImGui.TextDisabled(LM.Get("GUI_Frame_EntityExplorer_NoMatches"));
        ImGui.EndChild();
    }

    private static void EntityRow(Entity entity)
    {
        if (!ImGui.Button($"{entity.Name.Split('/')[^1]}##entity_{entity.ID}")) return;
        SelectionManager.Singleton.Select(entity);
        var v3d = Core.LunaWindow.Instance.GetFirstFrame<View3D>();
        if (v3d != null) v3d.SelectedEntity = entity;
    }

    public void OnLevelUnloading() => Wipe();

    public void OnLevelLoaded()
    {
        // Built by walking the scene rather than filtering one flat list: the zone each Tie, UFrag
        // and navmesh chunk belongs to is in the scene graph, and a flat list has already lost it.
        var em = EntityManager.Singleton;
        bool nameRegions = em.Regions.Count > 1;

        mobys = [.. em.Regions.SelectMany(r => r.MobyInstances.Entities)];
        volumes = [.. em.Regions.SelectMany(r => r.Volumes.Entities)];

        List<ZoneGroup> ties = [], ufrags = [], navmeshes = [];
        foreach (var region in em.Regions)
        {
            foreach (var zone in region.Zones)
            {
                string label = nameRegions ? $"{region.RegionName}/{zone.ZoneName}" : zone.ZoneName;
                if (zone.TieInstances.Size > 0) ties.Add(new ZoneGroup(label, [.. zone.TieInstances.Entities]));
                if (zone.UFrags.Size > 0) ufrags.Add(new ZoneGroup(label, [.. zone.UFrags.Entities]));
                if (zone.Navmeshes.Count > 0) navmeshes.Add(new ZoneGroup(label, [.. zone.Navmeshes]));
            }
        }
        tieGroups = [.. ties];
        ufragGroups = [.. ufrags];
        navmeshGroups = [.. navmeshes];
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (entityResearch.Length > 1)
        {
            string pattern = BuildPattern(entityResearch);
            filteredMobys = Match(mobys, pattern);
            filteredVolumes = Match(volumes, pattern);
            foreach (var group in tieGroups) group.Filtered = Match(group.Entities, pattern);
            foreach (var group in ufragGroups) group.Filtered = Match(group.Entities, pattern);
            foreach (var group in navmeshGroups) group.Filtered = Match(group.Entities, pattern);
            return;
        }
        filteredMobys = mobys;
        filteredVolumes = volumes;
        foreach (var group in tieGroups) group.Filtered = group.Entities;
        foreach (var group in ufragGroups) group.Filtered = group.Entities;
        foreach (var group in navmeshGroups) group.Filtered = group.Entities;
    }

    /// <summary>Comma-separated terms, each matched literally, any of them enough.</summary>
    private static string BuildPattern(string searchArgs) =>
        string.Join("|", Regex.Escape(searchArgs.ToLower()).Split(',', StringSplitOptions.RemoveEmptyEntries));

    private static Entity[] Match(Entity[] entities, string pattern) =>
        [.. entities.Where(e => Regex.IsMatch(e.Name.ToLower(), pattern))];

    public static Entity[] SearchEntities(Entity[] entities, string searchArgs) =>
        Match(entities, BuildPattern(searchArgs));

    public void Wipe()
    {
        mobys = [];
        volumes = [];
        filteredMobys = [];
        filteredVolumes = [];
        tieGroups = [];
        ufragGroups = [];
        navmeshGroups = [];
    }
}
