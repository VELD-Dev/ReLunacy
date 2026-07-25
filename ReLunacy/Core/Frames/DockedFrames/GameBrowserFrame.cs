using System.Numerics;
using Bliss.CSharp.Interact;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Engine.Games;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

public class GameBrowserFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private string rootPathInput = "";
    private GameLibrary? library;
    private string statusMessage = "";

    private string debugDatPathInput = "";
    private string debugDatStatusMessage = "";

    public GameBrowserFrame()
    {
        FrameName = LM.Get("GUI_Frame_GameBrowser");
    }

    protected override void Render(double deltaTime)
    {
        if (!ImGui.BeginTabBar("game_browser_tabs"))
            return;

        if (ImGui.BeginTabItem("Levels"))
        {
            RenderLevelsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Load a debug.dat (old engine only)"))
        {
            RenderDebugDatTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void RenderLevelsTab()
    {
        ImGui.Text("USRDIR:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(400);
        ImGui.InputTextWithHint("##usrdir_path", "C:\\NPEA00088\\USRDIR", ref rootPathInput, 512);

        ImGui.SameLine();
        if (ImGui.Button("..."))
        {
            var result = NativeFileDialogSharp.Dialog.FolderPicker(rootPathInput);
            if (result.IsOk) rootPathInput = result.Path;
        }

        ImGui.SameLine();
        if (ImGui.Button("Paste"))
        {
            try
            {
                var clipboard = Input.GetClipboardText();
                if (!string.IsNullOrEmpty(clipboard)) rootPathInput = clipboard;
            }
            catch (Exception e)
            {
                LunaLog.LogError($"Unable to paste from clipboard: {e}");
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Scan"))
        {
            Scan();
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            ImGui.TextColored(new Vector4(0.9f, 0.3f, 0.3f, 1f), statusMessage);
        }

        ImGui.Separator();

        if (library == null)
        {
            ImGui.TextWrapped("Point at a game's USRDIR and click Scan to find levels.");
            return;
        }

        ImGui.Text(library.DetectedGame != null
            ? $"Detected: {library.DetectedGame.DisplayName}"
            : "Detected: Unknown game (no level names matched yet)");
        ImGui.Text($"Levels found: {library.Levels.Count}");
        ImGui.Separator();

        if (ImGui.BeginChild("game_browser_levels", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            foreach (var level in library.Levels)
            {
                ImGui.Text(level.Name);
                ImGui.SameLine();
                ImGui.TextDisabled(level.SourceKind == LevelSourceKind.Psarc ? "[psarc]" : "[folder]");
                if ((library.DetectedGame?.IsOldEngine ?? true) && level.DebugDatPath == null)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("[no debug.dat found]");
                }
                ImGui.SameLine();
                if (ImGui.Button($"Load##level_{level.Name}"))
                {
                    LoadLevel(level);
                }
            }
        }
        ImGui.EndChild();
    }

    private void RenderDebugDatTab()
    {
        ImGui.TextWrapped("Old-engine levels usually don't ship debug.dat alongside their own " +
            "data — it's auto-detected next to the level when possible. Use this if a level loaded " +
            "without instance/asset names, or to load a different debug.dat than the one that was " +
            "auto-detected.");
        ImGui.Separator();

        ImGui.Text("debug.dat:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(400);
        ImGui.InputTextWithHint("##debugdat_path", "C:\\NPEA00088\\USRDIR\\built\\levels\\prologue\\debug.dat", ref debugDatPathInput, 512);

        ImGui.SameLine();
        if (ImGui.Button("...##debugdat_browse"))
        {
            var result = NativeFileDialogSharp.Dialog.FileOpen();
            if (result.IsOk) debugDatPathInput = result.Path;
        }

        ImGui.SameLine();
        if (ImGui.Button("Load##debugdat_load"))
        {
            debugDatStatusMessage = "";
            if (!File.Exists(debugDatPathInput))
            {
                debugDatStatusMessage = "That file doesn't exist.";
            }
            else if (LunaWindow.Instance.Level == null)
            {
                // No level loaded yet: just remember it, it'll be picked up on the next load.
                LunaWindow.Instance.PendingExternalDebugDatPath = debugDatPathInput;
                debugDatStatusMessage = "Saved — it'll be used the next time a level is loaded.";
            }
            else
            {
                var loadingModal = new LoadingModal(LM.Get("GUI_LoadLevelModal_Title"), 1);
                LunaWindow.Instance.AddFrame(loadingModal);
                LunaWindow.Instance.LoadExternalDebugDatAndReload(debugDatPathInput, loadingModal);
            }
        }

        if (!string.IsNullOrEmpty(debugDatStatusMessage))
        {
            ImGui.TextColored(new Vector4(0.9f, 0.3f, 0.3f, 1f), debugDatStatusMessage);
        }
    }

    private void Scan()
    {
        statusMessage = "";
        if (string.IsNullOrWhiteSpace(rootPathInput) || !Directory.Exists(rootPathInput))
        {
            statusMessage = "That path doesn't exist.";
            library = null;
            return;
        }

        try
        {
            library = GameLibraryScanner.Scan(rootPathInput);
            if (library.Levels.Count == 0)
                statusMessage = "No levels found under this folder.";
        }
        catch (Exception e)
        {
            LunaLog.LogError($"Failed to scan game folder: {e}");
            statusMessage = $"Scan failed: {e.Message}";
            library = null;
        }
    }

    private static void LoadLevel(Level level)
    {
        var loadingModal = new LoadingModal(LM.Get("GUI_LoadLevelModal_Title"), 1);
        LunaWindow.Instance.AddFrame(loadingModal);
        LunaWindow.Instance.LoadLevelDataAsync(level.SourcePath, loadingModal, level.DebugDatPath);
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.Once);
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Once, new Vector2(0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
