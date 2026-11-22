using System.Numerics;
using System.Runtime.Versioning;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

public class StartupConfigModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.UnsavedDocument | ImGuiWindowFlags.NoResize;

    private bool _createDesktopShortcut;
    private bool _hideNextTimes = true;

    public StartupConfigModal()
    {
        FrameName = LM.Get("GUI_StartupConfigModal_Frame");
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowSize(new Vector2(500, 0), ImGuiCond.Always);
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }

    protected override void Render(double deltaTime)
    {
        void Row(string label)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(LM.Get(label));
            ImGui.TableNextColumn();
        }

        ImGui.BeginGroup();
        ImGuiPlus.CenteredText(LM.Get("GUI_StartupConfigModal_Title"));
        ImGuiPlus.CenteredText(LM.Get("GUI_StartupConfigModal_Subtitle", ProgramInfo.Version), disabled: true);
        ImGui.Spacing();
        ImGuiPlus.CenteredText(LM.Get("GUI_StartupConfigModal_Message"));
        ImGui.EndGroup();
        if (ImGui.BeginTable("##configTable", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersOuter))
        {
            Row("GUI_StartupConfigModal_ConfigUpdateChannel");
            if (ImGui.BeginCombo("###startup_update_channel", Program.Settings.UpdateChannel.ToString()))
            {
                foreach (var channel in Enum.GetValues<UpdateChannel>())
                {
                    if (ImGui.Selectable($"{channel}", channel == Program.Settings.UpdateChannel))
                        Program.Settings.UpdateChannel = channel;
                }
                ImGui.EndCombo();
            }
            Row("GUI_StartupConfigModal_ConfigInculdePrereleases");
            ImGui.Checkbox("###startup_include_prerelease", ref Program.Settings.IncludePrereleases);
            Row("GUI_StartupConfigModal_CreateDesktopShortcut");
            ImGui.Checkbox("###startup_desktop_shortcut", ref _createDesktopShortcut);
            ImGui.EndTable();
        }

        ImGui.Checkbox(LM.Get("GUI_StartupConfigModal_HideNextTimes"), ref _hideNextTimes);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(25,  180, 25, 255) / 255);
        if (ImGuiPlus.CenteredButton(LM.Get("GUI_StartupConfigModal_ConfirmAndClose")))
        {
            SaveSettings();
        }
        ImGui.PopStyleColor();
    }

    private void SaveSettings()
    {
        Program.Settings.HideStartupConfigMessage = _hideNextTimes;
        Program.Settings.SaveSettingsToFile();

        if (_createDesktopShortcut)
            DesktopShortcutUtils.CreateDesktopShortcut();

        isOpen = false;
    }
}
