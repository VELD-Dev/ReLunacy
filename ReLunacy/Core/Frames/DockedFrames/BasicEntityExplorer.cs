using Hexa.NET.ImGui;
using LibLunacy.Numerics;
using ReLunacy.Core.EntityManagement;
using ReLunacy.Utility.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ReLunacy.Core.Frames.DockedFrames;

internal class BasicEntityExplorer : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().WorkPos + ImGui.GetMainViewport().WorkSize * 0.5f;
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private enum Tabs
    {
        Mobys,
        Ties,
        UFrags,
        Volumes
    }

    public Entity[] Entities { get => [.. mobys, .. ties, .. ufrags, .. volumes]; }
    private Entity[] mobys = [];
    private Entity[] ties = [];
    private Entity[] ufrags = [];
    private Entity[] volumes = [];
    private Entity[] _searchResults = [];
    private Tabs currentTab;

    private string entityResearch = "";

    public BasicEntityExplorer() : base()
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
                switch (currentTab)
                {
                    case Tabs.Mobys: SearchEntities(in mobys, entityResearch, out _searchResults); break;
                    case Tabs.Ties: SearchEntities(in ties, entityResearch, out _searchResults); break;
                    case Tabs.UFrags: SearchEntities(in ufrags, entityResearch, out _searchResults); break;
                    case Tabs.Volumes: SearchEntities(in volumes, entityResearch, out _searchResults); break;
                }
            }
            else
            {
                switch (currentTab)
                {
                    case Tabs.Mobys: _searchResults = mobys; break;
                    case Tabs.Ties: _searchResults = ties; break;
                    case Tabs.UFrags: _searchResults = ufrags; break;
                    case Tabs.Volumes: _searchResults = volumes; break;
                }
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

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowSize(new(200, 600), ImGuiCond.Once);
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Once, new(0.5f));
        base.RenderAsWindow(deltaTime);
    }

    public void ShowEntities(Entity[] entities)
    {
        ImGui.BeginChild("hierarchy_container", ImGui.GetContentRegionAvail());
        foreach (var entity in entities)
        {
            if (ImGui.Button(entity.Name.Split('/')[^1]))
            {
                if (LunaWindow.Instance.IsAnyFrameOpened<View3D>())
                {
                    //LunaWindow.Instance.GetFirstFrame<View3D>().SelectedEntity = entity;
                }
            }
        }
        ImGui.EndChild();
    }

    public void SetEntities(List<Entity> newEntityList)
    {
        mobys = [.. newEntityList.FindAll(e => e.GetType() == typeof(EntityMoby))];
        ties = [.. newEntityList.FindAll(e => e.GetType() == typeof(EntityTie))];
        ufrags = [.. newEntityList.FindAll(e => e.GetType() == typeof(EntityUFrag))];
        volumes = [.. newEntityList.FindAll(e => e.GetType() == typeof(EntityVolume))];
    }

    public void SearchEntities(in Entity[] entities, string searchArgs, out Entity[] res)
    {
        List<Entity> results = [];
        string searchRegex = string.Join("|", Regex.Escape(searchArgs.ToLower()).Split(',', StringSplitOptions.RemoveEmptyEntries));
        foreach (var entity in entities)
        {
            string name = entity.Name;
            if (Regex.IsMatch(name.ToLower(), searchRegex))
            {
                results.Add(entity);
            }
        }
        res = [.. results];
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
