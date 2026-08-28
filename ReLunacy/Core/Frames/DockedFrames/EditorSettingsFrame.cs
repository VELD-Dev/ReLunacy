using System.Numerics;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using NeoVeldrid;

namespace ReLunacy.Core.Frames.DockedFrames;

internal class EditorSettingsFrame : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoResize;

    private readonly string[] AAoptions = ["Disabled", "x2", "x4", "x8", "x16", "x32"];
    // Order must match the ReLunacy.Engine.Rendering.TextureFiltering enum (the combo index is
    // cast straight to it).
    private readonly string[] FilteringOptions = ["Nearest (Point)", "Bilinear"];
    private int currentFiltering;
    private readonly string[] Languages;
    private int selectedLanguage;
    private int currLanguage;
    public int currentMsaa;
    private readonly int maxMsaa;
    public int currentLogLevel = (int)Program.Settings.LogLevel;

    public EditorSettingsFrame()
    {
        FrameName = LM.Get("GUI_Frame_EditorSettings");
        maxMsaa = (int)LunaWindow.Instance.GraphicsDevice.GetSampleCountLimit(PixelFormat.R8_G8_B8_A8_SInt, false);
        Languages = [.. LM.Languages.Select(l => l.Value.LangName)];
    }

    protected override void Render(double deltaTime)
    {
        ImGui.BeginChild("settings_child", new Vector2(0, 450), ImGuiChildFlags.None);

        if (ImGui.BeginTabBar("settings_tab"))
        {
            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EditorSettings_VisualSettings")))
            {
                ImGui.BeginGroup();
                if (ImGui.BeginCombo(LM.Get("GUI_Frame_EditorSettings_GraphicsBackend"), Program.Settings.GraphicsBackend.ToString()))
                {
                    foreach (var backend in Enum.GetValues<GraphicsBackend>())
                    {
                        if (ImGui.Selectable($"\t {backend}", backend == Program.Settings.GraphicsBackend))
                            Program.Settings.GraphicsBackend = backend;
                    }
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
                ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_EditorSettings_RestartRequiredHelp"));
                ImGui.DragFloat(LM.Get("GUI_Frame_EditorSettings_FarClipDist"), ref Program.Settings.RenderDistance, 25, 150, 10000, "%0.1fm");
                ImGui.InputInt(LM.Get("GUI_Frame_EditorSettings_MaxFramerate"), ref Program.Settings.TargetFPS);
                // currentMsaa used to be a local int with no connection to Program.Settings.MSAA_Level
                // at all (never initialized from it, never written back to it) - the combo was
                // purely cosmetic and always showed "Disabled" regardless of the real, persisted
                // setting. Resync from the real value every frame (so external changes, e.g. the
                // Cancel button's ReloadSettings, are reflected too) and write straight back on edit.
                currentMsaa = (int)Program.Settings.MSAA_Level;
                if (ImGui.Combo(LM.Get("GUI_Frame_EditorSettings_MSAALevel"), ref currentMsaa, AAoptions, maxMsaa + 1))
                    Program.Settings.MSAA_Level = (uint)currentMsaa;
                ImGui.SameLine();
                ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_EditorSettings_RestartRequiredHelp"));
                if (ImGui.Checkbox(LM.Get("GUI_Frame_EditorSettings_VSync"), ref Program.Settings.VSync))
                    LunaWindow.Instance.GraphicsDevice.SyncToVerticalBlank = Program.Settings.VSync;
                ImGui.Checkbox(LM.Get("GUI_Frame_EditorSettings_UseFrustrumCulling"), ref Program.Settings.FrustrumCulling);
                ImGui.Checkbox(LM.Get("GUI_Frame_EditorSettings_BackfaceCulling"), ref Program.Settings.BackfaceCulling);
                ImGui.SameLine();
                ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_EditorSettings_BackfaceCullingHelp"));
                // Same resync-every-frame pattern as currentMsaa above (see that comment) -
                // applied live by Window.Update via AssetManager.SetTextureFiltering.
                currentFiltering = (int)Program.Settings.TextureFiltering;
                if (ImGui.Combo(LM.Get("GUI_Frame_EditorSettings_TextureFiltering"), ref currentFiltering, FilteringOptions, FilteringOptions.Length))
                    Program.Settings.TextureFiltering = (ReLunacy.Engine.Rendering.TextureFiltering)currentFiltering;
                ImGui.Checkbox(LM.Get("GUI_Frame_EditorSettings_EnableLighting"), ref Program.Settings.EnableLighting);
                ImGui.SameLine();
                ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_EditorSettings_EnableLightingHelp"));
                // Light direction/colour/ambient controls moved to the Level Data frame, which edits
                // the level's OWN lighting environment (section 0x8b00) - kept in one place rather
                // than split between here and there.
                if (ImGui.Combo(LM.Get("GUI_Frame_EditorSettings_Language"), ref selectedLanguage, Languages, Languages.Length))
                {
                    currLanguage = selectedLanguage;
                    string langCode = LM.Languages.Values.ElementAt(selectedLanguage).LangCode;
                    LM.TrySetLanguage(langCode);
                }
                if (ImGui.BeginCombo(LM.Get("GUI_Frame_EditorSettings_UpdateChannel"), Program.Settings.UpdateChannel.ToString()))
                {
                    foreach (var channel in Enum.GetValues<UpdateChannel>())
                    {
                        if (ImGui.Selectable($"\t {channel}", channel == Program.Settings.UpdateChannel))
                            Program.Settings.UpdateChannel = channel;
                    }
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
                ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_EditorSettings_UpdateChannelHelp"));
                if (ImGui.CollapsingHeader(LM.Get("GUI_Common_AdvancedCollapsed")))
                {
                    ImGui.Text(LM.Get("GUI_Frame_EditorSettings_CustomShadersPlaceholder"));
                }
                ImGui.EndGroup();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EditorSettings_ToolsSettings")))
            {
                ImGui.BeginGroup();
                ImGui.DragFloat(LM.Get("GUI_Frame_EditorSettings_GizmosSize"), ref Program.Settings.ToolsGizmoSize, 0, 0, 0, "%.3f", ImGuiSliderFlags.AlwaysClamp);
                ImGui.Checkbox(LM.Get("GUI_Frame_EditorSettings_GizmoSnapEnabled"), ref Program.Settings.GizmoSnapEnabled);
                ImGui.InputFloat(LM.Get("GUI_Frame_EditorSettings_GizmoSnapTranslation"), ref Program.Settings.GizmoSnapTranslation, 0.1f, 1.0f, "%.3fm");
                ImGui.InputFloat(LM.Get("GUI_Frame_EditorSettings_GizmoSnapRotation"), ref Program.Settings.GizmoSnapRotation, 1.0f, 15.0f, "%.3f deg");
                ImGui.InputFloat(LM.Get("GUI_Frame_EditorSettings_GizmoSnapScale"), ref Program.Settings.GizmoSnapScale, 0.05f, 0.25f, "%.3f");
                ImGui.SliderFloat(LM.Get("GUI_Frame_EditorSettings_VolumeWireThickness"), ref Program.Settings.VolumeWireThickness, 0.01f, 5f, "%.2f", ImGuiSliderFlags.AlwaysClamp);
                ImGui.SameLine();
                ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_EditorSettings_VolumeWireThicknessHelp"));
                ImGui.ColorEdit4(LM.Get("GUI_Frame_EditorSettings_VolumeColor"), ref Program.Settings.VolumeColor);
                ImGui.ColorEdit4(LM.Get("GUI_Frame_EditorSettings_VolumeSelectedColor"), ref Program.Settings.VolumeSelectedColor);
                ImGui.ColorEdit4(LM.Get("GUI_Frame_EditorSettings_SelectionOutlineColor"), ref Program.Settings.SelectionOutlineColor);
                ImGui.EndGroup();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_EditorSettings_CameraSettings")))
            {
                ImGui.BeginGroup();
                ImGui.DragFloat(LM.Get("GUI_Frame_EditorSettings_CameraSpeed"), ref Program.Settings.CamMoveSpeed, 0.5f, 0.5f, 10000, "%0.2fm/s");
                ImGui.DragFloat(LM.Get("GUI_Frame_EditorSettings_CameraShiftSpeed"), ref Program.Settings.CamMaxSpeed, 0.5f, 0.5f, 10000, "%0.2fm/s");
                ImGui.SliderFloat(LM.Get("GUI_Frame_EditorSettings_FOV"), ref Program.Settings.CamFOV, 30f, 120f, "%0.1f deg");
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
                ImGui.Combo(LM.Get("GUI_Frame_EditorSettings_OverlayLocation"), ref Program.Settings.OverlayPos, ["Top-Left", "Top-Right", "Bottom-Left", "Bottom-Right", "Center..?"], 5);
                ImGui.EndGroup();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Debug settings"))
            {
                ImGui.BeginGroup();
                ImGui.Checkbox("Enable Debug", ref Program.Settings.DebugMode);
                ImGui.Combo("Logging Level", ref currentLogLevel, ["Debug", "Info", "Warning", "Error", "Fatal"], 5);
                ImGui.EndGroup();
                ImGui.EndTabItem();
            }
        }
        ImGui.EndTabBar();
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.BeginGroup();
        if (ImGui.Button(LM.Get("GUI_Frame_EditorSettings_SaveApply")))
        {
            LunaWindow.Instance.SetTargetFPS(Program.Settings.TargetFPS);
            Program.Settings.LogLevel = (LunaLog.LogLevel)currentLogLevel;
            Program.Settings.SaveSettingsToFile();
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord")))
        {
            Program.Settings.ReloadSettings();
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord")))
        {
            Program.Settings.ReloadSettings();
            isOpen = false;
        }
        ImGui.EndGroup();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowSize(new Vector2(800, 600));
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Once, new Vector2(0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
