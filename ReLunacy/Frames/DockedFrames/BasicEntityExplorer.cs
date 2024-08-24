using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Frames.DockedFrames
{
    internal class BasicEntityExplorer : DockedFrame
    {
        protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
        protected override System.Numerics.Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
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
            FrameName = "Entity Explorer";
        }

        public BasicEntityExplorer(List<Entity> entities)
        {
            SetEntities(entities);
        }

        protected override void Render(float deltaTime)
        {
            ImGui.BeginGroup();
            ImGui.InputTextWithHint("", "Search for entity...", ref entityResearch, 128);
            if(ImGui.IsItemDeactivatedAfterEdit())
            {
                if(entityResearch.Length > 1)
                {
                    switch(currentTab)
                    {
                        case Tabs.Mobys: SearchEntities(in mobys, entityResearch, out _searchResults); break;
                        case Tabs.Ties: SearchEntities(in ties, entityResearch, out _searchResults); break;
                        case Tabs.UFrags: SearchEntities(in ufrags, entityResearch, out _searchResults); break;
                        case Tabs.Volumes: SearchEntities(in volumes, entityResearch, out _searchResults); break;
                    }
                }
                else
                {
                    switch(currentTab)
                    {
                        case Tabs.Mobys: _searchResults = mobys; break;
                        case Tabs.Ties: _searchResults = ties; break;
                        case Tabs.UFrags: _searchResults = ufrags; break;
                        case Tabs.Volumes: _searchResults = volumes; break;
                    }
                }
            }
            if(ImGui.BeginTabBar("hierarchy_filter", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton))
            {
                if (ImGui.BeginTabItem("Mobys"))
                {
                    currentTab = Tabs.Mobys;
                    ShowEntities(_searchResults);
                    ImGui.EndTabItem();
                }
                if (ImGui.IsItemClicked()) _searchResults = mobys;

                if (ImGui.BeginTabItem("Ties"))
                {
                    currentTab = Tabs.Ties;
                    ShowEntities(_searchResults);
                    ImGui.EndTabItem();
                }
                if (ImGui.IsItemActivated()) _searchResults = ties;

                if (ImGui.BeginTabItem("UFrags"))
                {
                    currentTab = Tabs.UFrags;
                    ShowEntities(_searchResults);
                    ImGui.EndTabItem();
                }
                if (ImGui.IsItemClicked()) _searchResults = ufrags;

                if (ImGui.BeginTabItem("Volumes"))
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

        public void ShowEntities(Entity[] entities)
        {
            ImGui.BeginChild("hierarchy_container");
            foreach(var entity in entities)
            {
                if (ImGui.Button(entity.name.Split('/')[^1]))
                {
                    if (Window.Singleton.IsAnyFrameOpened<View3DFrame>())
                    {
                        Window.Singleton.GetFirstFrame<View3DFrame>().SelectedEntity = entity;
                    }
                }
            }
            ImGui.EndChild();
        }

        public void SetEntities(List<Entity> newEntityList)
        {
            mobys = [.. newEntityList.FindAll(e => e.instance.GetType() == typeof(Region.CMobyInstance))];
            ties = [.. newEntityList.FindAll(e => e.instance.GetType() == typeof(CZone.CTieInstance))];
            ufrags = [.. newEntityList.FindAll(e => e.instance.GetType() == typeof(CZone.UFrag))];
            volumes = [.. newEntityList.FindAll(e => e.instance.GetType() == typeof(Region.CVolumeInstance))];
        }

        public void SearchEntities(in Entity[] entities, string searchArgs, out Entity[] res)
        {
            List<Entity> results = [];
            string searchRegex = string.Join("|", Regex.Escape(searchArgs.ToLower()).Split(',', StringSplitOptions.RemoveEmptyEntries));
            foreach (var entity in entities)
            {
                string name = entity.name;
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
        }
    }
}
