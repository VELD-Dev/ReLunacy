namespace ReLunacy.Frames;

internal class EditorSettingsFrame : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoResize;

    private string[] AAoptions = [ "Disabled", "x2", "x4", "x8", "x16", "x32", "x64", "x128", "x256", "x512"];
    public int currentMsaa = 0;
    public int currentVSync = (int)Program.Settings.VSyncMode;
    public int currentLogLevel = (int)Program.Settings.LogLevel;

    public EditorSettingsFrame() : base()
    {
        FrameName = "Editor settings";
    }

    protected override void Render(float deltaTime)
    {
        ImGui.BeginChild("settings", new(0, 450), true);

        if(ImGui.BeginTabBar("settings_tab"))
        {
            if(ImGui.BeginTabItem("Core settings"))
            {
                ImGui.BeginGroup();
                ImGui.DragFloat("Far clip distance", ref Program.Settings.RenderDistance, 25, 150, 10000, "%0.1fm");
                ImGui.Combo("MSAA level", ref currentMsaa, AAoptions, AAoptions.Length);
                ImGui.Combo("V-Sync", ref currentVSync, ["Off", "On", "Adaptative"], 3);
                if (ImGui.CollapsingHeader("Advanced"))
                {
                    ImGui.Text("Custom shaders");
                }
                ImGui.EndGroup();
                ImGui.EndTabItem();
            }
            if(ImGui.BeginTabItem("Camera settings"))
            {
                ImGui.BeginGroup();
                ImGui.DragFloat("Camera speed", ref Program.Settings.CamMoveSpeed, 0.5f, 0.5f, 10000, "%0.2fm/s");
                ImGui.DragFloat("Camera shift speed", ref Program.Settings.CamMaxSpeed, 0.5f, 0.5f, 10000, "%0.2fm/s");
                ImGui.SliderFloat("Field of view", ref Program.Settings.CamFOV, 30f, 120f, "%0.1f°");
                ImGui.SliderFloat("Camera sensivity", ref Program.Settings.CamSensivity, 0.001f, 2f, "%0.3f");
                ImGui.EndGroup();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Overlay settings"))
            {
                ImGui.BeginGroup();
                ImGui.Checkbox("Show Framerate", ref Program.Settings.OverlayFramerate);
                ImGui.Checkbox("Show Profiler", ref Program.Settings.OverlayProfiler);

                ImGui.BeginGroup();
                ImGui.Indent();
                ImGui.DragInt("Profiler Refresh Rate", ref Program.Settings.ProfilerRefreshRate, 50, 0, 1000, "%dms");
                ImGui.DragInt("Profiler FPS Sample Size", ref Program.Settings.ProfilerFrameSampleSize, 1, 3, 100, "%d");
                ImGui.EndGroup();

                ImGui.Checkbox("Show Level Stats", ref Program.Settings.OverlayLevelStats);
                ImGui.Checkbox("Show Camera Info", ref Program.Settings.OverlayCamInfo);
                ImGui.SliderFloat("Background Opacity", ref Program.Settings.OverlayOpacity, 0f, 1f);
                ImGui.InputFloat2("Overlay Padding", ref Program.Settings.OverlayPadding, "%0.1f");
                ImGui.Combo("Overlay Location", ref Program.Settings.OverlayPos, [ "Top-Left", "Top-Right", "Bottom-Left", "Bottom-Right", "Center..?" ], 5);
                ImGui.EndGroup();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Debug settings"))
            {
                ImGui.BeginGroup();
                ImGui.Checkbox("Enable Debug", ref Program.Settings.DebugMode);
                ImGui.Combo("Logging Level", ref currentLogLevel, ["Debug", "Info", "Warning", "Error", "Fatal"], 5);
                ImGui.Checkbox("Legacy Rendering Mode", ref Program.Settings.LegacyRenderingMode);
                ImGui.SameLine();
                ImGuiPlus.HelpMarker("If unsure, leave it unchecked. This heavily\naffects performances and has no reason to still be.");
                ImGui.EndGroup();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.BeginGroup();
        if(ImGui.Button("Save & Apply"))
        {
            Program.Settings.VSyncMode = (VSyncMode)currentVSync;
            Program.Settings.LogLevel = (LunaLog.LogLevel)currentLogLevel;
            Camera.Main.FOV = Program.Settings.CamFOVRad;
            Camera.Main.RenderDistance = Program.Settings.RenderDistance;
            Program.Settings.SaveSettingsToFile();
        }
        ImGui.SameLine();
        if(ImGui.Button("Cancel"))
        {
            Program.Settings.ReloadSettings();
        }
        ImGui.SameLine();
        if(ImGui.Button("Close"))
        {
            Program.Settings.ReloadSettings();
            isOpen = false;
        }
        ImGui.EndGroup();
    }

    public override void RenderAsWindow(float deltaTime)
    {
        ImGui.SetNextWindowSize(new(800, 600));
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetWorkCenter(), ImGuiCond.Once, new(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
