namespace ReLunacy.Frames;

internal class EditorSettingsFrame : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoResize;

    public EditorSettings Settings => Window.Singleton?.Settings;
    public int currentMsaa = 0;

    public EditorSettingsFrame() : base()
    {
        FrameName = "Editor settings";
    }

    protected override void Render(float deltaTime)
    {
        ImGui.Text("Visual settings");
        ImGui.BeginGroup();
        ImGui.DragFloat("Render distance", ref Settings.RenderDistance);
        ImGui.Combo("MSAA Level", ref currentMsaa, ["Disabled", "x2", "x4", "x8", "x16", "x32"], 6);
        if(ImGui.CollapsingHeader("Advanced"))
        {
            ImGui.Text("Custom shaders");
        }
        ImGui.EndGroup();

        ImGui.Separator();

        ImGui.Text("Camera settings");
        ImGui.BeginGroup();
        ImGui.DragFloat("Camera speed", ref Settings.MoveSpeed);
        ImGui.DragFloat("Camera Shift speed", ref Settings.MaxSpeed);
        ImGui.EndGroup();

        ImGui.Separator();

        ImGui.Text("Overlay settings");
        ImGui.BeginGroup();
        ImGui.SliderFloat("Background Opacity", ref Settings.OverlayOpacity, 0f, 1f);
        ImGui.EndGroup();

        ImGui.Separator();

        ImGui.BeginGroup();
        if(ImGui.Button("Save"))
        {
            Settings.SaveSettingsToFile();
        }
        ImGui.SameLine();
        if(ImGui.Button("Cancel"))
        {
            Settings.ReloadSettings();
        }
        ImGui.SameLine();
        if(ImGui.Button("Close"))
        {
            Settings.ReloadSettings();
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
