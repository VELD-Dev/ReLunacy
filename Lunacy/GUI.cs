using ImGuiNET;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using Matrix4 = OpenTK.Mathematics.Matrix4;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Lunacy
{
    public class GUI
	{
		ImGuiController controller;
		Window wnd;
        int RegionsCount { get => EntityManager.Singleton.regions.Count; }
        int ZonesCount { get => EntityManager.Singleton.TFrags.Count; }
        int MobyHandleCount
        {
            get
            {
                var c = 0;
                foreach (var ti in EntityManager.Singleton.MobyHandles)
                    c += ti.Value.Count;
                return c;
            }
        }
        int TieInstancesCount
        {
            get
            {
                var c = 0;
                foreach (var ti in EntityManager.Singleton.TieInstances)
                    c += ti.Count;
                return c;
            }
        }
        int UFragsCount
        {
            get
            {
                var c = 0;
                foreach (var uf in EntityManager.Singleton.TFrags)
                    c += uf.Count;
                return c;
            }
        }
        int ShadersCount;

		Entity selectedEntity = null;

		bool raycast = false;

		public GUI(Window wnd)
		{
			controller = new ImGuiController(wnd.ClientSize.X, wnd.ClientSize.Y);
			this.wnd = wnd;
		}

		public void Resize()
		{
			controller.WindowResized(wnd.ClientSize.X, wnd.ClientSize.Y);
		}

		public void FrameBegin(double delta)
		{
			controller.Update(wnd, (float)delta);
		}

		public void ShowRegionsWindow()
		{
            RenderDockspace();

            //ImGui.SetNextWindowViewport(ImGui.GetWindowViewport().PointerID);
			//RenderRegionsExplorer();
            //ImGui.SetNextWindowViewport(ImGui.GetWindowViewport().PointerID);
            //RenderZonesExplorer();

			if(true)
			{
				RenderInfoOverlay();
			}

			if(selectedEntity != null)
			{
				ShowEntityInfo();
			}
		}

		public void Tick()
		{
			if(wnd.KeyboardState.IsKeyPressed(Keys.P)) raycast = !raycast;

			if(raycast)
			{

				OpenTK.Mathematics.Vector2 mouse = wnd.MouseState.Position;
				OpenTK.Mathematics.Vector3 viewport = new Vector3(
					(2 * mouse.X) / wnd.ClientSize.X - 1,
					1 - (2 * mouse.Y) / wnd.ClientSize.Y,
					1
				).ToOpenTK();
				OpenTK.Mathematics.Vector4 homogeneousClip = new(viewport.X, viewport.Y, -1, 1);
				OpenTK.Mathematics.Vector4 eye = Matrix4.Invert(Matrix4.Transpose(Camera.ViewToClip)) * homogeneousClip;
				eye.Z = -1;
				eye.W = 0;
				OpenTK.Mathematics.Vector3 world = (Matrix4.Invert(Matrix4.Transpose(Camera.WorldToView)) * eye).Xyz;
				world.Normalize();
				string entityNames = string.Empty;
				for(int i = 0; i < EntityManager.Singleton.MobyHandles.Count; i++)
				{
					for(int j = 0; j < EntityManager.Singleton.MobyHandles.ElementAt(i).Value.Count; j++)
					{
						if(EntityManager.Singleton.MobyHandles.ElementAt(i).Value[j].IntersectsRay(world, -Camera.transform.position))
						{
							entityNames += $"{EntityManager.Singleton.MobyHandles.ElementAt(i).Value[j].name}\n";
						}
					}
				}
				for(int i = 0; i < EntityManager.Singleton.TieInstances.Count; i++)
				{
					for(int j = 0; j < EntityManager.Singleton.TieInstances[i].Count; j++)
					{
						if(EntityManager.Singleton.TieInstances[i][j].IntersectsRay(world, -Camera.transform.position))
						{
							entityNames += $"{EntityManager.Singleton.TieInstances[i][j].name}\n";
						}
					}
				}
				ImGui.SetTooltip(entityNames);
			}			
		}

		public void KeyPress(int c)
		{
			controller.PressChar((char)c);
		}

        public void RenderDockspace()
        {
            ImGuiWindowFlags winflags = ImGuiWindowFlags.NoDocking
                | ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.NoCollapse
                | ImGuiWindowFlags.NoResize
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoBringToFrontOnFocus
                | ImGuiWindowFlags.NoNavFocus;
            ImGui.SetNextWindowViewport(ImGui.GetWindowViewport().ID);
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().WorkPos);
            ImGui.SetNextWindowSize(ImGui.GetMainViewport().WorkSize);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0);
            ImGui.Begin("dockspace", winflags);

            uint dockspaceId = ImGui.GetID("dockspace");
            ImGui.DockSpace(dockspaceId, new(0,0), ImGuiDockNodeFlags.None);
            ImGui.DockSpaceOverViewport();
            ImGui.PopStyleVar();
        }

        void SearchEntities(in Dictionary<string, List<Entity>> dict, string args, out Dictionary<string, List<Entity>> searchResult)
        {
            searchResult = new();
            foreach (var kvp in dict)
            {
                var catResults = new List<Entity>();
                foreach(var entity in kvp.Value)
                {
                    string name = entity.name;
                    string searchRegex = string.Join("|", Regex.Escape(args.ToLower()).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                    if(Regex.IsMatch(name.ToLower(), searchRegex))
                    { 
                        catResults.Add(entity);
                    }
                }

                if (catResults.Count == 0)
                    continue;

                searchResult.Add(kvp.Key, catResults);
            }
        }

        Dictionary<string, List<Entity>> FilteredMobyHandles;
        string mobysSearchArgs = string.Empty;
        bool mobysDictInitialized = false;
		public void RenderRegionsExplorer()
        {
            if(!mobysDictInitialized)
            {
                FilteredMobyHandles = EntityManager.Singleton.MobyHandles;
                mobysDictInitialized = true;
            }

            uint dockspaceId = ImGui.GetID("dockspace");
            ImGui.SetNextWindowDockID(dockspaceId, ImGuiCond.Once);
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetWorkCenter(), ImGuiCond.FirstUseEver);
            ImGui.Begin("Regions", ImGuiWindowFlags.AlwaysVerticalScrollbar);
            if(ImGui.InputTextWithHint("Search", "blob_small, QWARK_NURSE, etc...", ref mobysSearchArgs, 0xFF, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (mobysSearchArgs.Length < 3)
                {
                    FilteredMobyHandles = EntityManager.Singleton.MobyHandles;
                }
                else
                {
                    SearchEntities(in EntityManager.Singleton.MobyHandles, mobysSearchArgs, out FilteredMobyHandles);
                }
            }
            ImGui.Separator();
            foreach (var mobys in FilteredMobyHandles)
            {
                if (ImGui.CollapsingHeader(mobys.Key))
                {
                    for (int i = 0; i < mobys.Value.Count; i++)
                    {
                        ImGui.PushID($"{mobys.Key}:{i}:{mobys.Value[i].name}");
                        if (ImGui.Button(mobys.Value[i].name))
                        {
                            Camera.transform.position = -mobys.Value[i].transform.position;
                            selectedEntity = mobys.Value[i];
                        }
                        ImGui.PopID();
                    }
                }
            }
            ImGui.End();
        }

        string tiesSearchArgs = string.Empty;
        readonly Dictionary<string, List<Entity>> TieInstances = new();
        Dictionary<string, List<Entity>> TieInstancesFiltered;
        bool tiesDictInitialized = false;
        public void RenderZonesExplorer()
		{
            if(tiesDictInitialized == false)
            {
                for (int i = 0; i < EntityManager.Singleton.TieInstances.Count; i++)
                {
                    var l = new List<Entity>();
                    foreach(var tie in EntityManager.Singleton.TieInstances[i])
                    {
                        l.Add(tie);
                    }
                    TieInstances.Add(EntityManager.Singleton.zones[i].name, l);
                }
                tiesDictInitialized = true;
                TieInstancesFiltered = TieInstances;
            }

            uint dockspaceId = ImGui.GetID("dockspace");
            ImGui.SetNextWindowDockID(dockspaceId, ImGuiCond.Once);
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetWorkCenter(), ImGuiCond.FirstUseEver);
            ImGui.Begin("Zones", ImGuiWindowFlags.AlwaysVerticalScrollbar);
            if(ImGui.InputTextWithHint("Search", "terrain, host, etc...", ref tiesSearchArgs, 0xFF, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if(tiesSearchArgs.Length < 3)
                {
                    TieInstancesFiltered = TieInstances;
                }
                else
                {
                    SearchEntities(in TieInstances, tiesSearchArgs, out TieInstancesFiltered);
                }
            }
            ImGui.Separator();
            foreach(var ties in TieInstancesFiltered)
            {
                if (ImGui.CollapsingHeader(ties.Key))
                {
                    for(int i = 0; i < ties.Value.Count; i++)
                    {
                        string tieName = ties.Value[i].name;

                        ImGui.PushID($"{ties.Key}:{i}:{tieName}");
                        if (ImGui.Button(tieName))
                        {
                            Camera.transform.position = -ties.Value[i].transform.position;
                            selectedEntity = ties.Value[i];
                        }
                        ImGui.PopID();
                    }
                }
            }
            ImGui.End();
        }

        public void RenderInfoOverlay()
        {
            ImGuiIOPtr io = ImGui.GetIO();
            ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;
            float padding = 10f;
            ImGuiViewportPtr viewport = ImGui.GetMainViewport();
            Vector2 work_pos = viewport.WorkPos;
            Vector2 work_size = viewport.WorkSize;
            Vector2 win_pos, win_pos_pivot;
            win_pos.X = work_pos.X + padding;
            win_pos.Y = work_pos.Y + work_size.Y - padding;
            win_pos_pivot.X = 0;
            win_pos_pivot.Y = 1f;
            ImGui.SetNextWindowPos(win_pos, ImGuiCond.Always, win_pos_pivot);
            windowFlags |= ImGuiWindowFlags.NoMove;  // Locks the overlay;

            ImGui.SetNextWindowBgAlpha(0.4f);
            if (ImGui.Begin("Stats", windowFlags))
            {
                ImGui.Text("Camera info");
                ImGui.Separator();
                ImGui.Text($"Pos: {-Camera.transform.position}");
                ImGui.Text($"Rot: {Camera.transform.eulerRotation}");
                ImGui.Spacing();
                ImGui.Text("Statistics");
                ImGui.Separator();
                ImGui.Text("Framerate: ");
                ImGui.SameLine();
                ImGui.TextColored(Window.framerate > 25 ? new Vector4(0.15f, 1f, 0.15f, 1f) : new Vector4(1f, 0.15f, 0.15f, 1f), $"{Math.Round(Window.framerate)}FPS");
                ImGui.Text($"Regions: {RegionsCount}");
                ImGui.Text($"Zones: {ZonesCount}");
                ImGui.Text($"FilteredMobyHandles: {MobyHandleCount}");
                ImGui.Text($"Ties: {TieInstancesCount}");
                ImGui.Text($"UFrags: {UFragsCount}");
                ImGui.Text($"Shaders: {ShadersCount}");
                ImGui.Text($"Drawables: {EntityManager.Singleton.opaqueDrawables.Count + EntityManager.Singleton.transparentDrawables.Count}");
            }
            ImGui.End();
        }

		private void ShowEntityInfo()
		{
			ImGui.Begin($"{selectedEntity.name} Properties");
			bool posChanged = false;
			bool rotChanged = false;
			bool scaleChanged = false;
			System.Numerics.Vector3 position = selectedEntity.transform.position.ToNumerics();
			System.Numerics.Vector3 rotation = (selectedEntity.transform.eulerRotation * (180f / MathHelper.Pi)).ToNumerics();
			System.Numerics.Vector3 scale = selectedEntity.transform.scale.ToNumerics();
			if(ImGui.InputFloat3("position", ref position)) posChanged = true;
			if(ImGui.InputFloat3("Rotation", ref rotation)) rotChanged = true;
			if(ImGui.InputFloat3("Scale", ref scale)) scaleChanged = true;
			if(posChanged) selectedEntity.SetPosition(Utils.ToOpenTK(position));
			if(rotChanged) selectedEntity.SetRotation(Utils.ToOpenTK(rotation / (180f / MathHelper.Pi)));
			if(scaleChanged) selectedEntity.SetScale(Utils.ToOpenTK(scale));
			ImGui.End();
			//ImGui.ShowDemoWindow();
		}

		public void FrameEnd()
		{
			controller.Render();
		}
	}
}