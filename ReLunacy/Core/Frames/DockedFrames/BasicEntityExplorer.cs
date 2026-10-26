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

    private enum Tabs { Mobys, Ties, UFrags, Volumes }

    public Entity[] Entities => [.. mobys, .. ties, .. ufrags, .. volumes];
    private Entity[] mobys = [];
    private Entity[] ties = [];
    private Entity[] ufrags = [];
    private Entity[] volumes = [];
    private Entity[] _searchResults = [];
    private Tabs currentTab;

    private string entityResearch = "";

    public BasicEntityExplorer()
    {
        FrameName = LM.Get("GUI_Frame_EntityExplorer");
    }

    public BasicEntityExplorer(List<Entity> entities) : this()
    {
        SetEntities(entities);
    }

    protected override void Render(double deltaTime)
    {
        ImGui.BeginGroup();
        ImGui.InputTextWithHint("##", LM.Get("GUI_Frame_EntityExplorer_SearchEntities"), ref entityResearch, 128);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (entityResearch.Length > 1)
            {
                _searchResults = currentTab switch
                {
                    Tabs.Mobys => SearchEntities(mobys, entityResearch),
                    Tabs.Ties => SearchEntities(ties, entityResearch),
                    Tabs.UFrags => SearchEntities(ufrags, entityResearch),
                    Tabs.Volumes => SearchEntities(volumes, entityResearch),
                    _ => _searchResults,
                };
            }
            else
            {
                _searchResults = currentTab switch
                {
                    Tabs.Mobys => mobys,
                    Tabs.Ties => ties,
                    Tabs.UFrags => ufrags,
                    Tabs.Volumes => volumes,
                    _ => _searchResults,
                };
            }
        }
        if (ImGui.BeginTabBar("hierarchy_filter", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton))
        {
            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EntityExplorer_MobysTab")))
            {
                currentTab = Tabs.Mobys;
                ShowEntities(_searchResults);
                ImGui.EndTabItem();
            }
            if (ImGui.IsItemClicked()) _searchResults = mobys;

            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EntityExplorer_TiesTab")))
            {
                currentTab = Tabs.Ties;
                ShowEntities(_searchResults);
                ImGui.EndTabItem();
            }
            if (ImGui.IsItemActivated()) _searchResults = ties;

            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EntityExplorer_UFragsTab")))
            {
                currentTab = Tabs.UFrags;
                ShowEntities(_searchResults);
                ImGui.EndTabItem();
            }
            if (ImGui.IsItemClicked()) _searchResults = ufrags;

            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EntityExplorer_VolumesTab")))
            {
                currentTab = Tabs.Volumes;
                ShowEntities(_searchResults);
                ImGui.EndTabItem();
            }
            if (ImGui.IsItemClicked()) _searchResults = volumes;

            ImGui.EndTabBar();
        }
        ImGui.EndGroup();
    }

    // No RenderAsWindow override - see ShaderBrowser's comment: SetNextWindowSize/Pos on first
    // appearance cancelled the dockspace preset's placement, which this frame is a target of
    // ("Entity"/"Hierarchy"/"Explorer") and is one of the three frames open by default.

    public void ShowEntities(Entity[] entities)
    {
        ImGui.BeginChild("hierarchy_container", ImGui.GetContentRegionAvail());
        foreach (var entity in entities)
        {
            if (ImGui.Button($"{entity.Name.Split('/')[^1]}##entity_{entity.ID}"))
            {
                SelectionManager.Singleton.Select(entity);
                var v3d = Core.LunaWindow.Instance.GetFirstFrame<View3D>();
                if (v3d != null) v3d.SelectedEntity = entity;
            }
        }
        ImGui.EndChild();
    }

    public void OnLevelUnloading() => SetEntities([]);
    public void OnLevelLoaded() => SetEntities(EntityManager.Singleton.AllEntities().ToList());

    public void SetEntities(List<Entity> newEntityList)
    {
        mobys = [.. newEntityList.FindAll(e => e is EntityMoby)];
        ties = [.. newEntityList.FindAll(e => e is EntityTie)];
        ufrags = [.. newEntityList.FindAll(e => e is EntityUFrag)];
        volumes = [.. newEntityList.FindAll(e => e is EntityVolume)];
        _searchResults = currentTab switch
        {
            Tabs.Mobys => mobys,
            Tabs.Ties => ties,
            Tabs.UFrags => ufrags,
            Tabs.Volumes => volumes,
            _ => mobys,
        };
    }

    public static Entity[] SearchEntities(Entity[] entities, string searchArgs)
    {
        List<Entity> results = [];
        string searchRegex = string.Join("|", Regex.Escape(searchArgs.ToLower()).Split(',', StringSplitOptions.RemoveEmptyEntries));
        foreach (var entity in entities)
        {
            if (Regex.IsMatch(entity.Name.ToLower(), searchRegex))
                results.Add(entity);
        }
        return [.. results];
    }

    public void Wipe()
    {
        mobys = [];
        ties = [];
        ufrags = [];
        volumes = [];
        _searchResults = [];
    }
}
