using System.Numerics;
using ReLunacy.Engine.Export;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>Lets the user pick which categories (Mobys/Ties/UFrags) to include before exporting
/// the whole loaded level to a single .glb — see LevelExporter for why this is glTF-only (OBJ has
/// no node hierarchy or mesh instancing, both of which whole-level export depends on).</summary>
public class LevelExportModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private bool exportMobys;
    private bool exportTies = true;
    private bool exportUFrags = true;

    public LevelExportModal()
    {
        FrameName = LM.Get("GUI_Frame_LevelExportModal_Title");
    }

    protected override void Render(double deltaTime)
    {
        ImGui.Checkbox(LM.Get("GUI_Frame_LevelExportModal_Mobys"), ref exportMobys);
        ImGui.Checkbox(LM.Get("GUI_Frame_LevelExportModal_Ties"), ref exportTies);
        ImGui.Checkbox(LM.Get("GUI_Frame_LevelExportModal_UFrags"), ref exportUFrags);

        ImGui.Spacing();

        bool anySelected = exportMobys || exportTies || exportUFrags;
        if (!anySelected)
            ImGui.TextColored(new Vector4(0.9f, 0.3f, 0.3f, 1f), LM.Get("GUI_Frame_LevelExportModal_NothingSelected"));

        ImGui.BeginDisabled(!anySelected);
        if (ImGui.Button(LM.Get("GUI_Frame_LevelExportModal_ExportButton")))
        {
            StartExport();
            isOpen = false;
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord")))
            isOpen = false;
    }

    private void StartExport()
    {
        string levelName = ExportPaths.SanitizeFileName(Path.GetFileName(Program.ProvidedPath.TrimEnd(Path.DirectorySeparatorChar)));
        string directory = Path.Combine(Program.EditorPath, "Exported", "Levels");
        string path = Path.Combine(directory, $"{levelName}.glb");
        var options = new LevelExportOptions(exportMobys, exportTies, exportUFrags);

        ExportRunner.Run(LM.Get("GUI_Frame_LevelExportModal_ExportingTitle"), path, directory,
            progress => LevelExporter.Export(path, levelName, EntityManager.Singleton, options, progress));
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
