using ImGuiNET;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Core.Frames;

internal class FileSelectionDialog : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoDocking;

    public FileSelectionDialog() : base()
    {
        FrameName = LM.Get("GUI_Frame_LevelSelection");
    }

    public string levelPath = "";

    protected override void Render(double deltaTime)
    {
        ImGui.BeginGroup();
        ImGui.Text(LM.Get("GUI_Frame_OpenLevel_LevelPath"));
        ImGui.SameLine();
        ImGui.InputTextWithHint("##", "C:\\NPEA00088\\packed\\levels\\metropolis\\main.dat", ref levelPath, 256);
        ImGui.SameLine();
        ImGui.Button("...");
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Frame_OpenLevel_PasteClipboard")))
        {
            try
            {
                if (ImGui.GetClipboardText() != null)
                    levelPath = ImGui.GetClipboardText();
            }
            catch (Exception e)
            {
                LunaLog.LogError($"Unable to paste from clipboard: {e}");
            }
        }


        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) isOpen = false;
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_LoadWord")))
        {
            if (levelPath == "")
            {
                Console.WriteLine("Level Path is empty!");
            }
            else
            {
                Program.ProvidedPath = levelPath;
                var lm = new LoadingModal(LM.Get("GUI_LoadLevelModal_Title"), 1);
                Task.Run(() => LunaWindow.Instance.LoadLevelDataAsync(levelPath, lm));
                isOpen = false;
            }
        }
        ImGui.EndGroup();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(450, 100));
        base.RenderAsWindow(deltaTime);
    }
}
