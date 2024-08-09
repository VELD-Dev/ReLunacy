namespace ReLunacy.Frames;

internal class EditorSettingsFrame : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoResize;

    public int currentMsaa = 0;
    public int currentVSync = (int)Program.Settings.VSyncMode;
    public int currentLogLevel = (int)Program.Settings.LogLevel;

    public EditorSettingsFrame() : base()
    {
        FrameName = "Editor settings";
    }

    protected override void Render(float deltaTime)
    {
        ImGui.SeparatorText("Visual settings");
        ImGui.BeginGroup();
        ImGui.DragFloat("Render distance", ref Program.Settings.RenderDistance, 25, 150, 10000, "%f.1m");
        ImGui.Combo("MSAA Level", ref currentMsaa, ["Disabled", "x2", "x4", "x8", "x16", "x32"], 5);
        ImGui.Combo("V-Sync", ref currentVSync, ["Off", "On", "Adaptative"], 3);
        if(ImGui.CollapsingHeader("Advanced"))
        {
            ImGui.Text("Custom shaders");
        }
        ImGui.EndGroup();
        ImGui.Spacing();

        ImGui.SeparatorText("Camera settings");
        ImGui.BeginGroup();
        ImGui.DragFloat("Camera speed", ref Program.Settings.MoveSpeed, 0.5f, 0.5f, 10000, "%f.2m\\/s");
        ImGui.DragFloat("Camera shift speed", ref Program.Settings.MaxSpeed, 0.5f, 0.5f, 10000, "%f.2m\\/s");
        ImGui.EndGroup();
        ImGui.Spacing();

        ImGui.SeparatorText("Overlay settings");
        ImGui.BeginGroup();
        ImGui.Checkbox("Show Framerate", ref Program.Settings.OverlayFramerate);
        ImGui.Checkbox("Show Profiler", ref Program.Settings.OverlayProfiler);
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.DragInt("Profiler Refresh Rate", ref Program.Settings.ProfilerRefreshRate, 50, 0, 1000, "%dms");
        ImGui.Checkbox("Show Level Stats", ref Program.Settings.OverlayLevelStats);
        ImGui.SliderFloat("Background Opacity", ref Program.Settings.OverlayOpacity, 0f, 1f);
        ImGui.EndGroup();
        ImGui.Spacing();

        ImGui.SeparatorText("Debug settings");
        ImGui.BeginGroup();
        ImGui.Checkbox("Enable Debug", ref Window.Singleton.debugMode);
        ImGui.Combo("Logging Level", ref currentLogLevel, ["Debug", "Info", "Warning", "Error", "Fatal"], 5);
        ImGui.EndGroup();
        ImGui.Spacing();

        ImGui.Separator();
        ImGui.BeginGroup();
        if(ImGui.Button("Save & Apply"))
        {
            Program.Settings.VSyncMode = (VSyncMode)currentVSync;
            Program.Settings.LogLevel = (LunaLog.LogLevel)currentLogLevel;
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
        base.RenderAsWindow(deltaTime);
    }
}
