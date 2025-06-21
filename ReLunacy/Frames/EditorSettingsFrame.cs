namespace ReLunacy.Frames;

internal class EditorSettingsFrame : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoResize;

    private string[] AAoptions = [ "Disabled", "x2", "x4", "x8", "x16", "x32", "x64", "x128", "x256", "x512"];
    private string[] Languages;
    private int selectedLanguage = 0;
    private int currLanguage = 0;
    public int currentMsaa = 0;
    private int maxMsaa = 8;
    public int currentVSync = (int)Program.Settings.VSyncMode;
    public int currentLogLevel = (int)Program.Settings.LogLevel;

    public EditorSettingsFrame() : base()
    {
        FrameName = LM.Get("GUI_Frame_EditorSettings");
        maxMsaa = (int)Math.Log2(GL.GetInteger(GetPName.MaxSamples));
        Languages = [.. LM.Languages.Select(l => l.Value.LangName)];
    }

    protected override void Render(float deltaTime)
    {
        ImGui.BeginChild("settings", new(0, 450));

        if(ImGui.BeginTabBar("settings_tab"))
        {
            if(ImGui.BeginTabItem(LM.Get("GUI_Frame_EditorSettings_VisualSettings")))
            {
                ImGui.BeginGroup();
                ImGui.DragFloat(LM.Get("GUI_Frame_EditorSettings_FarClipDist"), ref Program.Settings.RenderDistance, 25, 150, 10000, "%0.1fm");
                ImGui.Combo(LM.Get("GUI_Frame_EditorSettings_MSAALevel"), ref currentMsaa, AAoptions, maxMsaa + 1);
                ImGui.Combo(LM.Get("GUI_Frame_EditorSettings_VSync"), ref currentVSync, [LM.Get("GUI_VSyncMode_Off"), LM.Get("GUI_VSyncMode_On"), LM.Get("GUI_VSyncMode_Adaptative")], 3);
                ImGui.Checkbox(LM.Get("GUI_Frame_EditorSettings_UseFrustrumCulling"), ref Program.Settings.FrustrumCulling);
                if(ImGui.Combo(LM.Get("GUI_Frame_EditorSettings_Language"), ref selectedLanguage, Languages, Languages.Length))
                {
                    if (LM.Languages.Values.ElementAt(selectedLanguage) == null)
                        selectedLanguage = currLanguage;

                    currLanguage = selectedLanguage;
                    string langCode = LM.Languages.Values.ElementAt(selectedLanguage).LangCode;
                    LM.TrySetLanguage(langCode);
                }
                if (ImGui.CollapsingHeader(LM.Get("GUI_Common_AdvancedCollapsed")))
                {
                    ImGui.Text(LM.Get("GUI_Frame_EditorSettings_CustomShadersPlaceholder"));
                }
                ImGui.EndGroup();
                ImGui.EndTabItem();
            }
            if(ImGui.BeginTabItem(LM.Get("GUI_Frame_EditorSettings_CameraSettings")))
            {
                ImGui.BeginGroup();
                ImGui.DragFloat(LM.Get("GUI_Frame_EditorSettings_CameraSpeed"), ref Program.Settings.CamMoveSpeed, 0.5f, 0.5f, 10000, "%0.2fm/s");
                ImGui.DragFloat(LM.Get("GUI_Frame_EditorSettings_CameraShiftSpeed"), ref Program.Settings.CamMaxSpeed, 0.5f, 0.5f, 10000, "%0.2fm/s");
                ImGui.SliderFloat(LM.Get("GUI_Frame_EditorSettings_FOV"), ref Program.Settings.CamFOV, 30f, 120f, "%0.1f°");
                ImGui.SliderFloat(LM.Get("GUI_Frame_EditorSettings_Sensitivity"), ref Program.Settings.CamSensivity, 0.001f, 2f, "%0.3f");
                ImGui.EndGroup();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EditorSettings_OverlaySettings")))
            {
                ImGui.BeginGroup();
                ImGui.Checkbox(LM.Get("GUI_Frame_EditorSettings_OverlayFPS"), ref Program.Settings.OverlayFramerate);
                ImGui.Checkbox(LM.Get("GUI_Frame_EditorSettings_OverlayProfiler"), ref Program.Settings.OverlayProfiler);

                ImGui.BeginGroup();
                ImGui.Indent();
                ImGui.DragInt(LM.Get("GUI_Frame_EditorSettings_OverlayProfiler_RefreshRate"), ref Program.Settings.ProfilerRefreshRate, 50, 0, 1000, "%dms");
                ImGui.DragInt(LM.Get("GUI_Frame_EditorSettings_OverlayProfiler_FPSSampleSize"), ref Program.Settings.ProfilerFrameSampleSize, 1, 3, 100, "%d");
                ImGui.EndGroup();

                ImGui.Checkbox(LM.Get("GUI_Frame_EditorSettings_OverlayLevelStats"), ref Program.Settings.OverlayLevelStats);
                ImGui.Checkbox(LM.Get("GUI_Frame_EditorSettings_OveralyCameraInfo"), ref Program.Settings.OverlayCamInfo);
                ImGui.SliderFloat(LM.Get("GUI_Frame_EditorSettings_OverlayBGOpacity"), ref Program.Settings.OverlayOpacity, 0f, 1f);
                ImGui.InputFloat2(LM.Get("GUI_Frame_EditorSettings_OverlayPadding"), ref Program.Settings.OverlayPadding, "%0.1f");
                ImGui.Combo(LM.Get("GUI_Frame_EditorSettings_OverlayLocation"), ref Program.Settings.OverlayPos, [ "Top-Left", "Top-Right", "Bottom-Left", "Bottom-Right", "Center..?" ], 5);
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
        if(ImGui.Button(LM.Get("GUI_Frame_EditorSettings_SaveApply")))
        {
            Program.Settings.VSyncMode = (VSyncMode)currentVSync;
            Program.Settings.LogLevel = (LunaLog.LogLevel)currentLogLevel;
            Camera.Main.FOV = Program.Settings.CamFOVRad;
            Camera.Main.RenderDistance = Program.Settings.RenderDistance;
            Program.Settings.SaveSettingsToFile();
        }
        ImGui.SameLine();
        if(ImGui.Button(LM.Get("GUI_Common_CancelWord")))
        {
            Program.Settings.ReloadSettings();
        }
        ImGui.SameLine();
        if(ImGui.Button(LM.Get("GUI_Common_CloseWord")))
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
